using AwtrixSharpWeb.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using System.Net;

namespace Test.Http
{
    /// <summary>
    /// CR-15: hosts Program.AddHttpSurface/ConfigureHttpPipeline on TestServer with in-memory configuration and
    /// two minimal endpoints. No Awtrix services, broker or network are involved.
    /// </summary>
    public class HttpPipelineTests
    {
        private static async Task<WebApplication> StartAsync(string environment, Dictionary<string, string?>? settings = null)
        {
            // CreateBuilder loads appsettings.json from the content root while it is constructed, before the
            // Sources.Clear() below. The test output folder carries an empty (BOM-only) appsettings.json that does
            // not parse, so host over a fresh empty folder instead.
            var contentRoot = Path.Combine(Path.GetTempPath(), "awtrixsharp-ws7-http-" + Guid.NewGuid());
            Directory.CreateDirectory(contentRoot);

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = environment,
                ContentRootPath = contentRoot,
            });
            builder.WebHost.UseTestServer();
            builder.Configuration.Sources.Clear();
            builder.Configuration.AddInMemoryCollection(settings ?? new Dictionary<string, string?>());

            AwtrixSharpWeb.Program.AddHttpSurface(builder.Services, builder.Configuration);

            var app = builder.Build();
            AwtrixSharpWeb.Program.ConfigureHttpPipeline(app);
            app.MapGet("/test/ping", () => "pong");
            app.MapGet("/test/boom", new Func<string>(() => throw new InvalidOperationException("secret-detail-ws7")));

            await app.StartAsync();
            return app;
        }

        [Fact]
        public async Task Production_UnhandledException_Returns500ProblemDetails_WithoutExceptionDetail()
        {
            await using var app = await StartAsync("Production");

            var response = await app.GetTestClient().GetAsync("/test/boom");
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            Assert.DoesNotContain("secret-detail-ws7", body);
            Assert.DoesNotContain("InvalidOperationException", body);
        }

        [Fact]
        public async Task Development_UnhandledException_ShowsExceptionDetail()
        {
            await using var app = await StartAsync("Development");

            var response = await app.GetTestClient().GetAsync("/test/boom");
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Contains("secret-detail-ws7", body);
        }

        [Fact]
        public async Task Swagger_IsEnabledByDefault()
        {
            await using var app = await StartAsync("Production");

            var response = await app.GetTestClient().GetAsync("/swagger/v1/swagger.json");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task Swagger_CanBeDisabled()
        {
            await using var app = await StartAsync("Production", new() { ["Swagger:Enabled"] = "false" });

            var response = await app.GetTestClient().GetAsync("/swagger/v1/swagger.json");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task Swagger_InvalidEnabledValue_StaysEnabled()
        {
            await using var app = await StartAsync("Production", new() { ["Swagger:Enabled"] = "maybe" });

            var response = await app.GetTestClient().GetAsync("/swagger/v1/swagger.json");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task ApiKey_Unset_RequestsPassWithoutHeader()
        {
            await using var app = await StartAsync("Production");

            var response = await app.GetTestClient().GetAsync("/test/ping");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task ApiKey_Set_MissingHeader_Returns401ProblemDetails()
        {
            await using var app = await StartAsync("Production", new() { ["Api:Key"] = "s3cret" });

            var response = await app.GetTestClient().GetAsync("/test/ping");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }

        [Fact]
        public async Task ApiKey_Set_WrongHeader_Returns401()
        {
            await using var app = await StartAsync("Production", new() { ["Api:Key"] = "s3cret" });
            var client = app.GetTestClient();
            client.DefaultRequestHeaders.Add(ApiKeyMiddleware.HeaderName, "wrong");

            var response = await client.GetAsync("/test/ping");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task ApiKey_Set_CorrectHeader_PassesThrough_IgnoringSurroundingWhitespace()
        {
            await using var app = await StartAsync("Production", new() { ["Api:Key"] = "s3cret\n" });
            var client = app.GetTestClient();
            client.DefaultRequestHeaders.Add(ApiKeyMiddleware.HeaderName, "s3cret");

            var response = await client.GetAsync("/test/ping");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("pong", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task ApiKey_Set_SwaggerJsonStillServed_AndDocumentsHeader()
        {
            await using var app = await StartAsync("Production", new() { ["Api:Key"] = "s3cret" });

            var response = await app.GetTestClient().GetAsync("/swagger/v1/swagger.json");
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains(ApiKeyMiddleware.HeaderName, body);
        }

        [Theory]
        [InlineData("s3cret", "s3cret", true)]
        [InlineData(" s3cret ", "s3cret", true)]
        [InlineData("S3CRET", "s3cret", false)]
        [InlineData("", "s3cret", false)]
        [InlineData(null, "s3cret", false)]
        public void IsMatch_ComparesTrimmedValuesExactly(string? supplied, string expected, bool match)
        {
            Assert.Equal(match, ApiKeyMiddleware.IsMatch(supplied, expected));
        }
    }
}

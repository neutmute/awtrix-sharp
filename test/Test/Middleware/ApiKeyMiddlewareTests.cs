using AwtrixSharpWeb.Middleware;
using Microsoft.AspNetCore.Http;

namespace Test.Middleware
{
    public class ApiKeyMiddlewareTests
    {
        [Theory]
        [InlineData("/ui", true)]
        [InlineData("/ui/visuals", true)]
        [InlineData("/UI", true)]
        [InlineData("/_blazor", true)]
        [InlineData("/_blazor/negotiate", true)]
        [InlineData("/_framework/blazor.web.js", true)]
        [InlineData("/uix", false)]
        [InlineData("/diagnostics", false)]
        [InlineData("/", false)]
        [InlineData("/mqtt/publish", false)]
        public void IsOpenPath(string path, bool expected)
        {
            Assert.Equal(expected, ApiKeyMiddleware.IsOpenPath(new PathString(path)));
        }
    }
}

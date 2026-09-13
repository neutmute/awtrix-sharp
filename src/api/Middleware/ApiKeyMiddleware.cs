using AwtrixSharpWeb.Domain;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace AwtrixSharpWeb.Middleware
{
    /// <summary>
    /// When Api:Key is set, every request reaching this middleware must carry the key in the X-Api-Key header.
    /// Swagger is registered earlier in the pipeline, so its UI and JSON stay reachable. Read per request via
    /// IOptionsMonitor so an appsettings reload applies without restart.
    /// </summary>
    public class ApiKeyMiddleware
    {
        public const string HeaderName = "X-Api-Key";

        private readonly RequestDelegate _next;
        private readonly IOptionsMonitor<ApiSettings> _settings;
        private readonly ILogger<ApiKeyMiddleware> _logger;

        public ApiKeyMiddleware(RequestDelegate next, IOptionsMonitor<ApiSettings> settings, ILogger<ApiKeyMiddleware> logger)
        {
            _next = next;
            _settings = settings;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var expected = _settings.CurrentValue.Key;

            if (string.IsNullOrWhiteSpace(expected) || IsMatch(context.Request.Headers[HeaderName].ToString(), expected))
            {
                await _next(context);
                return;
            }

            _logger.LogWarning(
                "Rejected {Method} {Path} from {RemoteIp}: missing or invalid {Header} header",
                context.Request.Method,
                context.Request.Path,
                context.Connection.RemoteIpAddress,
                HeaderName);

            await Results.Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Missing or invalid API key",
                    detail: $"Send the configured API key in the {HeaderName} header.")
                .ExecuteAsync(context);
        }

        /// <summary>
        /// Trimmed, case-sensitive, constant-time comparison (hashing first equalises lengths).
        /// </summary>
        internal static bool IsMatch(string? supplied, string expected)
        {
            if (string.IsNullOrWhiteSpace(supplied))
            {
                return false;
            }

            var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(supplied.Trim()));
            var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected.Trim()));
            return CryptographicOperations.FixedTimeEquals(suppliedHash, expectedHash);
        }
    }
}

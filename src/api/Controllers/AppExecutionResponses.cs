using AwtrixSharpWeb.HostedServices;
using Microsoft.AspNetCore.Mvc;

namespace AwtrixSharpWeb.Controllers
{
    /// <summary>
    /// Maps Conductor.ExecuteNow results to HTTP responses: Started 200, NotFound 404, Error 500.
    /// </summary>
    internal static class AppExecutionResponses
    {
        public static IActionResult ToActionResult(this ControllerBase controller, AppExecutionResult result, string appName, string baseTopic)
        {
            return result switch
            {
                AppExecutionResult.Started => controller.Ok(new { message = $"App '{appName}' started on device '{baseTopic}'" }),
                AppExecutionResult.NotFound => controller.NotFound(new { message = $"App '{appName}' is not running on device '{baseTopic}'" }),
                _ => controller.StatusCode(StatusCodes.Status500InternalServerError,
                        new { message = $"App '{appName}' failed to start on device '{baseTopic}'; see the service log" })
            };
        }
    }
}

using Microsoft.AspNetCore.Diagnostics;

namespace LeadIntegration.Api.Infrastructure;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var statusCode = exception is BadHttpRequestException badRequest
            ? badRequest.StatusCode
            : StatusCodes.Status500InternalServerError;

        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception while processing {RequestPath}", httpContext.Request.Path);
        }

        var result = Results.Problem(
            statusCode: statusCode,
            title: statusCode == StatusCodes.Status400BadRequest
                ? "Malformed request"
                : "An unexpected server error occurred.",
            detail: statusCode == StatusCodes.Status400BadRequest
                ? "The request body could not be read as valid JSON."
                : "The server could not complete the request.");

        await result.ExecuteAsync(httpContext);
        return true;
    }
}

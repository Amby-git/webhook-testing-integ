using Microsoft.Extensions.Options;

namespace Salesforce.Simulator.Api.Features.FailureSimulation;

public sealed class SimulatedFailureFilter(IOptions<SalesforceSimulatorOptions> options) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var failureMode = options.Value.FailureMode;
        var statusCode = failureMode switch
        {
            SalesforceFailureMode.TooManyRequests => StatusCodes.Status429TooManyRequests,
            SalesforceFailureMode.InternalServerError => StatusCodes.Status500InternalServerError,
            SalesforceFailureMode.ServiceUnavailable => StatusCodes.Status503ServiceUnavailable,
            _ => 0
        };

        if (statusCode == 0)
        {
            return await next(context);
        }

        if (statusCode == StatusCodes.Status429TooManyRequests)
        {
            context.HttpContext.Response.Headers.RetryAfter = "5";
        }

        return Results.Problem(
            statusCode: statusCode,
            title: "Simulated Salesforce failure",
            detail: $"The simulator is configured to return HTTP {statusCode} for lead operations.",
            extensions: new Dictionary<string, object?> { ["simulated"] = true });
    }
}

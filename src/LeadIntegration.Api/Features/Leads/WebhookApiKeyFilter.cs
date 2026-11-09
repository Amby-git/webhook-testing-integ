using System.Security.Cryptography;
using System.Text;

namespace LeadIntegration.Api.Features.Leads;

public sealed class WebhookApiKeyFilter(IConfiguration configuration) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var configuredKey = configuration["WebhookAuthentication:ApiKey"];
        var providedKey = context.HttpContext.Request.Headers["X-Webhook-Api-Key"].ToString();

        if (string.IsNullOrWhiteSpace(configuredKey) || string.IsNullOrWhiteSpace(providedKey))
        {
            return Unauthorized();
        }

        var expectedBytes = Encoding.UTF8.GetBytes(configuredKey);
        var providedBytes = Encoding.UTF8.GetBytes(providedKey);

        if (expectedBytes.Length != providedBytes.Length ||
            !CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes))
        {
            return Unauthorized();
        }

        return await next(context);
    }

    private static IResult Unauthorized() => Results.Problem(
        statusCode: StatusCodes.Status401Unauthorized,
        title: "Unauthorized",
        detail: "A valid X-Webhook-Api-Key header is required.");
}

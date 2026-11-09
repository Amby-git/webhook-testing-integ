using System.Diagnostics;
using LeadIntegration.Api.Contracts.Marketing;
using LeadIntegration.Api.Features.Leads;
using LeadIntegration.Api.Features.RequestCatcher;

namespace LeadIntegration.Api.Features.Messaging;

public static class LeadEventEndpoints
{
    public static IEndpointRouteBuilder MapLeadEventEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/webhooks/leads/async", AcceptLeadAsync)
            .AddEndpointFilter<WebhookApiKeyFilter>()
            .RequireRateLimiting("lead-webhook")
            .WithName("AcceptLeadEventAsync")
            .WithTags("Webhooks")
            .WithSummary("Publishes a validated lead event for asynchronous processing.")
            .WithDescription("Returns 202 only after RabbitMQ confirms the message publish. This route does not wait for Salesforce processing to finish.")
            .Accepts<MarketingLeadWebhookRequest>("application/json")
            .Produces<LeadEventAcceptedResponse>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        endpoints.MapGet("/integration/events/{sourcePlatform}/{eventId}", GetEventStatus)
            .AddEndpointFilter<WebhookApiKeyFilter>()
            .WithName("GetLeadEventStatus")
            .WithTags("Integration Events")
            .WithSummary("Returns the in-memory processing status for a lead event.")
            .Produces<LeadEventStatus>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> AcceptLeadAsync(
        MarketingLeadWebhookRequest request,
        HttpContext httpContext,
        ILeadEventAcceptanceService service,
        IInboundWebhookObserver inboundWebhookObserver,
        CancellationToken cancellationToken)
    {
        var correlationId = GetOrCreateCorrelationId(httpContext);
        httpContext.Response.Headers["X-Correlation-Id"] = correlationId;

        var observationTask = inboundWebhookObserver.ObserveAsync(
            request,
            httpContext.Request.ContentType ?? "application/json",
            correlationId,
            cancellationToken);
        var result = await service.AcceptAsync(request, correlationId, cancellationToken);
        await observationTask;
        return result.Outcome switch
        {
            LeadEventAcceptanceOutcome.Accepted => AcceptedResult(result.Event!),
            LeadEventAcceptanceOutcome.ValidationFailed => Results.ValidationProblem(
                result.ValidationErrors!, statusCode: StatusCodes.Status400BadRequest),
            LeadEventAcceptanceOutcome.Duplicate => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Duplicate lead event",
                detail: "An event with this source platform and event ID has already been accepted."),
            LeadEventAcceptanceOutcome.BrokerUnavailable => Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Message broker unavailable",
                detail: "The event could not be confirmed by RabbitMQ and was not accepted."),
            _ => throw new InvalidOperationException("Unexpected lead event acceptance result.")
        };
    }

    private static IResult GetEventStatus(
        string sourcePlatform,
        string eventId,
        ILeadEventStore eventStore)
    {
        var status = eventStore.Get(sourcePlatform, eventId);
        return status is null
            ? Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Event not found",
                detail: "No accepted event matches the supplied source platform and event ID.")
            : Results.Ok(status);
    }

    private static IResult AcceptedResult(LeadEventStatus eventStatus)
    {
        var statusUrl = $"/integration/events/{Uri.EscapeDataString(eventStatus.SourcePlatform)}/{Uri.EscapeDataString(eventStatus.EventId)}";
        var response = new LeadEventAcceptedResponse(
            eventStatus.EventId,
            eventStatus.SourcePlatform,
            eventStatus.CorrelationId,
            eventStatus.Status.ToString(),
            statusUrl,
            eventStatus.CreatedAt);
        return Results.Accepted(statusUrl, response);
    }

    private static string GetOrCreateCorrelationId(HttpContext httpContext)
    {
        var providedCorrelationId = httpContext.Request.Headers["X-Correlation-Id"].ToString();
        return !string.IsNullOrWhiteSpace(providedCorrelationId)
            ? providedCorrelationId.Trim()
            : Activity.Current?.TraceId.ToString() ?? httpContext.TraceIdentifier;
    }
}

using LeadIntegration.Api.Contracts.Marketing;
using LeadIntegration.Api.Features.Leads;
using LeadIntegration.Api.Features.Salesforce;

namespace LeadIntegration.Api.Features.Messaging;

public sealed class LeadEventAcceptanceService(
    MarketingLeadValidator validator,
    SalesforceLeadMapper mapper,
    ILeadEventStore eventStore,
    ILeadMessagePublisher publisher,
    ILogger<LeadEventAcceptanceService> logger) : ILeadEventAcceptanceService
{
    public async Task<LeadEventAcceptanceResult> AcceptAsync(
        MarketingLeadWebhookRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var validationErrors = validator.Validate(request);
        if (validationErrors.Count > 0)
        {
            logger.LogWarning(
                "Lead event validation failed for {EventId} from {SourcePlatform} with correlation {CorrelationId}",
                request.EventId,
                request.SourcePlatform,
                correlationId);
            return new LeadEventAcceptanceResult(
                LeadEventAcceptanceOutcome.ValidationFailed,
                ValidationErrors: validationErrors);
        }

        var sourcePlatform = request.SourcePlatform!.Trim();
        var eventId = request.EventId!.Trim();
        if (!eventStore.TryReserve(sourcePlatform, eventId))
        {
            logger.LogInformation(
                "Duplicate lead event {EventId} from {SourcePlatform} rejected with correlation {CorrelationId}",
                eventId,
                sourcePlatform,
                correlationId);
            return new LeadEventAcceptanceResult(LeadEventAcceptanceOutcome.Duplicate);
        }

        var message = new LeadProcessingMessage(
            eventId,
            sourcePlatform,
            correlationId,
            DateTimeOffset.UtcNow,
            mapper.Map(request));

        try
        {
            await publisher.PublishAsync(message, cancellationToken);
            var eventStatus = eventStore.MarkAccepted(message);
            logger.LogInformation(
                "Lead event {EventId} from {SourcePlatform} accepted by RabbitMQ with correlation {CorrelationId}",
                eventId,
                sourcePlatform,
                correlationId);
            return new LeadEventAcceptanceResult(LeadEventAcceptanceOutcome.Accepted, eventStatus);
        }
        catch (RabbitMqUnavailableException)
        {
            eventStore.ReleaseReservation(sourcePlatform, eventId);
            return new LeadEventAcceptanceResult(LeadEventAcceptanceOutcome.BrokerUnavailable);
        }
        catch
        {
            eventStore.ReleaseReservation(sourcePlatform, eventId);
            throw;
        }
    }
}

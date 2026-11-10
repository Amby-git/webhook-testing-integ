using LeadIntegration.Api.Features.Salesforce;

namespace LeadIntegration.Api.Features.Messaging;

public sealed class LeadMessageProcessor(
    ISalesforceLeadClient salesforceClient,
    ILeadEventStore eventStore,
    LeadProcessingMessageValidator validator,
    ILogger<LeadMessageProcessor> logger) : ILeadMessageProcessor
{
    public async Task<LeadMessageProcessingResult> ProcessAsync(
        LeadProcessingMessage message,
        int attemptCount,
        CancellationToken cancellationToken)
    {
        var validationFailure = validator.Validate(message);
        if (validationFailure is not null)
        {
            return new LeadMessageProcessingResult(
                LeadMessageProcessingOutcome.PermanentFailure,
                FailureReason: validationFailure);
        }

        var current = eventStore.Get(message.SourcePlatform, message.EventId);
        if (current?.Status == LeadEventProcessingStatus.Completed)
        {
            return new LeadMessageProcessingResult(
                LeadMessageProcessingOutcome.AlreadyCompleted,
                current.SalesforceId);
        }

        eventStore.MarkProcessing(message, attemptCount);
        logger.LogInformation("Starting Salesforce lead processing for event {EventId} from {SourcePlatform}, correlation {CorrelationId}, attempt {AttemptCount}",
            message.EventId, message.SourcePlatform, message.CorrelationId, attemptCount);

        var result = await salesforceClient.CreateLeadAsync(
            message.Lead,
            cancellationToken,
            $"{message.SourcePlatform.Trim().ToUpperInvariant()}:{message.EventId.Trim()}");

        if (result.Succeeded)
        {
            eventStore.MarkCompleted(message, attemptCount, result.Value!.Id);
            logger.LogInformation("Lead event {EventId} from {SourcePlatform} completed as Salesforce record {SalesforceId}",
                message.EventId, message.SourcePlatform, result.Value.Id);
            return new LeadMessageProcessingResult(LeadMessageProcessingOutcome.Completed, result.Value.Id);
        }

        var failureReason = $"Salesforce request failed with {result.Failure}.";
        if (IsTransient(result.Failure))
        {
            logger.LogWarning("Transient Salesforce failure for event {EventId} from {SourcePlatform}: {Failure}",
                message.EventId, message.SourcePlatform, result.Failure);
            return new LeadMessageProcessingResult(
                LeadMessageProcessingOutcome.TransientFailure,
                FailureReason: failureReason,
                RetryAfter: result.RetryAfter,
                SalesforceFailure: result.Failure);
        }

        logger.LogWarning("Permanent Salesforce failure for event {EventId} from {SourcePlatform}: {Failure}",
            message.EventId, message.SourcePlatform, result.Failure);
        return new LeadMessageProcessingResult(
            LeadMessageProcessingOutcome.PermanentFailure,
            FailureReason: failureReason,
            SalesforceFailure: result.Failure);
    }

    private static bool IsTransient(SalesforceClientFailure failure) => failure is
        SalesforceClientFailure.RateLimited or SalesforceClientFailure.InternalServerError or
        SalesforceClientFailure.BadGateway or SalesforceClientFailure.Unavailable or
        SalesforceClientFailure.GatewayTimeout or SalesforceClientFailure.Network or SalesforceClientFailure.Timeout;
}

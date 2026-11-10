namespace LeadIntegration.Api.Features.Messaging;

public interface ILeadEventStore
{
    bool TryReserve(string sourcePlatform, string eventId);
    LeadEventStatus MarkAccepted(LeadProcessingMessage message);
    LeadEventStatus MarkProcessing(LeadProcessingMessage message, int attemptCount);
    LeadEventStatus MarkCompleted(LeadProcessingMessage message, int attemptCount, string salesforceId);
    LeadEventStatus MarkFailed(LeadProcessingMessage message, int attemptCount, string failureReason);
    void ReleaseReservation(string sourcePlatform, string eventId);
    LeadEventStatus? Get(string sourcePlatform, string eventId);
    IReadOnlyCollection<LeadEventStatus> GetFailed();
}

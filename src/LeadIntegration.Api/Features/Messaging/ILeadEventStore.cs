namespace LeadIntegration.Api.Features.Messaging;

public interface ILeadEventStore
{
    bool TryReserve(string sourcePlatform, string eventId);
    LeadEventStatus MarkAccepted(LeadProcessingMessage message);
    void ReleaseReservation(string sourcePlatform, string eventId);
    LeadEventStatus? Get(string sourcePlatform, string eventId);
}

namespace LeadIntegration.Api.Features.Messaging;

public sealed class InMemoryLeadEventStore : ILeadEventStore
{
    private readonly object _sync = new();
    private readonly HashSet<EventIdentity> _reservations = [];
    private readonly Dictionary<EventIdentity, LeadEventStatus> _events = [];

    public bool TryReserve(string sourcePlatform, string eventId)
    {
        var identity = EventIdentity.Create(sourcePlatform, eventId);
        lock (_sync)
        {
            if (_events.ContainsKey(identity) || !_reservations.Add(identity))
            {
                return false;
            }

            return true;
        }
    }

    public LeadEventStatus MarkAccepted(LeadProcessingMessage message)
    {
        var identity = EventIdentity.Create(message.SourcePlatform, message.EventId);
        var now = DateTimeOffset.UtcNow;
        var record = new LeadEventStatus(
            message.EventId,
            message.SourcePlatform,
            message.CorrelationId,
            LeadEventProcessingStatus.Accepted,
            AttemptCount: 0,
            CreatedAt: message.AcceptedAt,
            LastUpdatedAt: now);

        lock (_sync)
        {
            _reservations.Remove(identity);
            _events[identity] = record;
            return record;
        }
    }

    public void ReleaseReservation(string sourcePlatform, string eventId)
    {
        lock (_sync)
        {
            _reservations.Remove(EventIdentity.Create(sourcePlatform, eventId));
        }
    }

    public LeadEventStatus? Get(string sourcePlatform, string eventId)
    {
        lock (_sync)
        {
            return _events.GetValueOrDefault(EventIdentity.Create(sourcePlatform, eventId));
        }
    }

    private readonly record struct EventIdentity(string SourcePlatform, string EventId)
    {
        public static EventIdentity Create(string sourcePlatform, string eventId) =>
            new(sourcePlatform.Trim().ToUpperInvariant(), eventId.Trim());
    }
}

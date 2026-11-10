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
            // The consumer can receive a confirmed publication immediately. Never let the
            // publisher's later Accepted update regress Processing/Completed/Failed state.
            if (_events.TryGetValue(identity, out var existing))
            {
                return existing;
            }

            _events.Add(identity, record);
            return record;
        }
    }

    public LeadEventStatus MarkProcessing(LeadProcessingMessage message, int attemptCount) =>
        Transition(message, LeadEventProcessingStatus.Processing, attemptCount);

    public LeadEventStatus MarkCompleted(LeadProcessingMessage message, int attemptCount, string salesforceId) =>
        Transition(message, LeadEventProcessingStatus.Completed, attemptCount, salesforceId: salesforceId);

    public LeadEventStatus MarkFailed(LeadProcessingMessage message, int attemptCount, string failureReason) =>
        Transition(message, LeadEventProcessingStatus.Failed, attemptCount, failureReason: failureReason);

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

    public IReadOnlyCollection<LeadEventStatus> GetFailed()
    {
        lock (_sync)
        {
            return _events.Values
                .Where(item => item.Status == LeadEventProcessingStatus.Failed)
                .OrderByDescending(item => item.LastUpdatedAt)
                .ToArray();
        }
    }

    private LeadEventStatus Transition(
        LeadProcessingMessage message,
        LeadEventProcessingStatus status,
        int attemptCount,
        string? failureReason = null,
        string? salesforceId = null)
    {
        var identity = EventIdentity.Create(message.SourcePlatform, message.EventId);
        lock (_sync)
        {
            var current = _events.GetValueOrDefault(identity);
            var updated = new LeadEventStatus(
                message.EventId,
                message.SourcePlatform,
                message.CorrelationId,
                status,
                Math.Max(attemptCount, current?.AttemptCount ?? 0),
                current?.CreatedAt ?? message.AcceptedAt,
                DateTimeOffset.UtcNow,
                failureReason,
                salesforceId ?? current?.SalesforceId);
            _events[identity] = updated;
            return updated;
        }
    }

    private readonly record struct EventIdentity(string SourcePlatform, string EventId)
    {
        public static EventIdentity Create(string sourcePlatform, string eventId) =>
            new(sourcePlatform.Trim().ToUpperInvariant(), eventId.Trim());
    }
}

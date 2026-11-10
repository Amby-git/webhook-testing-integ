using System.Collections.Concurrent;
using LeadIntegration.Contracts.Salesforce;

namespace Salesforce.Simulator.Api.Features.Leads;

public sealed class InMemorySalesforceLeadRepository : ISalesforceLeadRepository
{
    private readonly ConcurrentDictionary<string, SalesforceLeadRecord> _leads = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _recordIdsByIdempotencyKey = new(StringComparer.Ordinal);
    private readonly object _idempotencyLock = new();

    public SalesforceLeadRecord Create(SalesforceLeadDto lead)
    {
        while (true)
        {
            var id = $"00Q{Guid.NewGuid():N}"[..18].ToUpperInvariant();
            var record = SalesforceLeadRecord.From(id, lead);
            if (_leads.TryAdd(id, record))
            {
                return record;
            }
        }
    }

    public (SalesforceLeadRecord Record, bool Created) CreateOrGet(SalesforceLeadDto lead, string idempotencyKey)
    {
        lock (_idempotencyLock)
        {
            if (_recordIdsByIdempotencyKey.TryGetValue(idempotencyKey, out var existingId) &&
                _leads.TryGetValue(existingId, out var existing))
            {
                return (existing, false);
            }

            var created = Create(lead);
            _recordIdsByIdempotencyKey[idempotencyKey] = created.Id;
            return (created, true);
        }
    }

    public IReadOnlyCollection<SalesforceLeadRecord> GetAll() => _leads.Values.ToArray();

    public SalesforceLeadRecord? GetById(string id) =>
        _leads.TryGetValue(id, out var lead) ? lead : null;

    public SalesforceLeadRecord? Update(string id, SalesforceLeadDto lead)
    {
        while (_leads.TryGetValue(id, out var current))
        {
            var updated = SalesforceLeadRecord.From(id, lead);
            if (_leads.TryUpdate(id, updated, current))
            {
                return updated;
            }
        }

        return null;
    }
}

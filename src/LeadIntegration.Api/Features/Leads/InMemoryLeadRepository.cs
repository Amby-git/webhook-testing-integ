namespace LeadIntegration.Api.Features.Leads;

public sealed class InMemoryLeadRepository : ILeadRepository
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, Lead> _leads = [];
    private readonly Dictionary<string, Guid> _leadIdsByEmail = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _idempotencyKeys = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Guid> _pendingKeys = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Guid> _pendingEmails = new(StringComparer.OrdinalIgnoreCase);

    public LeadReservationResult TryReserve(string idempotencyKey, string email)
    {
        lock (_sync)
        {
            if (_idempotencyKeys.Contains(idempotencyKey) || _pendingKeys.ContainsKey(idempotencyKey))
            {
                return new LeadReservationResult(LeadReservationStatus.DuplicateRequest);
            }

            if (_leadIdsByEmail.ContainsKey(email) || _pendingEmails.ContainsKey(email))
            {
                return new LeadReservationResult(LeadReservationStatus.DuplicateEmail);
            }

            var reservation = new LeadReservation(Guid.NewGuid(), idempotencyKey, email);
            _pendingKeys.Add(idempotencyKey, reservation.Token);
            _pendingEmails.Add(email, reservation.Token);
            return new LeadReservationResult(LeadReservationStatus.Reserved, reservation);
        }
    }

    public bool Commit(Lead lead, LeadReservation reservation)
    {
        lock (_sync)
        {
            if (!_pendingKeys.TryGetValue(reservation.IdempotencyKey, out var keyToken) ||
                keyToken != reservation.Token ||
                !_pendingEmails.TryGetValue(reservation.Email, out var emailToken) ||
                emailToken != reservation.Token ||
                _leadIdsByEmail.ContainsKey(lead.Email))
            {
                return false;
            }

            _leads.Add(lead.Id, lead);
            _leadIdsByEmail.Add(lead.Email, lead.Id);
            _idempotencyKeys.Add(reservation.IdempotencyKey);
            ReleaseReservation(reservation);
            return true;
        }
    }

    public void Release(LeadReservation reservation)
    {
        lock (_sync)
        {
            ReleaseReservation(reservation);
        }
    }

    public IReadOnlyCollection<Lead> GetAll()
    {
        lock (_sync)
        {
            return _leads.Values.ToArray();
        }
    }

    public Lead? GetById(Guid id)
    {
        lock (_sync)
        {
            return _leads.GetValueOrDefault(id);
        }
    }

    public LeadUpdateResult TryUpdate(Lead lead)
    {
        lock (_sync)
        {
            if (!_leads.TryGetValue(lead.Id, out var currentLead))
            {
                return new LeadUpdateResult(LeadUpdateStatus.NotFound);
            }

            if (_leadIdsByEmail.TryGetValue(lead.Email, out var ownerId) && ownerId != lead.Id)
            {
                return new LeadUpdateResult(LeadUpdateStatus.DuplicateEmail);
            }

            _leadIdsByEmail.Remove(currentLead.Email);
            _leads[lead.Id] = lead;
            _leadIdsByEmail[lead.Email] = lead.Id;
            return new LeadUpdateResult(LeadUpdateStatus.Updated, lead);
        }
    }

    public bool Delete(Guid id)
    {
        lock (_sync)
        {
            if (!_leads.Remove(id, out var lead))
            {
                return false;
            }

            _leadIdsByEmail.Remove(lead.Email);
            return true;
        }
    }

    private void ReleaseReservation(LeadReservation reservation)
    {
        if (_pendingKeys.TryGetValue(reservation.IdempotencyKey, out var keyToken) && keyToken == reservation.Token)
        {
            _pendingKeys.Remove(reservation.IdempotencyKey);
        }

        if (_pendingEmails.TryGetValue(reservation.Email, out var emailToken) && emailToken == reservation.Token)
        {
            _pendingEmails.Remove(reservation.Email);
        }
    }
}

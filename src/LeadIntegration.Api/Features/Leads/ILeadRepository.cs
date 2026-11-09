namespace LeadIntegration.Api.Features.Leads;

public interface ILeadRepository
{
    LeadReservationResult TryReserve(string idempotencyKey, string email);
    bool Commit(Lead lead, LeadReservation reservation);
    void Release(LeadReservation reservation);
    IReadOnlyCollection<Lead> GetAll();
    Lead? GetById(Guid id);
    LeadUpdateResult TryUpdate(Lead lead);
    bool Delete(Guid id);
}

public enum LeadReservationStatus
{
    Reserved,
    DuplicateEmail,
    DuplicateRequest
}

public sealed record LeadReservation(Guid Token, string IdempotencyKey, string Email);

public sealed record LeadReservationResult(LeadReservationStatus Status, LeadReservation? Reservation = null);

public enum LeadUpdateStatus
{
    Updated,
    NotFound,
    DuplicateEmail
}

public sealed record LeadUpdateResult(LeadUpdateStatus Status, Lead? Lead = null);

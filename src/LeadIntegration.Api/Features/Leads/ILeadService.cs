using LeadIntegration.Api.Contracts.Marketing;

namespace LeadIntegration.Api.Features.Leads;

public interface ILeadService
{
    Task<LeadProcessingResult> ProcessAsync(
        MarketingLeadWebhookRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken);
    IReadOnlyCollection<Lead> GetAll();
    Lead? GetById(Guid id);
    LeadUpdateResult Update(Guid id, LeadRequest validatedRequest);
    bool Delete(Guid id);
}

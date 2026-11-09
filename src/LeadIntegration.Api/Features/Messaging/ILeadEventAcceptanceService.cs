using LeadIntegration.Api.Contracts.Marketing;

namespace LeadIntegration.Api.Features.Messaging;

public interface ILeadEventAcceptanceService
{
    Task<LeadEventAcceptanceResult> AcceptAsync(
        MarketingLeadWebhookRequest request,
        string correlationId,
        CancellationToken cancellationToken);
}

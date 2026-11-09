using LeadIntegration.Api.Contracts.Marketing;

namespace LeadIntegration.Api.Features.RequestCatcher;

public interface IInboundWebhookObserver
{
    Task ObserveAsync(
        MarketingLeadWebhookRequest request,
        string contentType,
        string correlationId,
        CancellationToken cancellationToken);
}

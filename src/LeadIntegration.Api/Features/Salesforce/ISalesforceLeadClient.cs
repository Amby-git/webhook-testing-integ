using LeadIntegration.Contracts.Salesforce;

namespace LeadIntegration.Api.Features.Salesforce;

public interface ISalesforceLeadClient
{
    Task<SalesforceCallResult<SalesforceLeadResponse>> CreateLeadAsync(
        SalesforceLeadDto lead,
        CancellationToken cancellationToken,
        string? idempotencyKey = null);

    Task<SalesforceCallResult<SalesforceSimulatorStatusResponse>> GetStatusAsync(
        CancellationToken cancellationToken);
}

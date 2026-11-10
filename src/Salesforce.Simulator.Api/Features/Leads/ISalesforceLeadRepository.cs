using LeadIntegration.Contracts.Salesforce;

namespace Salesforce.Simulator.Api.Features.Leads;

public interface ISalesforceLeadRepository
{
    SalesforceLeadRecord Create(SalesforceLeadDto lead);
    (SalesforceLeadRecord Record, bool Created) CreateOrGet(SalesforceLeadDto lead, string idempotencyKey);
    IReadOnlyCollection<SalesforceLeadRecord> GetAll();
    SalesforceLeadRecord? GetById(string id);
    SalesforceLeadRecord? Update(string id, SalesforceLeadDto lead);
}

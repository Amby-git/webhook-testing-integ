using LeadIntegration.Contracts.Salesforce;

namespace Salesforce.Simulator.Api.Features.Leads;

public interface ISalesforceLeadRepository
{
    SalesforceLeadRecord Create(SalesforceLeadDto lead);
    IReadOnlyCollection<SalesforceLeadRecord> GetAll();
    SalesforceLeadRecord? GetById(string id);
    SalesforceLeadRecord? Update(string id, SalesforceLeadDto lead);
}

using System.ComponentModel.DataAnnotations;
using LeadIntegration.Contracts.Salesforce;

namespace Salesforce.Simulator.Api.Features.Leads;

public sealed class SalesforceLeadValidator
{
    private static readonly EmailAddressAttribute EmailValidator = new();

    public Dictionary<string, string[]> Validate(SalesforceLeadDto lead)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(lead.LastName))
        {
            errors[nameof(lead.LastName)] = ["LastName is required."];
        }

        if (string.IsNullOrWhiteSpace(lead.Company))
        {
            errors[nameof(lead.Company)] = ["Company is required for this simulated B2B Lead API."];
        }

        if (string.IsNullOrWhiteSpace(lead.Email))
        {
            errors[nameof(lead.Email)] = ["Email is required."];
        }
        else if (!EmailValidator.IsValid(lead.Email.Trim()))
        {
            errors[nameof(lead.Email)] = ["Email must be a valid email address."];
        }

        return errors;
    }
}

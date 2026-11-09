using System.ComponentModel.DataAnnotations;
using LeadIntegration.Api.Contracts.Marketing;

namespace LeadIntegration.Api.Features.Leads;

public sealed class MarketingLeadValidator
{
    private static readonly EmailAddressAttribute EmailAddressValidator = new();

    public Dictionary<string, string[]> Validate(MarketingLeadWebhookRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        AddRequiredError(errors, nameof(request.ExternalLeadId), request.ExternalLeadId, "ExternalLeadId is required.");
        AddRequiredError(errors, nameof(request.SourcePlatform), request.SourcePlatform, "SourcePlatform is required.");
        AddRequiredError(errors, nameof(request.FirstName), request.FirstName, "FirstName is required.");
        AddRequiredError(errors, nameof(request.LastName), request.LastName, "LastName is required.");
        AddRequiredError(errors, nameof(request.Company), request.Company, "Company is required for this B2B lead integration.");
        AddRequiredError(errors, nameof(request.EventId), request.EventId, "EventId is required for idempotent event processing.");

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            errors[nameof(request.Email)] = ["Email is required."];
        }
        else if (!EmailAddressValidator.IsValid(request.Email.Trim()))
        {
            errors[nameof(request.Email)] = ["Email must be a valid email address."];
        }

        if (request.CreatedAt is null)
        {
            errors[nameof(request.CreatedAt)] = ["CreatedAt is required."];
        }

        return errors;
    }

    private static void AddRequiredError(
        IDictionary<string, string[]> errors,
        string propertyName,
        string? value,
        string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors[propertyName] = [message];
        }
    }
}

using System.ComponentModel.DataAnnotations;

namespace LeadIntegration.Api.Features.Leads;

public sealed class LeadValidator
{
    private static readonly EmailAddressAttribute EmailAddressValidator = new();

    public IReadOnlyDictionary<string, string[]> Validate(LeadRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.FirstName))
        {
            errors[nameof(request.FirstName)] = ["First name is required."];
        }

        if (string.IsNullOrWhiteSpace(request.LastName))
        {
            errors[nameof(request.LastName)] = ["Last name is required."];
        }

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            errors[nameof(request.Email)] = ["Email is required."];
        }
        else if (!EmailAddressValidator.IsValid(request.Email.Trim()))
        {
            errors[nameof(request.Email)] = ["Email must be a valid email address."];
        }

        if (string.IsNullOrWhiteSpace(request.Source))
        {
            errors[nameof(request.Source)] = ["Source is required."];
        }

        return errors;
    }
}

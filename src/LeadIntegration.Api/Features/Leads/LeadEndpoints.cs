using LeadIntegration.Api.Contracts.Marketing;

namespace LeadIntegration.Api.Features.Leads;

public static class LeadEndpoints
{
    public static IEndpointRouteBuilder MapLeadEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var leads = endpoints.MapGroup("/leads").WithTags("Leads");

        leads.MapGet("", GetAllAsync)
            .WithName("GetLeads")
            .WithSummary("Returns all leads stored in memory.")
            .Produces<IReadOnlyCollection<LeadResponse>>()
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        leads.MapGet("/{id}", GetByIdAsync)
            .WithName("GetLeadById")
            .WithSummary("Returns one lead by its GUID.")
            .Produces<LeadResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        leads.MapPut("/{id}", UpdateAsync)
            .WithName("UpdateLead")
            .WithSummary("Replaces the details of an existing lead.")
            .Produces<LeadResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        leads.MapDelete("/{id}", DeleteAsync)
            .WithName("DeleteLead")
            .WithSummary("Deletes an existing lead.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        endpoints.MapPost("/webhooks/leads", ReceiveLeadAsync)
            .AddEndpointFilter<WebhookApiKeyFilter>()
            .RequireRateLimiting("lead-webhook")
            .WithName("ReceiveLead")
            .WithTags("Webhooks")
            .WithSummary("Accepts a lead webhook request for local processing.")
            .WithDescription("Requires X-Webhook-Api-Key. The validated marketing event is mapped to the Salesforce contract and sent synchronously to the separately hosted Salesforce simulator. No real Salesforce environment is contacted.")
            .Accepts<MarketingLeadWebhookRequest>("application/json")
            .Produces<LeadResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout);

        return endpoints;
    }

    private static IResult GetAllAsync(ILeadService service, LeadMapper mapper)
    {
        var leads = service.GetAll().Select(mapper.ToResponse).ToArray();
        return Results.Ok(leads);
    }

    private static IResult GetByIdAsync(string id, ILeadService service, LeadMapper mapper)
    {
        if (!Guid.TryParse(id, out var leadId))
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid lead ID", detail: "The lead ID must be a valid GUID.");
        }

        var lead = service.GetById(leadId);
        return lead is null
            ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Lead not found", detail: "No lead exists with the supplied ID.")
            : Results.Ok(mapper.ToResponse(lead));
    }

    private static IResult UpdateAsync(string id, LeadRequest request, LeadValidator validator, ILeadService service, LeadMapper mapper)
    {
        if (!Guid.TryParse(id, out var leadId))
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid lead ID", detail: "The lead ID must be a valid GUID.");
        }

        var errors = validator.Validate(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors, statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        var updateResult = service.Update(leadId, request);
        return updateResult.Status switch
        {
            LeadUpdateStatus.Updated => Results.Ok(mapper.ToResponse(updateResult.Lead!)),
            LeadUpdateStatus.NotFound => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Lead not found", detail: "No lead exists with the supplied ID."),
            LeadUpdateStatus.DuplicateEmail => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Email already exists", detail: "Another lead already uses this email address."),
            _ => throw new InvalidOperationException("Unexpected lead update result.")
        };
    }

    private static IResult DeleteAsync(string id, ILeadService service)
    {
        if (!Guid.TryParse(id, out var leadId) || !service.Delete(leadId))
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Lead not found", detail: "No lead exists with the supplied ID.");
        }

        return Results.NoContent();
    }

    private static async Task<IResult> ReceiveLeadAsync(
        MarketingLeadWebhookRequest request,
        HttpContext httpContext,
        ILeadService service,
        LeadMapper mapper,
        CancellationToken cancellationToken)
    {
        var result = await service.ProcessAsync(
            request,
            httpContext.Request.Headers["Idempotency-Key"].ToString(),
            cancellationToken);
        return result.Status switch
        {
            LeadProcessingStatus.Created => Results.Accepted($"/leads/{result.Lead!.Id}", mapper.ToResponse(result.Lead)),
            LeadProcessingStatus.ValidationFailed => Results.ValidationProblem(result.ValidationErrors!, statusCode: StatusCodes.Status400BadRequest),
            LeadProcessingStatus.DuplicateEmail => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Duplicate lead", detail: "A lead with this email address already exists."),
            LeadProcessingStatus.DuplicateRequest => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Duplicate event", detail: "This event or idempotency key has already been processed."),
            LeadProcessingStatus.SalesforceValidationFailed => Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Salesforce rejected the lead", detail: "The Salesforce simulator rejected the mapped lead data."),
            LeadProcessingStatus.SalesforceRateLimited => Results.Problem(statusCode: StatusCodes.Status429TooManyRequests, title: "Salesforce rate limit exceeded", detail: "The Salesforce simulator is rate limiting requests. Retry later."),
            LeadProcessingStatus.SalesforceInternalError => Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "Salesforce integration failed", detail: "The Salesforce simulator returned an internal error."),
            LeadProcessingStatus.SalesforceUnavailable or LeadProcessingStatus.SalesforceNetworkError => Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Salesforce integration unavailable", detail: "The Salesforce simulator could not be reached or is unavailable."),
            LeadProcessingStatus.SalesforceTimeout => Results.Problem(statusCode: StatusCodes.Status504GatewayTimeout, title: "Salesforce integration timed out", detail: "The Salesforce simulator did not respond before the configured timeout."),
            LeadProcessingStatus.SalesforceUnexpectedResponse => Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "Unexpected Salesforce response", detail: "The Salesforce simulator returned an unexpected response."),
            LeadProcessingStatus.LocalPersistenceFailed => Results.Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Lead processing failed", detail: "Salesforce created the record, but local integration tracking failed."),
            _ => throw new InvalidOperationException("Unexpected lead submission result.")
        };
    }
}

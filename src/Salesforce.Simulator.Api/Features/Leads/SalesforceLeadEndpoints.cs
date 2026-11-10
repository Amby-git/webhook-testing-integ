using LeadIntegration.Contracts.Salesforce;
using Salesforce.Simulator.Api.Features.FailureSimulation;

namespace Salesforce.Simulator.Api.Features.Leads;

public static class SalesforceLeadEndpoints
{
    public static IEndpointRouteBuilder MapSalesforceLeadEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var leads = endpoints.MapGroup("/api/salesforce/leads")
            .WithTags("Salesforce Lead API Simulation")
            .AddEndpointFilter<SimulatedFailureFilter>();

        leads.MapPost("", CreateAsync)
            .WithName("CreateSimulatedSalesforceLead")
            .WithSummary("Creates a Lead record in the in-memory Salesforce simulator.")
            .WithDescription("Requires LastName, Email, and Company for this simulated B2B Lead scenario. This is not a real Salesforce connection.")
            .Accepts<SalesforceLeadDto>("application/json")
            .Produces<SalesforceLeadResponse>(StatusCodes.Status201Created)
            .Produces<SalesforceLeadResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        leads.MapGet("", GetAllAsync)
            .WithName("GetSimulatedSalesforceLeads")
            .WithSummary("Returns all Lead records stored by the simulator.")
            .Produces<IReadOnlyCollection<SalesforceLeadRecord>>()
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        leads.MapGet("/{id}", GetByIdAsync)
            .WithName("GetSimulatedSalesforceLeadById")
            .WithSummary("Returns a simulated Lead record by Salesforce-style ID.")
            .Produces<SalesforceLeadResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        leads.MapPut("/{id}", UpdateAsync)
            .WithName("UpdateSimulatedSalesforceLead")
            .WithSummary("Replaces a Lead record in the simulator.")
            .WithDescription("The simulator keeps the existing Salesforce-style ID and replaces the supplied lead fields.")
            .Accepts<SalesforceLeadDto>("application/json")
            .Produces<SalesforceLeadResponse>()
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return endpoints;
    }

    private static IResult CreateAsync(
        SalesforceLeadDto request,
        HttpContext httpContext,
        SalesforceLeadValidator validator,
        ISalesforceLeadRepository repository)
    {
        var errors = validator.Validate(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors, statusCode: StatusCodes.Status400BadRequest);
        }

        var idempotencyKey = httpContext.Request.Headers["Idempotency-Key"].ToString();
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var (record, created) = repository.CreateOrGet(request, idempotencyKey.Trim());
            return created
                ? Results.Created($"/api/salesforce/leads/{record.Id}", record.ToResponse())
                : Results.Ok(record.ToResponse());
        }

        var lead = repository.Create(request);
        return Results.Created($"/api/salesforce/leads/{lead.Id}", lead.ToResponse());
    }

    private static IResult GetAllAsync(ISalesforceLeadRepository repository) =>
        Results.Ok(repository.GetAll());

    private static IResult GetByIdAsync(string id, ISalesforceLeadRepository repository)
    {
        var lead = repository.GetById(id);
        return lead is null
            ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Lead not found", detail: "No simulated Salesforce Lead has this ID.")
            : Results.Ok(lead.ToResponse());
    }

    private static IResult UpdateAsync(
        string id,
        SalesforceLeadDto request,
        SalesforceLeadValidator validator,
        ISalesforceLeadRepository repository)
    {
        var errors = validator.Validate(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors, statusCode: StatusCodes.Status400BadRequest);
        }

        var lead = repository.Update(id, request);
        return lead is null
            ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Lead not found", detail: "No simulated Salesforce Lead has this ID.")
            : Results.Ok(lead.ToResponse());
    }
}

using Salesforce.Simulator.Api.ApiDocumentation;
using Salesforce.Simulator.Api.Features.FailureSimulation;
using Salesforce.Simulator.Api.Features.Leads;
using LeadIntegration.Contracts.Salesforce;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi(options => options.AddOperationTransformer<SalesforceSimulatorOpenApiTransformer>());
builder.Services.AddProblemDetails();
builder.Services.Configure<SalesforceSimulatorOptions>(builder.Configuration.GetSection("SalesforceSimulator"));
builder.Services.AddSingleton<ISalesforceLeadRepository, InMemorySalesforceLeadRepository>();
builder.Services.AddSingleton<SalesforceLeadValidator>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapGet("/", () => Results.Redirect("/swagger"))
        .WithName("RedirectRootToSwagger");
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
        options.SwaggerEndpoint("/openapi/v1.json", "Salesforce CRM Simulator v1"));
}

app.MapSalesforceLeadEndpoints();

app.MapGet("/api/salesforce/status", (Microsoft.Extensions.Options.IOptions<SalesforceSimulatorOptions> options) =>
    options.Value.FailureMode == SalesforceFailureMode.ServiceUnavailable
        ? Results.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Salesforce simulator unavailable",
            detail: "The simulator is configured to return service-unavailable failures.",
            extensions: new Dictionary<string, object?> { ["simulated"] = true })
        : Results.Ok(new SalesforceSimulatorStatusResponse(
            Service: "Salesforce CRM Simulator",
            Status: "available",
            Simulated: true,
            ConfiguredFailureMode: options.Value.FailureMode.ToString())))
    .WithName("GetSalesforceSimulatorStatus")
    .WithTags("Simulator")
    .WithSummary("Reports status of the simulated Salesforce API.")
    .WithDescription("This is an independent in-memory simulation and does not connect to Salesforce.")
    .Produces<SalesforceSimulatorStatusResponse>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.Run();

public partial class Program { }

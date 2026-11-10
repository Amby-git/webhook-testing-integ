using System.Threading.RateLimiting;
using LeadIntegration.Api.ApiDocumentation;
using LeadIntegration.Api.Features.Leads;
using LeadIntegration.Api.Features.Messaging;
using LeadIntegration.Api.Features.RequestCatcher;
using LeadIntegration.Api.Features.Salesforce;
using LeadIntegration.Api.Infrastructure;
using LeadIntegration.Contracts.Salesforce;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi(options => options.AddOperationTransformer<ApiExamplesTransformer>());
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddSingleton<LeadValidator>();
builder.Services.AddSingleton<MarketingLeadValidator>();
builder.Services.AddSingleton<LeadMapper>();
builder.Services.AddSingleton<SalesforceLeadMapper>();
builder.Services.AddSingleton<ILeadRepository, InMemoryLeadRepository>();
builder.Services.AddOptions<RabbitMqOptions>()
    .Bind(builder.Configuration.GetSection(RabbitMqOptions.SectionName))
    .Validate(options => options.Port is > 0 and <= 65535 && options.ConnectionTimeoutSeconds > 0,
        "RabbitMQ port and connection timeout must be valid positive values.")
    .Validate(options => options.ConsumerPrefetchCount > 0 && options.MaximumRetryCount >= 0,
        "RabbitMQ prefetch count must be positive and maximum retries cannot be negative.")
    .Validate(options => options.InitialRetryDelayMilliseconds > 0 &&
                         options.MaximumRetryDelayMilliseconds >= options.InitialRetryDelayMilliseconds &&
                         options.ReconnectDelaySeconds > 0,
        "RabbitMQ retry and reconnect delays must be positive and the maximum retry delay must not be smaller than the initial delay.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.ExchangeName) &&
                         !string.IsNullOrWhiteSpace(options.QueueName) &&
                         !string.IsNullOrWhiteSpace(options.RoutingKey) &&
                         !string.IsNullOrWhiteSpace(options.RetryExchangeName) &&
                         !string.IsNullOrWhiteSpace(options.RetryQueueName) &&
                         !string.IsNullOrWhiteSpace(options.DeadLetterExchangeName) &&
                         !string.IsNullOrWhiteSpace(options.DeadLetterQueueName),
        "RabbitMQ exchange and queue names must be configured.")
    .ValidateOnStart();
builder.Services.AddSingleton<ILeadEventStore, InMemoryLeadEventStore>();
builder.Services.AddSingleton<IRabbitMqConnectionProvider, RabbitMqConnectionProvider>();
builder.Services.AddSingleton<ILeadMessagePublisher, RabbitMqLeadMessagePublisher>();
builder.Services.AddSingleton<LeadProcessingMessageValidator>();
builder.Services.AddScoped<ILeadMessageProcessor, LeadMessageProcessor>();
if (builder.Configuration.GetValue<bool>($"{RabbitMqOptions.SectionName}:ConsumerEnabled"))
{
    builder.Services.AddHostedService<LeadProcessingBackgroundService>();
}
builder.Services.AddScoped<ILeadEventAcceptanceService, LeadEventAcceptanceService>();
builder.Services.AddOptions<RequestCatcherOptions>()
    .Bind(builder.Configuration.GetSection(RequestCatcherOptions.SectionName))
    .Validate(options => Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseAddress) &&
                         baseAddress.Scheme == Uri.UriSchemeHttps,
        "RequestCatcher:BaseUrl must be an absolute HTTPS URI.")
    .Validate(options => options.TimeoutSeconds is >= 1 and <= 5,
        "RequestCatcher:TimeoutSeconds must be between 1 and 5 seconds.")
    .ValidateOnStart();
builder.Services.AddHttpClient<IInboundWebhookObserver, RequestCatcherInboundObserver>((services, client) =>
{
    var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<RequestCatcherOptions>>().Value;
    client.BaseAddress = new Uri($"{options.BaseUrl.TrimEnd('/')}/", UriKind.Absolute);
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
});
if (!Uri.TryCreate(builder.Configuration["SalesforceSimulator:BaseUrl"], UriKind.Absolute, out var simulatorBaseAddress))
{
    throw new InvalidOperationException("SalesforceSimulator:BaseUrl must be configured as an absolute URI.");
}

var salesforceTimeoutSeconds = builder.Configuration.GetValue("SalesforceSimulator:TimeoutSeconds", 10);
if (salesforceTimeoutSeconds <= 0)
{
    throw new InvalidOperationException("SalesforceSimulator:TimeoutSeconds must be greater than zero.");
}

builder.Services.AddHttpClient<ISalesforceLeadClient, SalesforceLeadClient>(client =>
{
    client.BaseAddress = simulatorBaseAddress;
    client.Timeout = TimeSpan.FromSeconds(salesforceTimeoutSeconds);
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("lead-webhook", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration.GetValue("WebhookRateLimit:PermitLimit", 30),
                Window = TimeSpan.FromSeconds(builder.Configuration.GetValue("WebhookRateLimit:WindowSeconds", 60)),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await Results.Problem(
            statusCode: StatusCodes.Status429TooManyRequests,
            title: "Too many requests",
            detail: "The webhook request limit has been exceeded.").ExecuteAsync(context.HttpContext);
    };
});
builder.Services.AddScoped<ILeadService, LeadService>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseRouting();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
        options.SwaggerEndpoint("/openapi/v1.json", "Lead Integration API v1"));
}

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
    .WithName("GetHealth")
    .WithTags("System")
    .WithSummary("Checks whether the API is running.")
    .Produces(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status500InternalServerError);

app.MapGet("/integration/salesforce/status", async (
    ISalesforceLeadClient salesforceClient,
    CancellationToken cancellationToken) =>
{
    var result = await salesforceClient.GetStatusAsync(cancellationToken);
    return result.Succeeded && string.Equals(result.Value!.Status, "available", StringComparison.OrdinalIgnoreCase)
        ? Results.Ok(result.Value)
        : Results.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Salesforce simulator unavailable",
            detail: "The Salesforce simulator did not report an available status.",
            extensions: new Dictionary<string, object?> { ["simulated"] = true });
})
    .WithName("GetSalesforceSimulationStatus")
    .WithTags("Integration")
    .WithSummary("Returns the configured availability of the Salesforce integration simulation.")
    .WithDescription("This endpoint queries the separate Salesforce Simulator API. No real Salesforce environment is contacted.")
    .Produces<SalesforceSimulatorStatusResponse>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
    .ProducesProblem(StatusCodes.Status500InternalServerError);

app.MapLeadEndpoints();
app.MapLeadEventEndpoints();

app.Run();

public partial class Program { }

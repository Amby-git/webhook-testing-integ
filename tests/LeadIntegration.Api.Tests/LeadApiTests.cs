using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using LeadIntegration.Api.Features.Leads;

namespace LeadIntegration.Api.Tests;

public sealed class LeadApiTests
{
    [Fact]
    public async Task SubmitLead_ValidRequest_CreatesSalesforceRecord()
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();

        using var response = await PostLeadAsync(client, "ada@example.com", "request-1");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.True(response.Headers.Location is not null);

        var acceptedLead = await response.Content.ReadFromJsonAsync<LeadResponse>();
        Assert.NotNull(acceptedLead);
        Assert.Equal("ada@example.com", acceptedLead.Email);
        Assert.Equal("00Q000000000001ABC", acceptedLead.SalesforceId);

        var leads = await client.GetFromJsonAsync<LeadResponse[]>("/leads");
        Assert.NotNull(leads);
        Assert.Single(leads);
        Assert.Equal(acceptedLead.Id, leads[0].Id);
    }

    [Fact]
    public async Task SubmitLead_MissingCompany_Returns400()
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();
        using var request = CreateLeadRequest("bad-email", "request-invalid");
        request.Content = JsonContent.Create(new
        {
            externalLeadId = "bad-email",
            sourcePlatform = "website",
            firstName = "Ada",
            lastName = "Lovelace",
            email = "bad-email",
            company = " ",
            eventId = "invalid-company",
            createdAt = DateTimeOffset.UtcNow
        });

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Company", body);
    }

    [Fact]
    public async Task MalformedJsonReturns400()
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/leads")
        {
            Content = new StringContent("{", Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Webhook-Api-Key", "test-api-key");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MissingWebhookKeyReturns401()
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/webhooks/leads", ValidLead("auth@example.com"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SubmitLead_DuplicateEvent_HandlesDuplicateAccordingToCurrentDesign()
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();

        using var first = await PostLeadAsync(client, "duplicate@example.com", "first-key");
        using var duplicateEmail = await PostLeadAsync(client, "DUPLICATE@example.com", "second-key");
        using var duplicateKey = await PostLeadAsync(client, "other@example.com", "first-key");

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicateEmail.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicateKey.StatusCode);
    }

    [Fact]
    public async Task CrudEndpointsReadUpdateAndDeleteStoredLeads()
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();
        using var accepted = await PostLeadAsync(client, "crud@example.com", "crud-key");
        var lead = await accepted.Content.ReadFromJsonAsync<LeadResponse>();
        Assert.NotNull(lead);

        using var found = await client.GetAsync($"/leads/{lead.Id}");
        Assert.Equal(HttpStatusCode.OK, found.StatusCode);

        using var update = await client.PutAsJsonAsync($"/leads/{lead.Id}", new
        {
            firstName = "Ada",
            lastName = "Lovelace",
            email = "updated@example.com",
            source = "crm"
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await update.Content.ReadFromJsonAsync<LeadResponse>();
        Assert.Equal("updated@example.com", updated?.Email);

        using var deleted = await client.DeleteAsync($"/leads/{lead.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        using var missing = await client.GetAsync($"/leads/{lead.Id}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var badId = await client.GetAsync("/leads/not-a-guid");
        Assert.Equal(HttpStatusCode.BadRequest, badId.StatusCode);
    }

    [Fact]
    public async Task WebhookRateLimitReturns429()
    {
        using var factory = new TestApiFactory(webhookPermitLimit: 1);
        using var client = factory.CreateClient();

        using var first = await PostLeadAsync(client, "first-limit@example.com", "limit-1");
        using var limited = await PostLeadAsync(client, "second-limit@example.com", "limit-2");

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Fact]
    public async Task SubmitLead_SalesforceUnavailable_DoesNotReturnSuccess()
    {
        using var factory = new TestApiFactory(failure: LeadIntegration.Api.Features.Salesforce.SalesforceClientFailure.Unavailable);
        using var client = factory.CreateClient();

        using var status = await client.GetAsync("/integration/salesforce/status");
        using var webhook = await PostLeadAsync(client, "offline@example.com", "offline-key");
        var leads = await client.GetFromJsonAsync<LeadResponse[]>("/leads");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, webhook.StatusCode);
        Assert.Empty(leads!);
    }

    [Fact]
    public async Task SubmitLead_SalesforceRateLimited_ReturnsAppropriateResponse()
    {
        using var factory = new TestApiFactory(failure: LeadIntegration.Api.Features.Salesforce.SalesforceClientFailure.RateLimited);
        using var client = factory.CreateClient();

        using var response = await PostLeadAsync(client, "limited@example.com", "limited-key");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Empty(await client.GetFromJsonAsync<LeadResponse[]>("/leads") ?? []);
    }

    [Fact]
    public async Task SubmitLead_InvalidEmail_Returns400Or422AccordingToValidationDesign()
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();

        using var response = await PostLeadAsync(client, "bad-email", "bad-email-key");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SubmitLead_ValidRequest_CreatesRecordInSalesforceSimulator()
    {
        using var simulator = new SalesforceSimulatorApiFactory();
        var simulatorHandler = simulator.Server.CreateHandler();
        using var simulatorHttp = new HttpClient(simulatorHandler) { BaseAddress = new Uri("http://simulator.test/") };
        var realClient = new LeadIntegration.Api.Features.Salesforce.SalesforceLeadClient(
            simulatorHttp,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<LeadIntegration.Api.Features.Salesforce.SalesforceLeadClient>.Instance);
        using var integration = new TestApiFactory(clientOverride: realClient);
        using var client = integration.CreateClient();

        using var response = await PostLeadAsync(client, "end-to-end@example.com", "e2e-key");
        var submitted = await response.Content.ReadFromJsonAsync<LeadResponse>();
        var records = await simulator.CreateClient().GetFromJsonAsync<JsonNode[]>("/api/salesforce/leads");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("00Q", submitted?.SalesforceId[..3]);
        Assert.Single(records!);
    }

    [Fact]
    public async Task UnexpectedReadExceptionReturns500ProblemDetails()
    {
        using var factory = new TestApiFactory(throwOnLeadRead: true);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/leads");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.StartsWith("application/problem+json", response.Content.Headers.ContentType?.ToString());
    }

    [Fact]
    public async Task OpenApiDocumentContainsExamplesAndWebhookResponses()
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();

        var document = JsonNode.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var webhook = document?["paths"]?["/webhooks/leads"]?["post"];

        Assert.NotNull(webhook?["requestBody"]?["content"]?["application/json"]?["examples"]);
        Assert.NotNull(webhook?["responses"]?["202"]?["content"]?["application/json"]?["example"]);
        Assert.NotNull(webhook?["responses"]?["401"]);
        Assert.NotNull(webhook?["responses"]?["409"]);
        Assert.NotNull(webhook?["responses"]?["422"]);
        Assert.NotNull(webhook?["responses"]?["429"]);
        Assert.NotNull(webhook?["responses"]?["503"]);
        Assert.NotNull(webhook?["responses"]?["502"]);
        Assert.NotNull(webhook?["responses"]?["504"]);
    }

    private static HttpRequestMessage CreateLeadRequest(string email, string? idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/leads")
        {
            Content = JsonContent.Create(ValidLead(email))
        };
        request.Headers.Add("X-Webhook-Api-Key", "test-api-key");
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return request;
    }

    private static Task<HttpResponseMessage> PostLeadAsync(HttpClient client, string email, string? idempotencyKey) =>
        client.SendAsync(CreateLeadRequest(email, idempotencyKey));

    private static object ValidLead(string email, string source = "website") => new
    {
        externalLeadId = "mkt-" + email,
        sourcePlatform = source,
        firstName = "Ada",
        lastName = "Lovelace",
        email,
        company = "Analytical Engines Ltd",
        phone = "+35312345678",
        campaignId = "spring-2026",
        createdAt = DateTimeOffset.Parse("2026-11-09T10:15:00Z"),
        eventId = "event-" + email
    };
}

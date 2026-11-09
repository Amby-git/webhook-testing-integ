using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using LeadIntegration.Contracts.Salesforce;

namespace LeadIntegration.Api.Tests;

public sealed class SalesforceSimulatorApiTests
{
    [Fact]
    public async Task CreateLeadReturns201WithLocationAndSalesforceStyleId()
    {
        using var factory = new SalesforceSimulatorApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/salesforce/leads", ValidSalesforceLead());
        var created = await response.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.NotNull(created?["id"]);
        Assert.StartsWith("00Q", created!["id"]!.GetValue<string>());
        Assert.Equal(18, created["id"]!.GetValue<string>().Length);
        Assert.Equal("Analytical Engines Ltd", created["company"]?.GetValue<string>());
    }

    [Fact]
    public async Task CreateWithoutCompanyReturns400ValidationProblem()
    {
        using var factory = new SalesforceSimulatorApiFactory();
        using var client = factory.CreateClient();
        var request = ValidSalesforceLead() with { Company = null };

        using var response = await client.PostAsJsonAsync("/api/salesforce/leads", request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Company", body);
    }

    [Fact]
    public async Task CreateWithInvalidEmailReturns400ValidationProblem()
    {
        using var factory = new SalesforceSimulatorApiFactory();
        using var client = factory.CreateClient();
        var request = ValidSalesforceLead() with { Email = "not-an-email" };

        using var response = await client.PostAsJsonAsync("/api/salesforce/leads", request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Email", body);
    }

    [Fact]
    public async Task ReadAndUpdateLeadUseTheGeneratedRecordId()
    {
        using var factory = new SalesforceSimulatorApiFactory();
        using var client = factory.CreateClient();
        using var createdResponse = await client.PostAsJsonAsync("/api/salesforce/leads", ValidSalesforceLead());
        var created = await createdResponse.Content.ReadFromJsonAsync<JsonObject>();
        Assert.NotNull(created);
        var id = created["id"]!.GetValue<string>();

        using var found = await client.GetAsync($"/api/salesforce/leads/{id}");
        using var updatedResponse = await client.PutAsJsonAsync(
            $"/api/salesforce/leads/{id}",
            ValidSalesforceLead() with { Company = "Analytical Engines International" });
        var updated = await updatedResponse.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        Assert.Equal(HttpStatusCode.OK, updatedResponse.StatusCode);
        Assert.Equal(id, updated?["id"]?.GetValue<string>());
        Assert.Equal("Analytical Engines International", updated?["company"]?.GetValue<string>());

        var leads = await client.GetFromJsonAsync<JsonArray>("/api/salesforce/leads");
        Assert.NotNull(leads);
        Assert.Single(leads);
    }

    [Fact]
    public async Task MissingSalesforceLeadReturns404()
    {
        using var factory = new SalesforceSimulatorApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/salesforce/leads/00Q000000000000001");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("TooManyRequests", HttpStatusCode.TooManyRequests)]
    [InlineData("InternalServerError", HttpStatusCode.InternalServerError)]
    [InlineData("ServiceUnavailable", HttpStatusCode.ServiceUnavailable)]
    public async Task ConfiguredFailureModeReturnsDeterministicStatus(string failureMode, HttpStatusCode expectedStatus)
    {
        using var factory = new SalesforceSimulatorApiFactory(failureMode);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/salesforce/leads", ValidSalesforceLead());
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Contains("simulated", body);
        if (expectedStatus == HttpStatusCode.TooManyRequests)
        {
            Assert.Equal("5", response.Headers.RetryAfter?.Delta?.TotalSeconds.ToString("0") ??
                response.Headers.RetryAfter?.ToString());
        }
    }

    [Fact]
    public async Task StatusAndOpenApiIdentifySimulatorAndDocumentExamples()
    {
        using var factory = new SalesforceSimulatorApiFactory();
        using var client = factory.CreateClient();

        var status = JsonNode.Parse(await client.GetStringAsync("/api/salesforce/status"));
        var openApi = JsonNode.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var createOperation = openApi?["paths"]?["/api/salesforce/leads"]?["post"];

        Assert.Equal(true, status?["simulated"]?.GetValue<bool>());
        Assert.NotNull(createOperation?["requestBody"]?["content"]?["application/json"]?["examples"]);
        Assert.NotNull(createOperation?["responses"]?["201"]?["content"]?["application/json"]?["example"]);
        Assert.NotNull(createOperation?["responses"]?["429"]);
        Assert.NotNull(createOperation?["responses"]?["500"]);
        Assert.NotNull(createOperation?["responses"]?["503"]);
    }

    [Fact]
    public async Task ServiceUnavailableFailureIsVisibleInStatus()
    {
        using var factory = new SalesforceSimulatorApiFactory("ServiceUnavailable");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/salesforce/status");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    private static SalesforceLeadDto ValidSalesforceLead() => new(
        FirstName: "Ada",
        LastName: "Lovelace",
        Email: "ada@example.com",
        Phone: "+35312345678",
        Company: "Analytical Engines Ltd",
        LeadSource: "HubSpot",
        ExternalLeadId: "mkt-001",
        CampaignId: "spring-2026");
}

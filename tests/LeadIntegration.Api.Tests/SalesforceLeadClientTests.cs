using System.Net;
using System.Text;
using LeadIntegration.Api.Features.Salesforce;
using LeadIntegration.Contracts.Salesforce;
using Microsoft.Extensions.Logging.Abstractions;

namespace LeadIntegration.Api.Tests;

public sealed class SalesforceLeadClientTests
{
    [Fact]
    public async Task CreateLead_ValidRequest_ReturnsSalesforceRecord()
    {
        var client = CreateClient(_ => JsonResponse(HttpStatusCode.Created,
            """{"id":"00Q000000000001ABC","firstName":"Ada","company":"Analytical Engines Ltd"}"""));

        var result = await client.CreateLeadAsync(ValidLead(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("00Q000000000001ABC", result.Value?.Id);
    }

    [Fact]
    public async Task CreateLead_TooManyRequests_Handles429()
    {
        var client = CreateClient(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)));

        var result = await client.CreateLeadAsync(ValidLead(), CancellationToken.None);

        Assert.Equal(SalesforceClientFailure.RateLimited, result.Failure);
    }

    [Fact]
    public async Task CreateLead_InternalServerError_Handles500()
    {
        var client = CreateClient(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        var result = await client.CreateLeadAsync(ValidLead(), CancellationToken.None);

        Assert.Equal(SalesforceClientFailure.InternalServerError, result.Failure);
    }

    [Fact]
    public async Task CreateLead_ServiceUnavailable_Handles503()
    {
        var client = CreateClient(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));

        var result = await client.CreateLeadAsync(ValidLead(), CancellationToken.None);

        Assert.Equal(SalesforceClientFailure.Unavailable, result.Failure);
    }

    [Fact]
    public async Task CreateLead_BadRequest_Handles400()
    {
        const string downstreamDetail = "sensitive downstream validation detail";
        var client = CreateClient(_ => JsonResponse(
            HttpStatusCode.BadRequest,
            $$"""{"title":"Validation failed","detail":"{{downstreamDetail}}"}"""));

        var result = await client.CreateLeadAsync(ValidLead(), CancellationToken.None);

        Assert.Equal(SalesforceClientFailure.Validation, result.Failure);
        Assert.Null(result.Value);
        Assert.DoesNotContain(downstreamDetail, result.ToString());
    }

    [Fact]
    public async Task CreateLead_NetworkException_HandlesConnectionFailure()
    {
        const string internalConnectionDetail = "sensitive internal host detail";
        var client = CreateClient(_ => Task.FromException<HttpResponseMessage>(
            new HttpRequestException(internalConnectionDetail)));

        var result = await client.CreateLeadAsync(ValidLead(), CancellationToken.None);

        Assert.Equal(SalesforceClientFailure.Network, result.Failure);
        Assert.Null(result.Value);
        Assert.DoesNotContain(internalConnectionDetail, result.ToString());
    }

    [Fact]
    public async Task CreateLead_Timeout_HandlesTimeout()
    {
        var client = CreateClient(async cancellationToken =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.Created);
        }, TimeSpan.FromMilliseconds(30));

        var result = await client.CreateLeadAsync(ValidLead(), CancellationToken.None);

        Assert.Equal(SalesforceClientFailure.Timeout, result.Failure);
    }

    private static SalesforceLeadClient CreateClient(Func<CancellationToken, Task<HttpResponseMessage>> handler, TimeSpan? timeout = null)
    {
        var httpClient = new HttpClient(new DelegateHandler(handler))
        {
            BaseAddress = new Uri("http://simulator.test/"),
            Timeout = timeout ?? TimeSpan.FromSeconds(2)
        };
        return new SalesforceLeadClient(httpClient, NullLogger<SalesforceLeadClient>.Instance);
    }

    private static Task<HttpResponseMessage> JsonResponse(HttpStatusCode status, string json) =>
        Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });

    private static SalesforceLeadDto ValidLead() => new(
        "Ada", "Lovelace", "ada@example.com", null, "Analytical Engines Ltd", "HubSpot", "mkt-1", null);

    private sealed class DelegateHandler(Func<CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(cancellationToken);
    }
}

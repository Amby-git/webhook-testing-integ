using LeadIntegration.Api.Features.Messaging;
using LeadIntegration.Api.Features.Salesforce;
using LeadIntegration.Contracts.Salesforce;
using Microsoft.Extensions.Logging.Abstractions;

namespace LeadIntegration.Api.Tests;

public sealed class LeadMessageProcessorTests
{
    [Fact]
    public async Task ProcessAsync_ValidMessage_CompletesAndStoresSalesforceId()
    {
        var store = new InMemoryLeadEventStore();
        var client = new FakeSalesforceClient(SalesforceCallResult<SalesforceLeadResponse>.Success(Response()));
        var processor = CreateProcessor(client, store);

        var result = await processor.ProcessAsync(ValidMessage(), 1, CancellationToken.None);
        var status = store.Get("HubSpot", "event-42");

        Assert.Equal(LeadMessageProcessingOutcome.Completed, result.Outcome);
        Assert.Equal(LeadEventProcessingStatus.Completed, status?.Status);
        Assert.Equal("00Q000000000001ABC", status?.SalesforceId);
        Assert.Equal("HUBSPOT:event-42", client.LastIdempotencyKey);
    }

    [Fact]
    public async Task ProcessAsync_TransientSalesforceFailure_ReturnsRetryableOutcome()
    {
        var store = new InMemoryLeadEventStore();
        var client = new FakeSalesforceClient(SalesforceCallResult<SalesforceLeadResponse>.Failed(
            SalesforceClientFailure.Unavailable, 503, TimeSpan.FromSeconds(4)));
        var processor = CreateProcessor(client, store);

        var result = await processor.ProcessAsync(ValidMessage(), 2, CancellationToken.None);

        Assert.Equal(LeadMessageProcessingOutcome.TransientFailure, result.Outcome);
        Assert.Equal(TimeSpan.FromSeconds(4), result.RetryAfter);
        Assert.Equal(LeadEventProcessingStatus.Processing, store.Get("HubSpot", "event-42")?.Status);
    }

    [Fact]
    public async Task ProcessAsync_PermanentSalesforceFailure_ReturnsPermanentFailure()
    {
        var store = new InMemoryLeadEventStore();
        var client = new FakeSalesforceClient(SalesforceCallResult<SalesforceLeadResponse>.Failed(
            SalesforceClientFailure.Validation, 400));
        var processor = CreateProcessor(client, store);

        var result = await processor.ProcessAsync(ValidMessage(), 1, CancellationToken.None);

        Assert.Equal(LeadMessageProcessingOutcome.PermanentFailure, result.Outcome);
        Assert.Equal(LeadEventProcessingStatus.Processing, store.Get("HubSpot", "event-42")?.Status);
    }

    [Fact]
    public async Task ProcessAsync_CompletedDuplicate_DoesNotCallSalesforceAgain()
    {
        var store = new InMemoryLeadEventStore();
        var message = ValidMessage();
        store.MarkAccepted(message);
        store.MarkCompleted(message, 1, "00Q000000000001ABC");
        var client = new FakeSalesforceClient(SalesforceCallResult<SalesforceLeadResponse>.Success(Response()));
        var processor = CreateProcessor(client, store);

        var result = await processor.ProcessAsync(message, 2, CancellationToken.None);

        Assert.Equal(LeadMessageProcessingOutcome.AlreadyCompleted, result.Outcome);
        Assert.Equal(0, client.CreateCount);
    }

    [Fact]
    public void RetryPolicy_TransientFailures_UsesCappedExponentialBackoffAndRetryAfter()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(100), LeadRetryPolicy.GetDelay(1, 100, 500));
        Assert.Equal(TimeSpan.FromMilliseconds(400), LeadRetryPolicy.GetDelay(3, 100, 500));
        Assert.Equal(TimeSpan.FromMilliseconds(500), LeadRetryPolicy.GetDelay(8, 100, 500));
        Assert.Equal(TimeSpan.FromSeconds(3), LeadRetryPolicy.GetDelay(1, 100, 500, TimeSpan.FromSeconds(3)));
        Assert.True(LeadRetryPolicy.ShouldDeadLetter(5, 5));
        Assert.False(LeadRetryPolicy.ShouldDeadLetter(4, 5));
    }

    [Fact]
    public void MessageValidator_MissingCompany_ReturnsSanitizedValidationError()
    {
        var validator = new LeadProcessingMessageValidator();
        var invalid = ValidMessage() with { Lead = ValidMessage().Lead with { Company = null } };

        Assert.Equal("The Salesforce lead payload is invalid or missing required fields.", validator.Validate(invalid));
    }

    private static LeadMessageProcessor CreateProcessor(FakeSalesforceClient client, ILeadEventStore store) =>
        new(client, store, new LeadProcessingMessageValidator(), NullLogger<LeadMessageProcessor>.Instance);

    private static LeadProcessingMessage ValidMessage() => new(
        "event-42", "HubSpot", "corr-42", DateTimeOffset.UtcNow,
        new SalesforceLeadDto("Ada", "Lovelace", "ada@example.com", null,
            "Analytical Engines Ltd", "HubSpot", "mkt-42", null));

    private static SalesforceLeadResponse Response() => new(
        "00Q000000000001ABC", "Ada", "Lovelace", "ada@example.com", null,
        "Analytical Engines Ltd", "HubSpot", "mkt-42", null);

    private sealed class FakeSalesforceClient(SalesforceCallResult<SalesforceLeadResponse> createResult)
        : ISalesforceLeadClient
    {
        public int CreateCount { get; private set; }
        public string? LastIdempotencyKey { get; private set; }

        public Task<SalesforceCallResult<SalesforceLeadResponse>> CreateLeadAsync(
            SalesforceLeadDto lead, CancellationToken cancellationToken, string? idempotencyKey = null)
        {
            CreateCount++;
            LastIdempotencyKey = idempotencyKey;
            return Task.FromResult(createResult);
        }

        public Task<SalesforceCallResult<SalesforceSimulatorStatusResponse>> GetStatusAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}

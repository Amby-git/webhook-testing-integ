using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LeadIntegration.Contracts.Salesforce;

namespace LeadIntegration.Api.Features.Salesforce;

public sealed class SalesforceLeadClient(HttpClient httpClient, ILogger<SalesforceLeadClient> logger)
    : ISalesforceLeadClient
{
    public async Task<SalesforceCallResult<SalesforceLeadResponse>> CreateLeadAsync(
        SalesforceLeadDto lead,
        CancellationToken cancellationToken,
        string? idempotencyKey = null)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/salesforce/leads")
            {
                Content = JsonContent.Create(lead)
            };
            if (!string.IsNullOrWhiteSpace(idempotencyKey))
            {
                request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
            }

            using var response = await httpClient.SendAsync(request, cancellationToken);

            if (response.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK)
            {
                var createdLead = await response.Content.ReadFromJsonAsync<SalesforceLeadResponse>(
                    cancellationToken: cancellationToken);

                return !string.IsNullOrWhiteSpace(createdLead?.Id)
                    ? SalesforceCallResult<SalesforceLeadResponse>.Success(createdLead)
                    : SalesforceCallResult<SalesforceLeadResponse>.Failed(SalesforceClientFailure.UnexpectedResponse);
            }

            return Failure<SalesforceLeadResponse>(response);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Salesforce simulator request failed at the network layer");
            return SalesforceCallResult<SalesforceLeadResponse>.Failed(SalesforceClientFailure.Network);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Salesforce simulator request timed out");
            return SalesforceCallResult<SalesforceLeadResponse>.Failed(SalesforceClientFailure.Timeout);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Salesforce simulator returned an unreadable lead response");
            return SalesforceCallResult<SalesforceLeadResponse>.Failed(SalesforceClientFailure.UnexpectedResponse);
        }
    }

    public async Task<SalesforceCallResult<SalesforceSimulatorStatusResponse>> GetStatusAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync("api/salesforce/status", cancellationToken);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var status = await response.Content.ReadFromJsonAsync<SalesforceSimulatorStatusResponse>(
                    cancellationToken: cancellationToken);
                return status is not null
                    ? SalesforceCallResult<SalesforceSimulatorStatusResponse>.Success(status)
                    : SalesforceCallResult<SalesforceSimulatorStatusResponse>.Failed(SalesforceClientFailure.UnexpectedResponse);
            }

            return Failure<SalesforceSimulatorStatusResponse>(response);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Salesforce simulator status request failed at the network layer");
            return SalesforceCallResult<SalesforceSimulatorStatusResponse>.Failed(SalesforceClientFailure.Network);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Salesforce simulator status request timed out");
            return SalesforceCallResult<SalesforceSimulatorStatusResponse>.Failed(SalesforceClientFailure.Timeout);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Salesforce simulator returned an unreadable status response");
            return SalesforceCallResult<SalesforceSimulatorStatusResponse>.Failed(SalesforceClientFailure.UnexpectedResponse);
        }
    }

    private static SalesforceCallResult<T> Failure<T>(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter?.Delta;
        if (retryAfter is null && response.Headers.RetryAfter?.Date is { } date)
        {
            retryAfter = date - DateTimeOffset.UtcNow;
        }

        return SalesforceCallResult<T>.Failed(
            MapFailure(response.StatusCode),
            (int)response.StatusCode,
            retryAfter is { } delay && delay > TimeSpan.Zero ? delay : null);
    }

    private static SalesforceClientFailure MapFailure(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.BadRequest => SalesforceClientFailure.Validation,
        HttpStatusCode.TooManyRequests => SalesforceClientFailure.RateLimited,
        HttpStatusCode.InternalServerError => SalesforceClientFailure.InternalServerError,
        HttpStatusCode.ServiceUnavailable => SalesforceClientFailure.Unavailable,
        HttpStatusCode.BadGateway => SalesforceClientFailure.BadGateway,
        HttpStatusCode.GatewayTimeout => SalesforceClientFailure.GatewayTimeout,
        _ => SalesforceClientFailure.UnexpectedResponse
    };
}

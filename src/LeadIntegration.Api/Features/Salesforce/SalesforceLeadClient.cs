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
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                "api/salesforce/leads",
                lead,
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.Created)
            {
                var createdLead = await response.Content.ReadFromJsonAsync<SalesforceLeadResponse>(
                    cancellationToken: cancellationToken);

                return !string.IsNullOrWhiteSpace(createdLead?.Id)
                    ? SalesforceCallResult<SalesforceLeadResponse>.Success(createdLead)
                    : SalesforceCallResult<SalesforceLeadResponse>.Failed(SalesforceClientFailure.UnexpectedResponse);
            }

            return SalesforceCallResult<SalesforceLeadResponse>.Failed(MapFailure(response.StatusCode));
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

            return SalesforceCallResult<SalesforceSimulatorStatusResponse>.Failed(MapFailure(response.StatusCode));
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

    private static SalesforceClientFailure MapFailure(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.BadRequest => SalesforceClientFailure.Validation,
        HttpStatusCode.TooManyRequests => SalesforceClientFailure.RateLimited,
        HttpStatusCode.InternalServerError => SalesforceClientFailure.InternalServerError,
        HttpStatusCode.ServiceUnavailable => SalesforceClientFailure.Unavailable,
        _ => SalesforceClientFailure.UnexpectedResponse
    };
}

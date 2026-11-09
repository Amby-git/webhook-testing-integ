using LeadIntegration.Api.Features.Salesforce;
using LeadIntegration.Api.Contracts.Marketing;

namespace LeadIntegration.Api.Features.Leads;

public sealed class LeadService(
    ILeadRepository repository,
    LeadMapper mapper,
    SalesforceLeadMapper salesforceLeadMapper,
    MarketingLeadValidator validator,
    ISalesforceLeadClient salesforceLeadClient,
    ILogger<LeadService> logger) : ILeadService
{
    public async Task<LeadProcessingResult> ProcessAsync(
        MarketingLeadWebhookRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var errors = validator.Validate(request);
        if (errors.Count > 0)
        {
            return new LeadProcessingResult(LeadProcessingStatus.ValidationFailed, ValidationErrors: errors);
        }

        var sourcePlatform = request.SourcePlatform!.Trim();
        var eventKey = string.IsNullOrWhiteSpace(idempotencyKey)
            ? $"{sourcePlatform}:{request.EventId!.Trim()}"
            : $"{sourcePlatform}:{idempotencyKey.Trim()}";
        var reservationResult = repository.TryReserve(eventKey, request.Email!.Trim());

        if (reservationResult.Status == LeadReservationStatus.DuplicateEmail)
        {
            return new LeadProcessingResult(LeadProcessingStatus.DuplicateEmail);
        }

        if (reservationResult.Status == LeadReservationStatus.DuplicateRequest || reservationResult.Reservation is null)
        {
            return new LeadProcessingResult(LeadProcessingStatus.DuplicateRequest);
        }

        var reservation = reservationResult.Reservation;
        try
        {
            var salesforcePayload = salesforceLeadMapper.Map(request);
            var salesforceResult = await salesforceLeadClient.CreateLeadAsync(salesforcePayload, cancellationToken);
            if (!salesforceResult.Succeeded)
            {
                logger.LogWarning(
                    "Salesforce simulator lead creation failed with outcome {SalesforceOutcome}",
                    salesforceResult.Failure);
                return new LeadProcessingResult(MapFailure(salesforceResult.Failure));
            }

            var lead = mapper.Map(request, salesforceResult.Value!.Id);
            if (!repository.Commit(lead, reservation))
            {
                logger.LogError(
                    "Salesforce record {SalesforceRecordId} was created but local lead tracking failed",
                    salesforceResult.Value.Id);
                return new LeadProcessingResult(LeadProcessingStatus.LocalPersistenceFailed);
            }

            logger.LogInformation(
                "Salesforce Lead {SalesforceRecordId} created for source platform {SourcePlatform}",
                lead.SalesforceId,
                lead.Source);
            return new LeadProcessingResult(LeadProcessingStatus.Created, lead);
        }
        finally
        {
            repository.Release(reservation);
        }
    }

    public IReadOnlyCollection<Lead> GetAll() => repository.GetAll();

    public Lead? GetById(Guid id) => repository.GetById(id);

    public LeadUpdateResult Update(Guid id, LeadRequest validatedRequest)
    {
        var currentLead = repository.GetById(id);
        return currentLead is null
            ? new LeadUpdateResult(LeadUpdateStatus.NotFound)
            : repository.TryUpdate(mapper.MapUpdate(validatedRequest, currentLead));
    }

    public bool Delete(Guid id) => repository.Delete(id);

    private static LeadProcessingStatus MapFailure(SalesforceClientFailure failure) => failure switch
    {
        SalesforceClientFailure.Validation => LeadProcessingStatus.SalesforceValidationFailed,
        SalesforceClientFailure.RateLimited => LeadProcessingStatus.SalesforceRateLimited,
        SalesforceClientFailure.InternalServerError => LeadProcessingStatus.SalesforceInternalError,
        SalesforceClientFailure.Unavailable => LeadProcessingStatus.SalesforceUnavailable,
        SalesforceClientFailure.Network => LeadProcessingStatus.SalesforceNetworkError,
        SalesforceClientFailure.Timeout => LeadProcessingStatus.SalesforceTimeout,
        _ => LeadProcessingStatus.SalesforceUnexpectedResponse
    };
}

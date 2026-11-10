namespace LeadIntegration.Api.Features.Messaging;

public sealed class LeadRetryPolicy
{
    public static bool IsTransient(LeadMessageProcessingResult result) =>
        result.Outcome == LeadMessageProcessingOutcome.TransientFailure;

    public static bool ShouldDeadLetter(int retriesAlreadyScheduled, int maximumRetryCount) =>
        retriesAlreadyScheduled >= maximumRetryCount;

    public static TimeSpan GetDelay(int retryCount, int initialDelayMilliseconds, int maximumDelayMilliseconds,
        TimeSpan? retryAfter = null)
    {
        var exponent = Math.Clamp(retryCount - 1, 0, 30);
        var milliseconds = Math.Min(maximumDelayMilliseconds, initialDelayMilliseconds * Math.Pow(2, exponent));
        var backoff = TimeSpan.FromMilliseconds(milliseconds);
        return retryAfter is { } requested && requested > backoff ? requested : backoff;
    }
}

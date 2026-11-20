namespace MultiChatOverlay.Core.Models;

/// <summary>
/// Represents the health state of a chat source connection.
/// Used for adaptive behavior (backoff, reduced polling, etc.)
/// </summary>
public sealed record ConnectionHealth
{
    public int ConsecutiveFailures { get; init; }
    public DateTimeOffset LastSuccess { get; init; }
    public TimeSpan AverageLatency { get; init; }
    public DateTimeOffset LastFailure { get; init; }
    public int TotalRequests { get; init; }
    public int FailedRequests { get; init; }

    /// <summary>
    /// Whether this source should reduce its polling frequency or activity
    /// </summary>
    public bool ShouldReducePolling => ConsecutiveFailures >= 3;

    /// <summary>
    /// Whether this source is considered healthy
    /// </summary>
    public bool IsHealthy => ConsecutiveFailures == 0 && TotalRequests > 0;

    /// <summary>
    /// Failure rate as a percentage (0-100)
    /// </summary>
    public double FailureRate => TotalRequests > 0 ? (double)FailedRequests / TotalRequests * 100 : 0;

    public static ConnectionHealth Healthy(DateTimeOffset lastSuccess, TimeSpan latency) =>
        new()
        {
            ConsecutiveFailures = 0,
            LastSuccess = lastSuccess,
            AverageLatency = latency,
            LastFailure = DateTimeOffset.MinValue,
            TotalRequests = 1,
            FailedRequests = 0
        };

    public static ConnectionHealth Unhealthy(DateTimeOffset lastFailure, int consecutiveFailures) =>
        new()
        {
            ConsecutiveFailures = consecutiveFailures,
            LastSuccess = DateTimeOffset.MinValue,
            AverageLatency = TimeSpan.Zero,
            LastFailure = lastFailure,
            TotalRequests = consecutiveFailures,
            FailedRequests = consecutiveFailures
        };

    public ConnectionHealth RecordSuccess(TimeSpan latency) =>
        this with
        {
            ConsecutiveFailures = 0,
            LastSuccess = DateTimeOffset.UtcNow,
            AverageLatency = AverageLatency == TimeSpan.Zero ? latency : TimeSpan.FromTicks((AverageLatency.Ticks * 3 + latency.Ticks) / 4), // EMA
            TotalRequests = TotalRequests + 1
        };

    public ConnectionHealth RecordFailure() =>
        this with
        {
            ConsecutiveFailures = ConsecutiveFailures + 1,
            LastFailure = DateTimeOffset.UtcNow,
            TotalRequests = TotalRequests + 1,
            FailedRequests = FailedRequests + 1
        };
}
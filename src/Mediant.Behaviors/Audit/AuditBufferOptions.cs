namespace Mediant.Behaviors.Audit;

/// <summary>
/// Options for buffered audit persistence (<c>AddMediantAuditBuffering</c>).
/// </summary>
public sealed class AuditBufferOptions
{
    /// <summary>Maximum entries written to the underlying store per batch. Default 100.</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>How often the background flusher drains the buffer. Default 5 seconds.</summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Maximum entries the buffer holds before writers are back-pressured (writes wait instead of
    /// dropping audit entries). Default 10000.
    /// </summary>
    public int Capacity { get; set; } = 10_000;
}

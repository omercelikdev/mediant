namespace Mediant.Abstractions;

/// <summary>
/// Provides an explicit execution order for pipeline behaviors.
/// Behaviors with lower Order values execute first (outermost in the pipeline).
/// Behaviors without this interface default to Order = 0 and maintain registration order.
/// </summary>
public interface IBehaviorOrder
{
    /// <summary>
    /// Gets the execution order. Lower values execute first (outermost).
    /// Built-in behavior defaults: Audit=100, Logging=200, UnhandledException=300,
    /// Authorization=400, Validation=500, Idempotency=600, Transaction=700,
    /// Performance=800, Retry=900, Caching=1000.
    /// </summary>
    int Order { get; }
}

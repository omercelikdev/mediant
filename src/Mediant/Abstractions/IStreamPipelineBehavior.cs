namespace Mediant.Abstractions;

/// <summary>
/// Delegate representing the next step in the stream pipeline.
/// Returns the async enumerable stream from the handler or next behavior.
/// </summary>
/// <typeparam name="TResponse">The type of each item in the stream.</typeparam>
public delegate IAsyncEnumerable<TResponse> StreamHandlerDelegate<out TResponse>();

/// <summary>
/// Pipeline behavior for streaming requests. Wraps the stream creation,
/// enabling pre-stream checks (authorization, validation) and post-stream
/// operations (audit logging).
/// </summary>
/// <typeparam name="TRequest">The stream request type.</typeparam>
/// <typeparam name="TResponse">The type of each item in the stream.</typeparam>
public interface IStreamPipelineBehavior<in TRequest, TResponse>
    where TRequest : IStreamRequest<TResponse>
{
    /// <summary>
    /// Handles the stream request within the pipeline. Call <paramref name="next"/>
    /// to invoke the next behavior or the stream handler.
    /// </summary>
    /// <param name="request">The stream request.</param>
    /// <param name="next">Delegate to the next behavior or handler.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The async enumerable stream.</returns>
    IAsyncEnumerable<TResponse> Handle(TRequest request, StreamHandlerDelegate<TResponse> next, CancellationToken cancellationToken);
}

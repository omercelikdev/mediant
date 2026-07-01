namespace Mediant.Abstractions;

/// <summary>
/// Defines a pre-processor that runs before the request handler.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
public interface IRequestPreProcessor<in TRequest>
    where TRequest : notnull
{
    /// <summary>
    /// Processes the request before the handler is invoked.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    ValueTask Process(TRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Defines a post-processor that runs after the request handler.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public interface IRequestPostProcessor<in TRequest, in TResponse>
    where TRequest : notnull
{
    /// <summary>
    /// Processes the request after the handler has been invoked.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="response">The response from the handler.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    ValueTask Process(TRequest request, TResponse response, CancellationToken cancellationToken);
}

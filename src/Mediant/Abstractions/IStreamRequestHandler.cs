namespace Mediant.Abstractions;

/// <summary>
/// Defines a handler for a streaming request.
/// </summary>
/// <typeparam name="TRequest">The streaming request type.</typeparam>
/// <typeparam name="TResponse">The type of each item in the stream.</typeparam>
public interface IStreamRequestHandler<in TRequest, out TResponse>
    where TRequest : IStreamRequest<TResponse>
{
    /// <summary>
    /// Handles the streaming request.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async enumerable of response items.</returns>
    IAsyncEnumerable<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
}

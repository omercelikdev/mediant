namespace Mediant.Abstractions;

/// <summary>
/// Defines a pipeline behavior that wraps request handling.
/// Behaviors are executed in order and can short-circuit the pipeline.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public interface IPipelineBehavior<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>
    /// Handles the pipeline behavior.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="next">The delegate to invoke the next behavior or the handler.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response.</returns>
    ValueTask<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken);
}

/// <summary>
/// Represents the next action in the pipeline.
/// </summary>
/// <typeparam name="TResponse">The response type.</typeparam>
/// <returns>The response.</returns>
public delegate ValueTask<TResponse> RequestHandlerDelegate<TResponse>();

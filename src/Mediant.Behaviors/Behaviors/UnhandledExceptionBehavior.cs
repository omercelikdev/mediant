using Microsoft.Extensions.Logging;
using Mediant.Abstractions;

namespace Mediant.Behaviors.Behaviors;

/// <summary>
/// Pipeline behavior that catches and logs unhandled exceptions.
/// Acts as a catch-all safety net. Always re-throws after logging.
/// </summary>
public sealed class UnhandledExceptionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>, IBehaviorOrder
    where TRequest : IRequest<TResponse>
{
    public int Order => 300;
    private readonly ILogger<UnhandledExceptionBehavior<TRequest, TResponse>> _logger;

    public UnhandledExceptionBehavior(ILogger<UnhandledExceptionBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async ValueTask<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        try
        {
            return await next().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var requestName = typeof(TRequest).Name;
            _logger.LogError(ex,
                "Unhandled exception for request {RequestName} ({RequestType}): {Message}",
                requestName, typeof(TRequest).FullName, ex.Message);
            throw;
        }
    }
}

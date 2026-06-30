using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qorpe.Mediator.Abstractions;
using Qorpe.Mediator.Behaviors.Attributes;
using Qorpe.Mediator.Behaviors.Configuration;
using Qorpe.Mediator.Results;

namespace Qorpe.Mediator.Behaviors.Behaviors;

/// <summary>
/// Pipeline behavior that enforces authorization requirements.
/// Checks [Authorize] attributes — all must pass. No context = Unauthorized.
/// <para>
/// When <typeparamref name="TResponse"/> is <see cref="Result"/> or <see cref="Result{T}"/> a
/// denial is returned as an Unauthorized/Forbidden <c>Result.Failure</c>. For any other response
/// type there is no failure value to return, so an <see cref="UnauthorizedAccessException"/> is
/// thrown — map it to 401/403 in your host if you use non-Result handlers.
/// </para>
/// </summary>
public sealed class AuthorizationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>, IBehaviorOrder
    where TRequest : IRequest<TResponse>
{
    public int Order => 400;

    // Cached attribute lookup — runs once per closed generic type (per TRequest), not per request
    private static readonly AuthorizeAttribute[] CachedAttributes =
        typeof(TRequest).GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>()
            .ToArray();

    private readonly IAuthorizationContext? _authContext;
    private readonly ILogger<AuthorizationBehavior<TRequest, TResponse>> _logger;
    private readonly AuthorizationBehaviorOptions _options;

    public AuthorizationBehavior(
        ILogger<AuthorizationBehavior<TRequest, TResponse>> logger,
        IOptions<AuthorizationBehaviorOptions> options,
        IAuthorizationContext? authContext = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _authContext = authContext;
    }

    public async ValueTask<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return await next().ConfigureAwait(false);
        }

        if (CachedAttributes.Length == 0)
        {
            return await next().ConfigureAwait(false);
        }

        // No auth context — unauthorized
        if (_authContext is null || !_authContext.IsAuthenticated)
        {
            _logger.LogWarning("Unauthorized access attempt for {RequestName}", typeof(TRequest).Name);
            return CreateUnauthorizedResult();
        }

        // Check all [Authorize] attributes — ALL must pass
        for (int i = 0; i < CachedAttributes.Length; i++)
        {
            var attr = CachedAttributes[i];

            if (attr.Roles is not null)
            {
                var requiredRoles = attr.Roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                for (int j = 0; j < requiredRoles.Length; j++)
                {
                    var found = false;
                    for (int k = 0; k < _authContext.Roles.Count; k++)
                    {
                        if (string.Equals(_authContext.Roles[k], requiredRoles[j], StringComparison.OrdinalIgnoreCase))
                        {
                            found = true;
                            break;
                        }
                    }

                    if (!found)
                    {
                        _logger.LogWarning(
                            "Forbidden: user {UserId} lacks role '{Role}' for {RequestName}",
                            _authContext.UserId, requiredRoles[j], typeof(TRequest).Name);
                        return CreateForbiddenResult();
                    }
                }
            }

            if (attr.Policy is not null)
            {
                if (!_authContext.HasClaim("Policy", attr.Policy))
                {
                    _logger.LogWarning(
                        "Forbidden: user {UserId} does not satisfy policy '{Policy}' for {RequestName}",
                        _authContext.UserId, attr.Policy, typeof(TRequest).Name);
                    return CreateForbiddenResult();
                }
            }
        }

        return await next().ConfigureAwait(false);
    }

    // Cached delegate for Result<T>.Failure(Error) — avoids reflection on every auth failure
    private static readonly Func<Error, TResponse>? CachedFailureFactory = BuildFailureFactory();

    private static Func<Error, TResponse>? BuildFailureFactory()
    {
        if (typeof(TResponse) == typeof(Result))
        {
            return error => (TResponse)(object)Result.Failure(error);
        }

        if (typeof(TResponse).IsGenericType && typeof(TResponse).GetGenericTypeDefinition() == typeof(Result<>))
        {
            var failureMethod = typeof(TResponse).GetMethod("Failure", new[] { typeof(Error) });
            if (failureMethod is null) return null;
            return error => (TResponse)failureMethod.Invoke(null, new object[] { error })!;
        }

        return null;
    }

    private static TResponse CreateUnauthorizedResult()
    {
        if (CachedFailureFactory is not null)
        {
            return CachedFailureFactory(Error.Unauthorized("Auth.Unauthorized", "Authentication is required."));
        }

        throw new UnauthorizedAccessException("Authentication is required.");
    }

    private static TResponse CreateForbiddenResult()
    {
        if (CachedFailureFactory is not null)
        {
            return CachedFailureFactory(Error.Forbidden("Auth.Forbidden", "You do not have permission to perform this action."));
        }

        throw new UnauthorizedAccessException("You do not have permission to perform this action.");
    }
}

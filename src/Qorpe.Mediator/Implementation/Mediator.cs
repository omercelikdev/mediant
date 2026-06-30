using System.Collections.Concurrent;
using Qorpe.Mediator.Abstractions;
using Qorpe.Mediator.DependencyInjection;

namespace Qorpe.Mediator.Implementation;

/// <summary>
/// Default mediator implementation using typed handler wrappers.
/// Zero reflection on the hot path after first call per type.
/// Thread-safe and re-entrant — handler calling Send() inside will not deadlock.
/// </summary>
public sealed class Mediator : IMediator
{
    private readonly IServiceProvider _serviceProvider;
    private readonly INotificationPublisher _notificationPublisher;
    private readonly bool _polymorphicNotifications;

    // Cache: (requestType, responseType) -> Func that does typed Send without boxing.
    // The response type is part of the key because IRequest<out TResponse> is covariant:
    // the same concrete request type can legitimately be sent through more than one
    // TResponse, and each produces a delegate of a different closed type.
    private static readonly ConcurrentDictionary<(Type RequestType, Type ResponseType), object> SendDelegateCache = new();

    // Cache: notificationType -> base notification types (for polymorphic dispatch)
    private static readonly ConcurrentDictionary<Type, Type[]> NotificationTypeHierarchyCache = new();

    /// <summary>
    /// Initializes a new instance of <see cref="Mediator"/>.
    /// </summary>
    public Mediator(IServiceProvider serviceProvider, INotificationPublisher notificationPublisher, MediatorOptions options)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _notificationPublisher = notificationPublisher ?? throw new ArgumentNullException(nameof(notificationPublisher));
        _polymorphicNotifications = options?.EnablePolymorphicNotifications ?? false;
    }

    /// <inheritdoc />
    public ValueTask<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var requestType = request.GetType();

        // Get cached typed send delegate — one dictionary lookup, then direct typed call.
        // Keyed by (requestType, TResponse) so covariant sends of the same request type
        // through different response types never collide on a single cache slot.
        var sendDelegate = (Func<object, IServiceProvider, CancellationToken, ValueTask<TResponse>>)
            SendDelegateCache.GetOrAdd((requestType, typeof(TResponse)), static key =>
            {
                // The wrapper is built for the requested TResponse (guaranteed valid by the
                // IRequest<TResponse> constraint on the Send signature), not a reflected type.
                var wrapperType = typeof(RequestHandlerWrapper<,>).MakeGenericType(key.RequestType, key.ResponseType);
                var wrapper = Activator.CreateInstance(wrapperType)!;

                // Build a Func that directly calls the typed HandleTyped method
                // This uses a lambda that captures the typed wrapper — no reflection on invocation
                var method = wrapperType.GetMethod("HandleTyped")!;

                return CreateSendDelegate<TResponse>(wrapper, method);
            });

        return sendDelegate(request, _serviceProvider, cancellationToken);
    }

    private static object CreateSendDelegate<TResponse>(object wrapper, System.Reflection.MethodInfo handleMethod)
    {
        // Compile an Expression Tree delegate for zero-reflection invocation.
        // This creates: (object req, IServiceProvider sp, CancellationToken ct) =>
        //     ((RequestHandlerWrapper<TReq, TResp>)wrapper).HandleTyped((TReq)req, sp, ct)
        var reqParam = System.Linq.Expressions.Expression.Parameter(typeof(object), "req");
        var spParam = System.Linq.Expressions.Expression.Parameter(typeof(IServiceProvider), "sp");
        var ctParam = System.Linq.Expressions.Expression.Parameter(typeof(CancellationToken), "ct");

        var wrapperConst = System.Linq.Expressions.Expression.Constant(wrapper);
        var requestType = handleMethod.GetParameters()[0].ParameterType;
        var castReq = System.Linq.Expressions.Expression.Convert(reqParam, requestType);

        var call = System.Linq.Expressions.Expression.Call(wrapperConst, handleMethod, castReq, spParam, ctParam);

        var lambda = System.Linq.Expressions.Expression.Lambda<Func<object, IServiceProvider, CancellationToken, ValueTask<TResponse>>>(
            call, reqParam, spParam, ctParam);

        return lambda.Compile();
    }

    /// <inheritdoc />
    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var requestType = request.GetType();
        var wrapper = HandlerWrapperFactory.GetStreamWrapper(requestType, typeof(TResponse));

        return StreamResults<TResponse>(wrapper, request, cancellationToken);
    }

    private async IAsyncEnumerable<TResponse> StreamResults<TResponse>(
        StreamHandlerWrapperBase wrapper, object request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var item in wrapper.Handle(request, _serviceProvider, cancellationToken).ConfigureAwait(false))
        {
            yield return (TResponse)item!;
        }
    }

    /// <inheritdoc />
    public ValueTask Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        ArgumentNullException.ThrowIfNull(notification);
        cancellationToken.ThrowIfCancellationRequested();

        // Dispatch on the RUNTIME type so the generic and non-generic overloads behave
        // identically. Using typeof(TNotification) would resolve handlers for the static
        // type only, silently skipping handlers registered for the concrete type when a
        // notification is published through a base/interface reference.
        var notificationType = notification.GetType();

        if (_polymorphicNotifications)
        {
            return PublishPolymorphic(notification, notificationType, cancellationToken);
        }

        var wrapper = HandlerWrapperFactory.GetNotificationWrapper(notificationType);
        return wrapper.Handle(notification, _serviceProvider, cancellationToken, _notificationPublisher);
    }

    /// <inheritdoc />
    public ValueTask Publish(INotification notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        cancellationToken.ThrowIfCancellationRequested();

        var notificationType = notification.GetType();

        if (_polymorphicNotifications)
        {
            return PublishPolymorphic(notification, notificationType, cancellationToken);
        }

        var wrapper = HandlerWrapperFactory.GetNotificationWrapper(notificationType);
        return wrapper.Handle(notification, _serviceProvider, cancellationToken, _notificationPublisher);
    }

    private async ValueTask PublishPolymorphic(INotification notification, Type notificationType, CancellationToken cancellationToken)
    {
        var typeHierarchy = NotificationTypeHierarchyCache.GetOrAdd(notificationType, static type =>
        {
            var types = new List<Type> { type };
            var current = type.BaseType;

            while (current is not null && current != typeof(object))
            {
                if (typeof(INotification).IsAssignableFrom(current))
                {
                    types.Add(current);
                }
                current = current.BaseType;
            }

            // Also check interfaces that implement INotification (excluding INotification itself)
            foreach (var iface in type.GetInterfaces())
            {
                if (iface != typeof(INotification) && typeof(INotification).IsAssignableFrom(iface))
                {
                    types.Add(iface);
                }
            }

            return types.ToArray();
        });

        // Publish to each type in the hierarchy
        for (int i = 0; i < typeHierarchy.Length; i++)
        {
            var wrapper = HandlerWrapperFactory.GetNotificationWrapper(typeHierarchy[i]);
            await wrapper.Handle(notification, _serviceProvider, cancellationToken, _notificationPublisher).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Clears all caches. For testing purposes only.
    /// </summary>
    internal static void ClearAllCaches()
    {
        SendDelegateCache.Clear();
        HandlerWrapperFactory.ClearCache();
    }
}

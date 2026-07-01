using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Mediant.Abstractions;
using Mediant.DependencyInjection;
using Mediant.Diagnostics;

namespace Mediant.Implementation;

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

    internal const string DynamicCodeMessage =
        "The reflection-based dispatch fallback uses runtime code generation. Under trimming/Native AOT, " +
        "register handlers with the source generator via services.AddMediantGenerated() so dispatch is precomputed.";

    /// <summary>
    /// Precomputes the typed send delegate for a request type. Called by the source generator so
    /// dispatch works without runtime code generation (Native AOT / trimming safe).
    /// </summary>
    public static void RegisterSend<TRequest, TResponse>() where TRequest : IRequest<TResponse>
    {
        var wrapper = new RequestHandlerWrapper<TRequest, TResponse>();
        SendDelegateCache[(typeof(TRequest), typeof(TResponse))] =
            (Func<object, IServiceProvider, CancellationToken, ValueTask<TResponse>>)
            ((req, sp, ct) => wrapper.HandleTyped((TRequest)req, sp, ct));
    }

    /// <summary>
    /// Precomputes the notification wrapper for a notification type. Called by the source generator.
    /// </summary>
    public static void RegisterNotification<TNotification>() where TNotification : INotification
        => HandlerWrapperFactory.RegisterNotification<TNotification>();

    /// <summary>
    /// Precomputes the stream wrapper for a stream request type. Called by the source generator.
    /// </summary>
    public static void RegisterStream<TRequest, TResponse>() where TRequest : IStreamRequest<TResponse>
        => HandlerWrapperFactory.RegisterStream<TRequest, TResponse>();

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
        var key = (requestType, typeof(TResponse));

        // Fast path: a precomputed delegate (from the source generator or a prior dynamic build).
        // Keyed by (requestType, TResponse) so covariant sends of the same request type through
        // different response types never collide on a single cache slot.
        if (!SendDelegateCache.TryGetValue(key, out var boxed))
        {
            boxed = BuildSendDelegateOrThrow<TResponse>(requestType, key);
        }

        var sendDelegate = (Func<object, IServiceProvider, CancellationToken, ValueTask<TResponse>>)boxed;

        // Fast path: when no tracer/meter is listening, skip all instrumentation entirely.
        if (!MediatorDiagnostics.IsSendEnabled)
        {
            return sendDelegate(request, _serviceProvider, cancellationToken);
        }

        return SendInstrumented(sendDelegate, request, requestType, cancellationToken);
    }

    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Guarded by RuntimeFeature.IsDynamicCodeSupported; the dynamic-code path is unreachable under Native AOT, where dispatch is precomputed by the source generator.")]
    private static object BuildSendDelegateOrThrow<TResponse>(Type requestType, (Type RequestType, Type ResponseType) key)
    {
        if (RuntimeFeature.IsDynamicCodeSupported)
        {
            return BuildSendDelegateDynamic<TResponse>(key);
        }

        throw new InvalidOperationException(
            $"No dispatch is registered for request type '{requestType}'. {DynamicCodeMessage}");
    }

    [RequiresDynamicCode(DynamicCodeMessage)]
    private static object BuildSendDelegateDynamic<TResponse>((Type RequestType, Type ResponseType) key)
    {
        return SendDelegateCache.GetOrAdd(key, static k =>
        {
            // The wrapper is built for the requested TResponse (guaranteed valid by the
            // IRequest<TResponse> constraint on the Send signature), not a reflected type.
            var wrapperType = typeof(RequestHandlerWrapper<,>).MakeGenericType(k.RequestType, k.ResponseType);
            var wrapper = Activator.CreateInstance(wrapperType)!;
            var method = wrapperType.GetMethod("HandleTyped")!;
            return CreateSendDelegate<TResponse>(wrapper, method);
        });
    }

    private async ValueTask<TResponse> SendInstrumented<TResponse>(
        Func<object, IServiceProvider, CancellationToken, ValueTask<TResponse>> sendDelegate,
        object request, Type requestType, CancellationToken cancellationToken)
    {
        var requestName = requestType.Name;
        using var activity = MediatorDiagnostics.StartSend(requestName, requestType);
        var startTimestamp = Stopwatch.GetTimestamp();
        try
        {
            var response = await sendDelegate(request, _serviceProvider, cancellationToken).ConfigureAwait(false);
            activity?.SetStatus(ActivityStatusCode.Ok);
            MediatorDiagnostics.RecordSend(requestName, startTimestamp, success: true, exception: null);
            return response;
        }
        catch (Exception ex)
        {
            MediatorDiagnostics.MarkFailed(activity, ex);
            MediatorDiagnostics.RecordSend(requestName, startTimestamp, success: false, exception: ex);
            throw;
        }
    }

    [RequiresDynamicCode(DynamicCodeMessage)]
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
        return PublishCore(notification, notification.GetType(), cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask Publish(INotification notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        cancellationToken.ThrowIfCancellationRequested();

        return PublishCore(notification, notification.GetType(), cancellationToken);
    }

    private ValueTask PublishCore(INotification notification, Type notificationType, CancellationToken cancellationToken)
    {
        // Fast path: when no tracer/meter is listening, skip all instrumentation entirely.
        if (!MediatorDiagnostics.IsPublishEnabled)
        {
            return PublishDispatch(notification, notificationType, cancellationToken);
        }

        return PublishInstrumented(notification, notificationType, cancellationToken);
    }

    private ValueTask PublishDispatch(INotification notification, Type notificationType, CancellationToken cancellationToken)
    {
        if (_polymorphicNotifications)
        {
            return PublishPolymorphic(notification, notificationType, cancellationToken);
        }

        var wrapper = HandlerWrapperFactory.GetNotificationWrapper(notificationType);
        return wrapper.Handle(notification, _serviceProvider, cancellationToken, _notificationPublisher);
    }

    private async ValueTask PublishInstrumented(INotification notification, Type notificationType, CancellationToken cancellationToken)
    {
        var notificationName = notificationType.Name;
        using var activity = MediatorDiagnostics.StartPublish(notificationName, notificationType);
        var startTimestamp = Stopwatch.GetTimestamp();
        try
        {
            await PublishDispatch(notification, notificationType, cancellationToken).ConfigureAwait(false);
            activity?.SetStatus(ActivityStatusCode.Ok);
            MediatorDiagnostics.RecordPublish(notificationName, startTimestamp, success: true);
        }
        catch (Exception ex)
        {
            MediatorDiagnostics.MarkFailed(activity, ex);
            MediatorDiagnostics.RecordPublish(notificationName, startTimestamp, success: false);
            throw;
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Walks the published notification's own base types and interfaces; those types are roots because the notification instance exists.")]
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

using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Qorpe.Mediator.Abstractions;
using Qorpe.Mediator.Exceptions;

namespace Qorpe.Mediator.Implementation;

/// <summary>
/// Abstract base for type-erased request handler invocation.
/// One concrete wrapper is created per (TRequest, TResponse) pair and cached forever.
/// This eliminates ALL reflection from the hot path.
/// </summary>
internal abstract class RequestHandlerWrapperBase
{
    public abstract ValueTask<object?> Handle(object request, IServiceProvider serviceProvider, CancellationToken cancellationToken);
}

/// <summary>
/// Typed request handler wrapper. Resolves handler and behaviors from DI with full type safety.
/// Zero reflection, zero boxing on the typed path.
/// </summary>
internal sealed class RequestHandlerWrapper<TRequest, TResponse> : RequestHandlerWrapperBase
    where TRequest : IRequest<TResponse>
{
    public override async ValueTask<object?> Handle(object request, IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var result = await HandleTyped((TRequest)request, serviceProvider, cancellationToken).ConfigureAwait(false);
        return result;
    }

    // Volatile flag per <TRequest, TResponse>: set to 1 when any container is handler-only.
    // When 0, cache check is skipped entirely — zero overhead for behavior-only scenarios.
    private static volatile int _hasFastPathCandidate;


    public ValueTask<TResponse> HandleTyped(TRequest request, IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        // Gate: only check per-container cache if a handler-only path has ever been seen for this type
        if (_hasFastPathCandidate == 1)
        {
            var probeCache = serviceProvider.GetService<PipelineProbeCache>();
            if (probeCache != null && probeCache.TryGetHandlerOnly(typeof(TRequest), out var isHandlerOnly) && isHandlerOnly)
            {
                // FAST PATH: no processors, no behaviors — 1 DI call, no closure, no delegate
                var h = serviceProvider.GetService<IRequestHandler<TRequest, TResponse>>()
                    ?? throw new HandlerNotFoundException(typeof(TRequest));
                return h.Handle(request, cancellationToken);
            }
        }

        // FULL PIPELINE — original code, zero cache overhead when _hasFastPathCandidate == 0
        var handler = serviceProvider.GetService<IRequestHandler<TRequest, TResponse>>();
        if (handler is null)
        {
            throw new HandlerNotFoundException(typeof(TRequest));
        }

        // Resolve pre/post processors — DI returns cached empty enumerable when none registered (~5ns)
        // Use ICollection.Count for zero-allocation emptiness check (avoids .Any() enumerator allocation)
        var preProcessors = serviceProvider.GetService<IEnumerable<IRequestPreProcessor<TRequest>>>();
        var postProcessors = serviceProvider.GetService<IEnumerable<IRequestPostProcessor<TRequest, TResponse>>>();

        var hasPreProcessors = HasItems(preProcessors);
        var hasPostProcessors = HasItems(postProcessors);

        // Build innermost delegate
        RequestHandlerDelegate<TResponse> handlerDelegate;
        if (!hasPreProcessors && !hasPostProcessors)
        {
            handlerDelegate = () => handler.Handle(request, cancellationToken);
        }
        else
        {
            handlerDelegate = BuildProcessorDelegate(request, handler, preProcessors!, postProcessors!, hasPreProcessors, hasPostProcessors, cancellationToken);
        }

        // Resolve behaviors — fully typed DI call
        var behaviors = serviceProvider.GetService<IEnumerable<IPipelineBehavior<TRequest, TResponse>>>();

        // Fast path: no behaviors registered
        if (behaviors is null)
        {
            if (!hasPreProcessors && !hasPostProcessors)
            {
                // Handler-only: cache for fast path on subsequent calls
                var pc = serviceProvider.GetService<PipelineProbeCache>();
                pc?.SetHandlerOnly(typeof(TRequest), true);
                _hasFastPathCandidate = 1;
            }
            return handlerDelegate();
        }

        // Materialize to array — use ICollection fast path when available
        IPipelineBehavior<TRequest, TResponse>[]? behaviorArray;
        int behaviorCount;

        if (behaviors is IPipelineBehavior<TRequest, TResponse>[] arr)
        {
            behaviorArray = arr;
            behaviorCount = arr.Length;
        }
        else if (behaviors is ICollection<IPipelineBehavior<TRequest, TResponse>> col)
        {
            behaviorCount = col.Count;
            if (behaviorCount == 0)
            {
                if (!hasPreProcessors && !hasPostProcessors)
                {
                    var pc = serviceProvider.GetService<PipelineProbeCache>();
                    pc?.SetHandlerOnly(typeof(TRequest), true);
                    _hasFastPathCandidate = 1;
                }
                return handlerDelegate();
            }
            behaviorArray = new IPipelineBehavior<TRequest, TResponse>[behaviorCount];
            col.CopyTo(behaviorArray, 0);
        }
        else
        {
            var list = new List<IPipelineBehavior<TRequest, TResponse>>(behaviors);
            behaviorArray = list.ToArray();
            behaviorCount = behaviorArray.Length;
        }

        if (behaviorCount == 0)
        {
            if (!hasPreProcessors && !hasPostProcessors)
            {
                var pc = serviceProvider.GetService<PipelineProbeCache>();
                pc?.SetHandlerOnly(typeof(TRequest), true);
                _hasFastPathCandidate = 1;
            }
            return handlerDelegate();
        }

        // Has behaviors — mark this container so fast-path check works correctly
        if (_hasFastPathCandidate == 1)
        {
            var pc = serviceProvider.GetService<PipelineProbeCache>();
            pc?.SetHandlerOnly(typeof(TRequest), false);
        }

        // Sort by IBehaviorOrder.Order if any behaviors implement it
        SortBehaviorsByOrder(behaviorArray, behaviorCount);

        // Build pipeline chain — no logger resolve on hot path (diagnostics only on cancellation)
        RequestHandlerDelegate<TResponse> next = handlerDelegate;

        for (int i = behaviorCount - 1; i >= 0; i--)
        {
            var behavior = behaviorArray[i];
            var currentNext = next;
            next = () => behavior.Handle(request, currentNext, cancellationToken);
        }

        return next();
    }

    private static bool HasItems<T>(IEnumerable<T>? enumerable)
    {
        if (enumerable is null) return false;
        if (enumerable is ICollection<T> col) return col.Count > 0;
        if (enumerable is IReadOnlyCollection<T> roc) return roc.Count > 0;
        using var e = enumerable.GetEnumerator();
        return e.MoveNext();
    }

    private static RequestHandlerDelegate<TResponse> BuildProcessorDelegate(
        TRequest request,
        IRequestHandler<TRequest, TResponse> handler,
        IEnumerable<IRequestPreProcessor<TRequest>> preProcessors,
        IEnumerable<IRequestPostProcessor<TRequest, TResponse>> postProcessors,
        bool hasPreProcessors,
        bool hasPostProcessors,
        CancellationToken cancellationToken)
    {
        return async () =>
        {
            if (hasPreProcessors)
            {
                foreach (var preProcessor in preProcessors)
                {
                    await preProcessor.Process(request, cancellationToken).ConfigureAwait(false);
                }
            }

            var response = await handler.Handle(request, cancellationToken).ConfigureAwait(false);

            if (hasPostProcessors)
            {
                foreach (var postProcessor in postProcessors)
                {
                    await postProcessor.Process(request, response, cancellationToken).ConfigureAwait(false);
                }
            }

            return response;
        };
    }

    private static int GetBehaviorOrder(IPipelineBehavior<TRequest, TResponse> behavior)
        => behavior is IBehaviorOrder o ? o.Order : int.MaxValue / 2;

    private static void SortBehaviorsByOrder(IPipelineBehavior<TRequest, TResponse>[] behaviors, int count)
    {
        // Only sort if any behavior implements IBehaviorOrder
        var hasOrdering = false;
        for (int i = 0; i < count; i++)
        {
            if (behaviors[i] is IBehaviorOrder)
            {
                hasOrdering = true;
                break;
            }
        }

        if (!hasOrdering) return;

        // Stable insertion sort by Order. Array.Sort (introsort) is NOT stable, which would
        // give behaviors that share an Order value (or both lack IBehaviorOrder) a
        // nondeterministic relative order. Insertion sort preserves registration order on
        // ties; count is small (pipeline depth), so the cost is negligible.
        for (int i = 1; i < count; i++)
        {
            var key = behaviors[i];
            var keyOrder = GetBehaviorOrder(key);
            int j = i - 1;
            while (j >= 0 && GetBehaviorOrder(behaviors[j]) > keyOrder)
            {
                behaviors[j + 1] = behaviors[j];
                j--;
            }
            behaviors[j + 1] = key;
        }
    }
}

/// <summary>
/// Abstract base for type-erased notification handler invocation.
/// </summary>
internal abstract class NotificationHandlerWrapperBase
{
    public abstract ValueTask Handle(INotification notification, IServiceProvider serviceProvider, CancellationToken cancellationToken,
        INotificationPublisher publisher);
}

/// <summary>
/// Typed notification handler wrapper. Zero reflection on the hot path.
/// Uses DIRECT handler invocation — no NotificationHandlerExecutor allocation for sequential path.
/// Falls back to executor-based path only for custom publishers.
/// </summary>
internal sealed class NotificationHandlerWrapper<TNotification> : NotificationHandlerWrapperBase
    where TNotification : INotification
{
    public override ValueTask Handle(INotification notification, IServiceProvider serviceProvider, CancellationToken cancellationToken,
        INotificationPublisher publisher)
    {
        // Resolve handlers — single typed DI call
        var handlers = serviceProvider.GetService<IEnumerable<INotificationHandler<TNotification>>>();

        if (handlers is null)
        {
            return ValueTask.CompletedTask;
        }

        // OPTIMIZATION: For sequential publishers, invoke handlers directly without executor wrappers.
        // This eliminates NotificationHandlerExecutor + closure allocation entirely.
        if (publisher is ForeachNotificationPublisher foreachPublisher)
        {
            return InvokeDirectSequential((TNotification)notification, handlers, cancellationToken);
        }

        if (publisher is ParallelNotificationPublisher)
        {
            return InvokeDirectParallel((TNotification)notification, handlers, cancellationToken);
        }

        // Fallback: build executors for custom publisher implementations
        return InvokeViaExecutors(notification, handlers, publisher, cancellationToken);
    }

    private static async ValueTask InvokeDirectSequential(TNotification notification,
        IEnumerable<INotificationHandler<TNotification>> handlers, CancellationToken cancellationToken)
    {
        // Direct invocation — ZERO allocation per handler, no executors, no closures
        foreach (var handler in handlers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await handler.Handle(notification, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async ValueTask InvokeDirectParallel(TNotification notification,
        IEnumerable<INotificationHandler<TNotification>> handlers, CancellationToken cancellationToken)
    {
        // Collect tasks for parallel execution
        Task[]? tasks = null;
        int count = 0;

        foreach (var handler in handlers)
        {
            tasks ??= new Task[4];
            if (count >= tasks.Length)
            {
                var newArr = new Task[tasks.Length * 2];
                Array.Copy(tasks, newArr, count);
                tasks = newArr;
            }
            tasks[count++] = handler.Handle(notification, cancellationToken).AsTask();
        }

        if (count == 0) return;

        if (count == 1)
        {
            await tasks![0].ConfigureAwait(false);
            return;
        }

        if (count < tasks!.Length)
        {
            Array.Resize(ref tasks, count);
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static ValueTask InvokeViaExecutors(INotification notification,
        IEnumerable<INotificationHandler<TNotification>> handlers,
        INotificationPublisher publisher, CancellationToken cancellationToken)
    {
        // Build executors for custom publishers
        NotificationHandlerExecutor[]? executors = null;
        int count = 0;

        foreach (var handler in handlers)
        {
            executors ??= new NotificationHandlerExecutor[4];
            if (count >= executors.Length)
            {
                var newArr = new NotificationHandlerExecutor[executors.Length * 2];
                Array.Copy(executors, newArr, count);
                executors = newArr;
            }
            var h = handler;
            executors[count++] = new NotificationHandlerExecutor(h, (n, ct) => h.Handle((TNotification)n, ct));
        }

        if (count == 0) return ValueTask.CompletedTask;
        if (count < executors!.Length) Array.Resize(ref executors, count);

        return publisher.Publish(executors, notification, cancellationToken);
    }
}

/// <summary>
/// Abstract base for type-erased stream handler invocation.
/// </summary>
internal abstract class StreamHandlerWrapperBase
{
    public abstract IAsyncEnumerable<object?> Handle(object request, IServiceProvider serviceProvider, CancellationToken cancellationToken);
}

/// <summary>
/// Typed stream handler wrapper. Resolves handler and stream pipeline behaviors from DI.
/// Behaviors wrap the stream creation, enabling pre-stream checks and post-stream operations.
/// </summary>
internal sealed class StreamHandlerWrapper<TRequest, TResponse> : StreamHandlerWrapperBase
    where TRequest : IStreamRequest<TResponse>
{
    public override async IAsyncEnumerable<object?> Handle(object request, IServiceProvider serviceProvider,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var handler = serviceProvider.GetService<IStreamRequestHandler<TRequest, TResponse>>();
        if (handler is null)
        {
            throw new HandlerNotFoundException(typeof(TRequest));
        }

        var typedRequest = (TRequest)request;

        // Resolve stream pipeline behaviors
        var behaviors = serviceProvider.GetService<IEnumerable<IStreamPipelineBehavior<TRequest, TResponse>>>();

        IAsyncEnumerable<TResponse> stream;

        if (behaviors is null)
        {
            stream = handler.Handle(typedRequest, cancellationToken);
        }
        else
        {
            // Materialize behaviors
            var behaviorArray = behaviors is IStreamPipelineBehavior<TRequest, TResponse>[] arr
                ? arr
                : behaviors is ICollection<IStreamPipelineBehavior<TRequest, TResponse>> col
                    ? col.ToArray()
                    : behaviors.ToArray();

            if (behaviorArray.Length == 0)
            {
                stream = handler.Handle(typedRequest, cancellationToken);
            }
            else
            {
                // Sort by IBehaviorOrder.Order if any behaviors implement it
                SortStreamBehaviorsByOrder(behaviorArray);

                // Build pipeline chain — innermost is the handler
                StreamHandlerDelegate<TResponse> next = () => handler.Handle(typedRequest, cancellationToken);

                for (int i = behaviorArray.Length - 1; i >= 0; i--)
                {
                    var behavior = behaviorArray[i];
                    var currentNext = next;
                    next = () => behavior.Handle(typedRequest, currentNext, cancellationToken);
                }

                stream = next();
            }
        }

        await foreach (var item in stream.ConfigureAwait(false))
        {
            yield return item;
        }
    }

    private static int GetStreamBehaviorOrder(IStreamPipelineBehavior<TRequest, TResponse> behavior)
        => behavior is IBehaviorOrder o ? o.Order : int.MaxValue / 2;

    private static void SortStreamBehaviorsByOrder(IStreamPipelineBehavior<TRequest, TResponse>[] behaviors)
    {
        var hasOrdering = false;
        for (int i = 0; i < behaviors.Length; i++)
        {
            if (behaviors[i] is IBehaviorOrder)
            {
                hasOrdering = true;
                break;
            }
        }

        if (!hasOrdering) return;

        // Stable insertion sort — see SortBehaviorsByOrder for rationale.
        for (int i = 1; i < behaviors.Length; i++)
        {
            var key = behaviors[i];
            var keyOrder = GetStreamBehaviorOrder(key);
            int j = i - 1;
            while (j >= 0 && GetStreamBehaviorOrder(behaviors[j]) > keyOrder)
            {
                behaviors[j + 1] = behaviors[j];
                j--;
            }
            behaviors[j + 1] = key;
        }
    }
}

/// <summary>
/// Factory and cache for handler wrappers. Creates wrappers once per type, caches forever.
/// The only place MakeGenericType is called — and only on first access.
/// </summary>
internal static class HandlerWrapperFactory
{
    private static readonly ConcurrentDictionary<Type, NotificationHandlerWrapperBase> NotificationWrappers = new();
    private static readonly ConcurrentDictionary<Type, StreamHandlerWrapperBase> StreamWrappers = new();

    public static NotificationHandlerWrapperBase GetNotificationWrapper(Type notificationType)
    {
        return NotificationWrappers.GetOrAdd(notificationType, static type =>
        {
            var wrapperType = typeof(NotificationHandlerWrapper<>).MakeGenericType(type);
            return (NotificationHandlerWrapperBase)Activator.CreateInstance(wrapperType)!;
        });
    }

    public static StreamHandlerWrapperBase GetStreamWrapper(Type requestType, Type responseType)
    {
        return StreamWrappers.GetOrAdd(requestType, static (type, resp) =>
        {
            var wrapperType = typeof(StreamHandlerWrapper<,>).MakeGenericType(type, resp);
            return (StreamHandlerWrapperBase)Activator.CreateInstance(wrapperType)!;
        }, responseType);
    }

    internal static void ClearCache()
    {
        NotificationWrappers.Clear();
        StreamWrappers.Clear();
    }
}

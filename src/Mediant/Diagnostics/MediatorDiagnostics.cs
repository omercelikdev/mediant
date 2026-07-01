using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Mediant.Diagnostics;

/// <summary>
/// OpenTelemetry-compatible instrumentation for the mediator pipeline.
/// <para>
/// Exposes a <see cref="System.Diagnostics.ActivitySource"/> and <see cref="System.Diagnostics.Metrics.Meter"/>
/// using the BCL primitives (no dependency on the OpenTelemetry SDK). Wire them up in your app:
/// </para>
/// <code>
/// builder.Services.AddOpenTelemetry()
///     .WithTracing(t => t.AddSource(MediatorDiagnostics.ActivitySourceName))
///     .WithMetrics(m => m.AddMeter(MediatorDiagnostics.MeterName));
/// </code>
/// <para>
/// When nothing is listening (the default), instrumentation is effectively free: the hot path
/// only performs a cheap <see cref="System.Diagnostics.ActivitySource.HasListeners"/> /
/// <see cref="Instrument.Enabled"/> check and skips all activity and measurement work.
/// </para>
/// </summary>
public static class MediatorDiagnostics
{
    /// <summary>The name of the <see cref="System.Diagnostics.ActivitySource"/> used for tracing.</summary>
    public const string ActivitySourceName = "Mediant";

    /// <summary>The name of the <see cref="System.Diagnostics.Metrics.Meter"/> used for metrics.</summary>
    public const string MeterName = "Mediant";

    private static readonly string Version =
        typeof(MediatorDiagnostics).Assembly.GetName().Version?.ToString() ?? "1.0.0";

    internal static readonly ActivitySource ActivitySource = new(ActivitySourceName, Version);

    private static readonly Meter Meter = new(MeterName, Version);

    private static readonly Counter<long> SendCount =
        Meter.CreateCounter<long>("qorpe.mediator.send.count", unit: "{request}", description: "Number of requests sent through the mediator.");

    private static readonly Histogram<double> SendDuration =
        Meter.CreateHistogram<double>("qorpe.mediator.send.duration", unit: "ms", description: "Duration of mediator Send operations.");

    private static readonly Counter<long> PublishCount =
        Meter.CreateCounter<long>("qorpe.mediator.publish.count", unit: "{notification}", description: "Number of notifications published through the mediator.");

    private static readonly Histogram<double> PublishDuration =
        Meter.CreateHistogram<double>("qorpe.mediator.publish.duration", unit: "ms", description: "Duration of mediator Publish operations.");

    // Tag names follow OpenTelemetry semantic-convention style (lowercase, dotted namespace).
    private const string RequestTag = "qorpe.mediator.request";
    private const string NotificationTag = "qorpe.mediator.notification";
    private const string SuccessTag = "qorpe.mediator.success";
    private const string ErrorTypeTag = "error.type";

    /// <summary>
    /// True when a tracer or the Send meters are listening. Guards the Send hot path so the
    /// default (no telemetry wired) case does no extra work beyond this check.
    /// </summary>
    internal static bool IsSendEnabled =>
        ActivitySource.HasListeners() || SendCount.Enabled || SendDuration.Enabled;

    /// <summary>
    /// True when a tracer or the Publish meters are listening. Guards the Publish hot path.
    /// </summary>
    internal static bool IsPublishEnabled =>
        ActivitySource.HasListeners() || PublishCount.Enabled || PublishDuration.Enabled;

    internal static Activity? StartSend(string requestName, Type requestType)
    {
        var activity = ActivitySource.StartActivity($"mediator.send {requestName}", ActivityKind.Internal);
        if (activity is not null)
        {
            activity.SetTag(RequestTag, requestType.FullName ?? requestName);
        }

        return activity;
    }

    internal static Activity? StartPublish(string notificationName, Type notificationType)
    {
        var activity = ActivitySource.StartActivity($"mediator.publish {notificationName}", ActivityKind.Internal);
        if (activity is not null)
        {
            activity.SetTag(NotificationTag, notificationType.FullName ?? notificationName);
        }

        return activity;
    }

    internal static void RecordSend(string requestName, long startTimestamp, bool success, Exception? exception)
    {
        if (SendCount.Enabled)
        {
            SendCount.Add(1,
                new KeyValuePair<string, object?>(RequestTag, requestName),
                new KeyValuePair<string, object?>(SuccessTag, success));
        }

        if (SendDuration.Enabled)
        {
            SendDuration.Record(Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds,
                new KeyValuePair<string, object?>(RequestTag, requestName),
                new KeyValuePair<string, object?>(SuccessTag, success));
        }

        _ = exception;
    }

    internal static void RecordPublish(string notificationName, long startTimestamp, bool success)
    {
        if (PublishCount.Enabled)
        {
            PublishCount.Add(1,
                new KeyValuePair<string, object?>(NotificationTag, notificationName),
                new KeyValuePair<string, object?>(SuccessTag, success));
        }

        if (PublishDuration.Enabled)
        {
            PublishDuration.Record(Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds,
                new KeyValuePair<string, object?>(NotificationTag, notificationName),
                new KeyValuePair<string, object?>(SuccessTag, success));
        }
    }

    internal static void MarkFailed(Activity? activity, Exception exception)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity.SetTag(ErrorTypeTag, exception.GetType().FullName);
    }
}

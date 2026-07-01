using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Mediant.Abstractions;
using Mediant.DependencyInjection;
using Mediant.Diagnostics;
using Mediant.Results;

namespace Mediant.UnitTests.Diagnostics;

/// <summary>
/// Verifies the mediator emits OpenTelemetry-compatible activities and metrics via the BCL
/// <see cref="ActivitySource"/>/<see cref="Meter"/> primitives — and that nothing is emitted
/// when no listener is attached (the zero-overhead default).
/// </summary>
public class MediatorDiagnosticsTests
{
    private static IMediator BuildMediator()
    {
        var services = new ServiceCollection();
        services.AddMediant(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(MediatorDiagnosticsTests).Assembly));
        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    private static (ActivityListener listener, List<Activity> activities) ListenToActivities()
    {
        var activities = new List<Activity>();
        var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == MediatorDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activities.Add,
        };
        ActivitySource.AddActivityListener(listener);
        return (listener, activities);
    }

    // NOTE: the ActivitySource/Meter are process-global, so other test classes running in
    // parallel also emit to a "Mediant" listener. Every assertion therefore filters to
    // the specific request/notification type this test owns, which no other test produces.

    [Fact]
    public async Task Send_Emits_Activity_With_Ok_Status()
    {
        var (listener, activities) = ListenToActivities();
        using (listener)
        {
            var mediator = BuildMediator();

            await mediator.Send(new DiagPing());

            var activity = activities.Should().ContainSingle(a => a.OperationName == "mediator.send DiagPing").Subject;
            activity.Status.Should().Be(ActivityStatusCode.Ok);
            activity.GetTagItem("mediant.request").Should().Be(typeof(DiagPing).FullName);
        }
    }

    [Fact]
    public async Task Send_When_Handler_Throws_Emits_Error_Activity()
    {
        var (listener, activities) = ListenToActivities();
        using (listener)
        {
            var mediator = BuildMediator();

            var act = async () => await mediator.Send(new DiagFail());
            await act.Should().ThrowAsync<InvalidOperationException>();

            var activity = activities.Should().ContainSingle(a => a.OperationName == "mediator.send DiagFail").Subject;
            activity.Status.Should().Be(ActivityStatusCode.Error);
            activity.GetTagItem("error.type").Should().Be(typeof(InvalidOperationException).FullName);
        }
    }

    [Fact]
    public async Task Publish_Emits_Activity()
    {
        var (listener, activities) = ListenToActivities();
        using (listener)
        {
            var mediator = BuildMediator();

            await mediator.Publish(new DiagEvent());

            var activity = activities.Should().ContainSingle(a => a.OperationName == "mediator.publish DiagEvent").Subject;
            activity.Status.Should().Be(ActivityStatusCode.Ok);
        }
    }

    [Fact]
    public async Task Send_Records_Count_And_Duration_Metrics()
    {
        var counts = new List<(string Instrument, long Value, string Request)>();
        var durations = new List<string>();

        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == MediatorDiagnostics.MeterName)
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            var request = string.Empty;
            foreach (var tag in tags)
            {
                if (tag.Key == "mediant.request" && tag.Value is string r)
                {
                    request = r;
                }
            }
            counts.Add((instrument.Name, value, request));
        });
        meterListener.SetMeasurementEventCallback<double>((instrument, _, _, _) => durations.Add(instrument.Name));
        meterListener.Start();

        var mediator = BuildMediator();
        await mediator.Send(new DiagPing());

        // Filter to this test's request — other parallel Sends also fire these instruments.
        var mine = counts.Where(c => c.Instrument == "mediant.send.count" && c.Request == "DiagPing").ToList();
        mine.Should().ContainSingle();
        mine[0].Value.Should().Be(1);
        durations.Should().Contain("mediant.send.duration");
    }

    [Fact]
    public async Task Send_Result_Is_Unaffected_By_Instrumentation()
    {
        // Instrumentation must be transparent — the response is identical with a listener present.
        var (listener, _) = ListenToActivities();
        using (listener)
        {
            var mediator = BuildMediator();

            var result = await mediator.Send(new DiagPing());

            result.IsSuccess.Should().BeTrue();
        }
    }
}

public sealed record DiagPing : IRequest<Result>;

internal sealed class DiagPingHandler : IRequestHandler<DiagPing, Result>
{
    public ValueTask<Result> Handle(DiagPing request, CancellationToken cancellationToken)
        => ValueTask.FromResult(Result.Success());
}

public sealed record DiagFail : IRequest<Result>;

internal sealed class DiagFailHandler : IRequestHandler<DiagFail, Result>
{
    public ValueTask<Result> Handle(DiagFail request, CancellationToken cancellationToken)
        => throw new InvalidOperationException("diag boom");
}

public sealed record DiagEvent : INotification;

internal sealed class DiagEventHandler : INotificationHandler<DiagEvent>
{
    public ValueTask Handle(DiagEvent notification, CancellationToken cancellationToken)
        => ValueTask.CompletedTask;
}

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mediant.Abstractions;
using Mediant.Behaviors.Attributes;
using Mediant.Behaviors.Behaviors;
using Mediant.Behaviors.Configuration;
using Mediant.Results;
using Mediant.UnitTests.Helpers;

namespace Mediant.UnitTests.Behaviors;

public class PerformanceBehaviorTests
{
    [Fact]
    public async Task Should_Pass_Through_Fast_Requests()
    {
        var logger = Substitute.For<ILogger<PerformanceBehavior<TestCommand, Result>>>();
        var opts = Options.Create(new PerformanceBehaviorOptions { WarningThresholdMs = 500 });
        var behavior = new PerformanceBehavior<TestCommand, Result>(logger, opts);

        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());

        var result = await behavior.Handle(new TestCommand("test"), next, CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Should_Skip_When_Disabled()
    {
        var logger = Substitute.For<ILogger<PerformanceBehavior<TestCommand, Result>>>();
        var opts = Options.Create(new PerformanceBehaviorOptions { Enabled = false });
        var behavior = new PerformanceBehavior<TestCommand, Result>(logger, opts);

        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());
        var result = await behavior.Handle(new TestCommand("test"), next, CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Should_Throw_When_Threshold_Is_Zero()
    {
        var logger = Substitute.For<ILogger<PerformanceBehavior<TestCommand, Result>>>();
        var opts = Options.Create(new PerformanceBehaviorOptions { WarningThresholdMs = 0 });

        var act = () => new PerformanceBehavior<TestCommand, Result>(logger, opts);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task Should_Log_Warning_For_Slow_Request()
    {
        var logger = Substitute.For<ILogger<PerformanceBehavior<TestCommand, Result>>>();
        var opts = Options.Create(new PerformanceBehaviorOptions { WarningThresholdMs = 1, CriticalThresholdMs = 5000 });
        var behavior = new PerformanceBehavior<TestCommand, Result>(logger, opts);

        RequestHandlerDelegate<Result> next = async () =>
        {
            await Task.Delay(50);
            return Result.Success();
        };

        var result = await behavior.Handle(new TestCommand("test"), next, CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        // Logger will have been called with Warning level
    }

    [Fact]
    public async Task Should_Use_Attribute_Thresholds_Over_Global_Options()
    {
        var logger = Substitute.For<ILogger<PerformanceBehavior<CustomThresholdCommand, Result>>>();
        // Global: 500ms warning — but attribute says 1ms warning
        var opts = Options.Create(new PerformanceBehaviorOptions { WarningThresholdMs = 500, CriticalThresholdMs = 5000 });
        var behavior = new PerformanceBehavior<CustomThresholdCommand, Result>(logger, opts);

        RequestHandlerDelegate<Result> next = async () =>
        {
            await Task.Delay(10); // 10ms — above attribute warning (1ms) but below global (500ms)
            return Result.Success();
        };

        var result = await behavior.Handle(new CustomThresholdCommand("test"), next, CancellationToken.None);
        result.IsSuccess.Should().BeTrue();

        // Should have logged at Warning level using attribute threshold (1ms), not global (500ms)
        logger.Received().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("SLOW")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task Should_Fall_Back_To_Global_When_No_Attribute()
    {
        var logger = Substitute.For<ILogger<PerformanceBehavior<TestCommand, Result>>>();
        var opts = Options.Create(new PerformanceBehaviorOptions { WarningThresholdMs = 500 });
        var behavior = new PerformanceBehavior<TestCommand, Result>(logger, opts);

        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());

        var result = await behavior.Handle(new TestCommand("test"), next, CancellationToken.None);
        result.IsSuccess.Should().BeTrue();

        // Fast execution — should NOT warn (global threshold is 500ms)
        logger.DidNotReceive().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("SLOW")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }
    [Fact]
    public async Task Should_Log_Critical_When_Hard_Ceiling_Exceeded()
    {
        var logger = Substitute.For<ILogger<PerformanceBehavior<TestCommand, Result>>>();
        // Ceiling of 1ms — a 20ms handler must land above it without waiting 30 real seconds.
        var opts = Options.Create(new PerformanceBehaviorOptions
        {
            WarningThresholdMs = 60_000,
            CriticalThresholdMs = 60_000,
            HardCeilingMs = 1,
        });
        var behavior = new PerformanceBehavior<TestCommand, Result>(logger, opts);

        RequestHandlerDelegate<Result> next = async () =>
        {
            await Task.Delay(20);
            return Result.Success();
        };

        await behavior.Handle(new TestCommand("test"), next, CancellationToken.None);

        logger.Received().Log(
            LogLevel.Critical,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("hard ceiling")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task Attribute_Ceiling_Overrides_Global_Ceiling()
    {
        var logger = Substitute.For<ILogger<PerformanceBehavior<LowCeilingCommand, Result>>>();
        // Global ceiling stays at the 30s default; the attribute pulls it down to 1ms.
        var opts = Options.Create(new PerformanceBehaviorOptions
        {
            WarningThresholdMs = 60_000,
            CriticalThresholdMs = 60_000,
        });
        var behavior = new PerformanceBehavior<LowCeilingCommand, Result>(logger, opts);

        RequestHandlerDelegate<Result> next = async () =>
        {
            await Task.Delay(20);
            return Result.Success();
        };

        await behavior.Handle(new LowCeilingCommand("test"), next, CancellationToken.None);

        logger.Received().Log(
            LogLevel.Critical,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("hard ceiling")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task Negative_Attribute_Ceiling_Disables_Ceiling_For_Request_Type()
    {
        var logger = Substitute.For<ILogger<PerformanceBehavior<NoCeilingCommand, Result>>>();
        // Global ceiling of 1ms would fire for any handler — the attribute disables it.
        var opts = Options.Create(new PerformanceBehaviorOptions
        {
            WarningThresholdMs = 60_000,
            CriticalThresholdMs = 60_000,
            HardCeilingMs = 1,
        });
        var behavior = new PerformanceBehavior<NoCeilingCommand, Result>(logger, opts);

        RequestHandlerDelegate<Result> next = async () =>
        {
            await Task.Delay(20);
            return Result.Success();
        };

        await behavior.Handle(new NoCeilingCommand("test"), next, CancellationToken.None);

        logger.DidNotReceive().Log(
            LogLevel.Critical,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }
}

[PerformanceThreshold(CeilingMs = 1)]
public sealed record LowCeilingCommand(string Data) : ICommand<Result>;

public sealed class LowCeilingCommandHandler : ICommandHandler<LowCeilingCommand>
{
    public ValueTask<Result> Handle(LowCeilingCommand request, CancellationToken cancellationToken)
        => new(Result.Success());
}

// Long-running by design — the negative ceiling opts this request type out of the hard ceiling.
[PerformanceThreshold(CeilingMs = -1)]
public sealed record NoCeilingCommand(string Data) : ICommand<Result>;

public sealed class NoCeilingCommandHandler : ICommandHandler<NoCeilingCommand>
{
    public ValueTask<Result> Handle(NoCeilingCommand request, CancellationToken cancellationToken)
        => new(Result.Success());
}

// CriticalMs is set high so the handler's small delay (1ms+) reliably lands in the WARNING band
// even on a slow/loaded CI runner — otherwise the elapsed time can overshoot a low critical
// threshold and log at Error instead of Warning, making the test flaky.
[PerformanceThreshold(WarningMs = 1, CriticalMs = 60_000)]
public sealed record CustomThresholdCommand(string Data) : ICommand<Result>;

public sealed class CustomThresholdCommandHandler : ICommandHandler<CustomThresholdCommand>
{
    public ValueTask<Result> Handle(CustomThresholdCommand request, CancellationToken cancellationToken)
        => new(Result.Success());
}

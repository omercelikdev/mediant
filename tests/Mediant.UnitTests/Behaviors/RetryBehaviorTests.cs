using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mediant.Abstractions;
using Mediant.Behaviors.Behaviors;
using Mediant.Behaviors.Configuration;
using Mediant.Results;
using Mediant.UnitTests.Helpers;

namespace Mediant.UnitTests.Behaviors;

public class RetryBehaviorTests
{
    private readonly ILogger<RetryBehavior<RetryableCommand, Result>> _logger =
        Substitute.For<ILogger<RetryBehavior<RetryableCommand, Result>>>();
    private readonly IOptions<RetryBehaviorOptions> _options =
        Options.Create(new RetryBehaviorOptions());

    [Fact]
    public async Task Should_Retry_On_TimeoutException()
    {
        var behavior = new RetryBehavior<RetryableCommand, Result>(_logger, _options);
        var callCount = 0;

        RequestHandlerDelegate<Result> next = () =>
        {
            callCount++;
            if (callCount < 3)
                throw new TimeoutException("timeout");
            return new ValueTask<Result>(Result.Success());
        };

        var result = await behavior.Handle(new RetryableCommand("data"), next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        callCount.Should().Be(3);
    }

    [Fact]
    public async Task Should_Retry_On_HttpRequestException()
    {
        var behavior = new RetryBehavior<RetryableCommand, Result>(_logger, _options);
        var callCount = 0;

        RequestHandlerDelegate<Result> next = () =>
        {
            callCount++;
            if (callCount < 2)
                throw new HttpRequestException("network error");
            return new ValueTask<Result>(Result.Success());
        };

        var result = await behavior.Handle(new RetryableCommand("data"), next, CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        callCount.Should().Be(2);
    }

    [Fact]
    public async Task Should_NOT_Retry_UnauthorizedAccessException()
    {
        var behavior = new RetryBehavior<RetryableCommand, Result>(_logger, _options);

        RequestHandlerDelegate<Result> next = () => throw new UnauthorizedAccessException("no access");

        var act = async () => await behavior.Handle(new RetryableCommand("data"), next, CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Should_NOT_Retry_ArgumentException()
    {
        var behavior = new RetryBehavior<RetryableCommand, Result>(_logger, _options);

        RequestHandlerDelegate<Result> next = () => throw new ArgumentException("bad arg");

        var act = async () => await behavior.Handle(new RetryableCommand("data"), next, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Should_NOT_Retry_InvalidOperationException()
    {
        var behavior = new RetryBehavior<RetryableCommand, Result>(_logger, _options);

        RequestHandlerDelegate<Result> next = () => throw new InvalidOperationException("invalid");

        var act = async () => await behavior.Handle(new RetryableCommand("data"), next, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Should_Skip_When_No_Retryable_Attribute()
    {
        var cmdLogger = Substitute.For<ILogger<RetryBehavior<TestCommand, Result>>>();
        var behavior = new RetryBehavior<TestCommand, Result>(cmdLogger, _options);
        var called = false;

        RequestHandlerDelegate<Result> next = () =>
        {
            called = true;
            return new ValueTask<Result>(Result.Success());
        };

        var result = await behavior.Handle(new TestCommand("test"), next, CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        called.Should().BeTrue();
    }

    [Fact]
    public async Task Should_Throw_After_Max_Retries_Exhausted()
    {
        var behavior = new RetryBehavior<RetryableCommand, Result>(_logger, _options);

        RequestHandlerDelegate<Result> next = () => throw new TimeoutException("always fails");

        var act = async () => await behavior.Handle(new RetryableCommand("data"), next, CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutException>();
    }

    [Fact]
    public async Task Should_Stop_Retry_On_Cancellation()
    {
        var behavior = new RetryBehavior<RetryableCommand, Result>(_logger, _options);
        var cts = new CancellationTokenSource();
        var callCount = 0;

        RequestHandlerDelegate<Result> next = () =>
        {
            callCount++;
            cts.Cancel(); // Cancel token before retry
            throw new TimeoutException("timeout");
        };

        var act = async () => await behavior.Handle(new RetryableCommand("data"), next, cts.Token);

        // Should throw — either TimeoutException (no retry since cancelled) or OperationCanceledException
        await act.Should().ThrowAsync<Exception>();
        callCount.Should().BeLessThanOrEqualTo(2); // At most 1-2 calls before cancellation detected
    }

    [Fact]
    public async Task Should_Skip_When_Disabled()
    {
        var opts = Options.Create(new RetryBehaviorOptions { Enabled = false });
        var behavior = new RetryBehavior<RetryableCommand, Result>(_logger, opts);

        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());
        var result = await behavior.Handle(new RetryableCommand("data"), next, CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Should_Log_Success_Attempt_After_Retry()
    {
        var behavior = new RetryBehavior<RetryableCommand, Result>(_logger, _options);
        var callCount = 0;

        RequestHandlerDelegate<Result> next = () =>
        {
            callCount++;
            if (callCount < 2)
                throw new TimeoutException("transient");
            return new ValueTask<Result>(Result.Success());
        };

        var result = await behavior.Handle(new RetryableCommand("data"), next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        callCount.Should().Be(2, "should succeed on second attempt");

        // Verify the success log was emitted (attempt 2/3)
        _logger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("succeeded on attempt")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task Should_Not_Log_Success_Attempt_On_First_Try()
    {
        var behavior = new RetryBehavior<RetryableCommand, Result>(_logger, _options);

        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());

        var result = await behavior.Handle(new RetryableCommand("data"), next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        // Should NOT log success attempt when first try succeeds
        _logger.DidNotReceive().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("succeeded on attempt")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }
}

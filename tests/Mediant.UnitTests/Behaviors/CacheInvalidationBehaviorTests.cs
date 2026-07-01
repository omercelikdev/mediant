using Microsoft.Extensions.Logging;
using Mediant.Abstractions;
using Mediant.Behaviors.Attributes;
using Mediant.Behaviors.Behaviors;
using Mediant.Results;

namespace Mediant.UnitTests.Behaviors;

public class CacheInvalidationBehaviorTests
{
    private readonly ILogger<CacheInvalidationBehavior<InvalidatingCommand, Result>> _logger =
        Substitute.For<ILogger<CacheInvalidationBehavior<InvalidatingCommand, Result>>>();

    [Fact]
    public async Task Should_Invalidate_Cache_After_Successful_Execution()
    {
        var invalidator = Substitute.For<ICacheInvalidator>();
        var behavior = new CacheInvalidationBehavior<InvalidatingCommand, Result>(_logger, invalidator);

        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());

        await behavior.Handle(new InvalidatingCommand("data"), next, CancellationToken.None);

        await invalidator.Received(1).InvalidateAsync("OrderCache", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Not_Invalidate_On_Handler_Failure()
    {
        var invalidator = Substitute.For<ICacheInvalidator>();
        var behavior = new CacheInvalidationBehavior<InvalidatingCommand, Result>(_logger, invalidator);

        RequestHandlerDelegate<Result> next = () => throw new InvalidOperationException("fail");

        var act = async () => await behavior.Handle(new InvalidatingCommand("data"), next, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await invalidator.DidNotReceive().InvalidateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Skip_When_No_Attribute()
    {
        var cmdLogger = Substitute.For<ILogger<CacheInvalidationBehavior<Helpers.TestCommand, Result>>>();
        var invalidator = Substitute.For<ICacheInvalidator>();
        var behavior = new CacheInvalidationBehavior<Helpers.TestCommand, Result>(cmdLogger, invalidator);

        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());

        await behavior.Handle(new Helpers.TestCommand("data"), next, CancellationToken.None);

        await invalidator.DidNotReceive().InvalidateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Skip_When_No_Invalidator_Registered()
    {
        var behavior = new CacheInvalidationBehavior<InvalidatingCommand, Result>(_logger, cacheInvalidator: null);

        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());

        var result = await behavior.Handle(new InvalidatingCommand("data"), next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Should_Handle_Multiple_InvalidatesCache_Attributes()
    {
        var invalidator = Substitute.For<ICacheInvalidator>();
        var multiLogger = Substitute.For<ILogger<CacheInvalidationBehavior<MultiInvalidatingCommand, Result>>>();
        var behavior = new CacheInvalidationBehavior<MultiInvalidatingCommand, Result>(multiLogger, invalidator);

        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());

        await behavior.Handle(new MultiInvalidatingCommand("data"), next, CancellationToken.None);

        await invalidator.Received(1).InvalidateAsync("OrderCache", Arg.Any<CancellationToken>());
        await invalidator.Received(1).InvalidateAsync("UserCache", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Continue_On_Invalidation_Failure()
    {
        var invalidator = Substitute.For<ICacheInvalidator>();
        invalidator.InvalidateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(x => throw new Exception("cache down"));

        var behavior = new CacheInvalidationBehavior<InvalidatingCommand, Result>(_logger, invalidator);

        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());

        // Should not throw — cache invalidation failure is non-fatal
        var result = await behavior.Handle(new InvalidatingCommand("data"), next, CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
    }
}

[InvalidatesCache("OrderCache")]
public sealed record InvalidatingCommand(string Data) : ICommand<Result>;

public sealed class InvalidatingCommandHandler : ICommandHandler<InvalidatingCommand>
{
    public ValueTask<Result> Handle(InvalidatingCommand request, CancellationToken cancellationToken)
        => new(Result.Success());
}

[InvalidatesCache("OrderCache")]
[InvalidatesCache("UserCache")]
public sealed record MultiInvalidatingCommand(string Data) : ICommand<Result>;

public sealed class MultiInvalidatingCommandHandler : ICommandHandler<MultiInvalidatingCommand>
{
    public ValueTask<Result> Handle(MultiInvalidatingCommand request, CancellationToken cancellationToken)
        => new(Result.Success());
}

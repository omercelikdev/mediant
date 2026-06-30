using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qorpe.Mediator.Abstractions;
using Qorpe.Mediator.Behaviors.Attributes;
using Qorpe.Mediator.Behaviors.Behaviors;
using Qorpe.Mediator.Behaviors.Configuration;
using Qorpe.Mediator.Results;

namespace Qorpe.Mediator.UnitTests.Behaviors;

/// <summary>
/// Regression tests for client-supplied idempotency keys (<see cref="IdempotentAttribute.KeyProperty"/>).
/// Without it, hashing the whole payload means a retry carrying a fresh timestamp/correlation id
/// produces a different key and silently re-executes — the exact failure idempotency must prevent.
/// </summary>
public class IdempotencyKeyTests
{
    private readonly ILogger<IdempotencyBehavior<KeyedCommand, Result>> _logger =
        Substitute.For<ILogger<IdempotencyBehavior<KeyedCommand, Result>>>();
    private readonly IOptions<IdempotencyBehaviorOptions> _options =
        Options.Create(new IdempotencyBehaviorOptions());

    [Fact]
    public async Task Same_KeyProperty_Different_Incidental_Fields_Produce_Same_Key()
    {
        var keys = new List<string>();
        var store = Substitute.For<IIdempotencyStore>();
        store.ExistsAsync(Arg.Do<string>(keys.Add), Arg.Any<CancellationToken>()).Returns(false);

        var behavior = new IdempotencyBehavior<KeyedCommand, Result>(_logger, _options, store);
        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());

        // Same client key, different timestamp (an incidental field).
        await behavior.Handle(new KeyedCommand("client-123", DateTimeOffset.UnixEpoch), next, CancellationToken.None);
        await behavior.Handle(new KeyedCommand("client-123", DateTimeOffset.UtcNow), next, CancellationToken.None);

        keys.Should().HaveCount(2);
        keys[0].Should().Be(keys[1], "only the KeyProperty should determine the idempotency key");
    }

    [Fact]
    public async Task Different_KeyProperty_Produces_Different_Key()
    {
        var keys = new List<string>();
        var store = Substitute.For<IIdempotencyStore>();
        store.ExistsAsync(Arg.Do<string>(keys.Add), Arg.Any<CancellationToken>()).Returns(false);

        var behavior = new IdempotencyBehavior<KeyedCommand, Result>(_logger, _options, store);
        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());

        await behavior.Handle(new KeyedCommand("client-A", DateTimeOffset.UnixEpoch), next, CancellationToken.None);
        await behavior.Handle(new KeyedCommand("client-B", DateTimeOffset.UnixEpoch), next, CancellationToken.None);

        keys[0].Should().NotBe(keys[1]);
    }
}

[Idempotent(300, KeyProperty = nameof(IdempotencyKey))]
public sealed record KeyedCommand(string IdempotencyKey, DateTimeOffset RequestedAt) : ICommand<Result>;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mediant.Abstractions;
using Mediant.Behaviors.Attributes;
using Mediant.Behaviors.Behaviors;
using Mediant.Behaviors.Configuration;
using Mediant.Behaviors.Idempotency;
using Mediant.Results;

namespace Mediant.UnitTests.Behaviors;

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
        store.GetAsync<IdempotencyEntry<Result>>(Arg.Do<string>(keys.Add), Arg.Any<CancellationToken>())
            .Returns((IdempotencyEntry<Result>?)null);

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
        store.GetAsync<IdempotencyEntry<Result>>(Arg.Do<string>(keys.Add), Arg.Any<CancellationToken>())
            .Returns((IdempotencyEntry<Result>?)null);

        var behavior = new IdempotencyBehavior<KeyedCommand, Result>(_logger, _options, store);
        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());

        await behavior.Handle(new KeyedCommand("client-A", DateTimeOffset.UnixEpoch), next, CancellationToken.None);
        await behavior.Handle(new KeyedCommand("client-B", DateTimeOffset.UnixEpoch), next, CancellationToken.None);

        keys[0].Should().NotBe(keys[1]);
    }

    [Fact]
    public async Task Key_Reuse_With_Different_Payload_Throws_When_Detection_Enabled()
    {
        var logger = Substitute.For<ILogger<IdempotencyBehavior<FingerprintedCommand, Result>>>();
        var store = new InMemoryIdempotencyStore();
        var behavior = new IdempotencyBehavior<FingerprintedCommand, Result>(logger, _options, store);
        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());

        await behavior.Handle(new FingerprintedCommand("client-1", Amount: 100), next, CancellationToken.None);

        // Same key, different payload — must surface as key reuse, not a silent replay.
        var act = async () => await behavior.Handle(
            new FingerprintedCommand("client-1", Amount: 999), next, CancellationToken.None);

        await act.Should().ThrowAsync<IdempotencyKeyReuseException>();
    }

    [Fact]
    public async Task Key_Reuse_With_Identical_Payload_Replays_When_Detection_Enabled()
    {
        var logger = Substitute.For<ILogger<IdempotencyBehavior<FingerprintedCommand, Result>>>();
        var store = new InMemoryIdempotencyStore();
        var behavior = new IdempotencyBehavior<FingerprintedCommand, Result>(logger, _options, store);

        var executions = 0;
        RequestHandlerDelegate<Result> next = () =>
        {
            executions++;
            return new ValueTask<Result>(Result.Success());
        };

        await behavior.Handle(new FingerprintedCommand("client-2", Amount: 100), next, CancellationToken.None);
        var replay = await behavior.Handle(new FingerprintedCommand("client-2", Amount: 100), next, CancellationToken.None);

        executions.Should().Be(1, "an identical retry must replay the stored response");
        replay.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Key_Reuse_With_Different_Payload_Replays_Silently_When_Detection_Off()
    {
        // Default behavior (DetectPayloadMismatch not set) is unchanged: same key wins, payload ignored.
        var logger = Substitute.For<ILogger<IdempotencyBehavior<KeyedCommand, Result>>>();
        var store = new InMemoryIdempotencyStore();
        var behavior = new IdempotencyBehavior<KeyedCommand, Result>(logger, _options, store);

        var executions = 0;
        RequestHandlerDelegate<Result> next = () =>
        {
            executions++;
            return new ValueTask<Result>(Result.Success());
        };

        await behavior.Handle(new KeyedCommand("client-3", DateTimeOffset.UnixEpoch), next, CancellationToken.None);
        await behavior.Handle(new KeyedCommand("client-3", DateTimeOffset.UtcNow), next, CancellationToken.None);

        executions.Should().Be(1);
    }
}

[Idempotent(300, KeyProperty = nameof(IdempotencyKey))]
public sealed record KeyedCommand(string IdempotencyKey, DateTimeOffset RequestedAt) : ICommand<Result>;

[Idempotent(300, KeyProperty = nameof(IdempotencyKey), DetectPayloadMismatch = true)]
public sealed record FingerprintedCommand(string IdempotencyKey, decimal Amount) : ICommand<Result>;

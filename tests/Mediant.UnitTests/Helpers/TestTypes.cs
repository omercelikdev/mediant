using Mediant.Abstractions;
using Mediant.Attributes;
using Mediant.Behaviors.Attributes;
using Mediant.Results;

namespace Mediant.UnitTests.Helpers;

// --- Commands ---
public sealed record TestCommand(string Name) : ICommand<Result>;
public sealed record TestCommandWithResponse(string Name) : ICommand<Result<string>>;

[Transactional]
[Auditable]
public sealed record TransactionalCommand(string Data) : ICommand<Result>;

[Retryable(3)]
public sealed record RetryableCommand(string Data) : ICommand<Result>;

[Idempotent(60)]
public sealed record IdempotentCommand(string Data) : ICommand<Result>;

// --- Queries ---
public sealed record TestQuery(int Id) : IQuery<Result<string>>;

[Cacheable(300)]
public sealed record CacheableQuery(int Id) : IQuery<Result<string>>;

// --- Notifications ---
public sealed record TestNotification(string Message) : INotification;

// Dedicated notification for the exception-propagation handler so it does not pollute
// TestNotification fanout when the whole assembly is scanned (all handlers register now).
public sealed record ThrowingTestNotification(string Message) : INotification;
public sealed record TestDomainEvent(string Name) : IDomainEvent
{
    public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;
}

// --- Stream ---
public sealed record TestStreamRequest(int Count) : IStreamRequest<int>;

// --- With sensitive data ---
public sealed record SensitiveCommand(
    string Name,
    [property: SensitiveData] string Password,
    [property: AuditMask("XXXX")] string CreditCard) : ICommand<Result>;

// --- Authorize ---
[Authorize(Roles = "Admin")]
public sealed record AdminCommand(string Data) : ICommand<Result>;

[Authorize(Roles = "Admin,Manager")]
public sealed record MultiRoleCommand(string Data) : ICommand<Result>;

// --- Handlers ---
public sealed class TestCommandHandler : ICommandHandler<TestCommand>
{
    public ValueTask<Result> Handle(TestCommand request, CancellationToken cancellationToken)
    {
        return new ValueTask<Result>(Result.Success());
    }
}

public sealed class TestCommandWithResponseHandler : ICommandHandler<TestCommandWithResponse, Result<string>>
{
    public ValueTask<Result<string>> Handle(TestCommandWithResponse request, CancellationToken cancellationToken)
    {
        return new ValueTask<Result<string>>(Result<string>.Success($"Hello, {request.Name}!"));
    }
}

public sealed class TestQueryHandler : IQueryHandler<TestQuery, Result<string>>
{
    public ValueTask<Result<string>> Handle(TestQuery request, CancellationToken cancellationToken)
    {
        return new ValueTask<Result<string>>(Result<string>.Success($"Item-{request.Id}"));
    }
}

public sealed class TestNotificationHandler1 : INotificationHandler<TestNotification>
{
    public List<string> ReceivedMessages { get; } = new();

    public ValueTask Handle(TestNotification notification, CancellationToken cancellationToken)
    {
        ReceivedMessages.Add(notification.Message);
        return ValueTask.CompletedTask;
    }
}

public sealed class TestNotificationHandler2 : INotificationHandler<TestNotification>
{
    public List<string> ReceivedMessages { get; } = new();

    public ValueTask Handle(TestNotification notification, CancellationToken cancellationToken)
    {
        ReceivedMessages.Add(notification.Message);
        return ValueTask.CompletedTask;
    }
}

public sealed class ThrowingNotificationHandler : INotificationHandler<ThrowingTestNotification>
{
    public ValueTask Handle(ThrowingTestNotification notification, CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("Handler failed");
    }
}

public sealed class TestStreamHandler : IStreamRequestHandler<TestStreamRequest, int>
{
    public async IAsyncEnumerable<int> Handle(TestStreamRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (int i = 0; i < request.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return i;
            await Task.Yield();
        }
    }
}

public sealed class TransactionalCommandHandler : ICommandHandler<TransactionalCommand>
{
    public ValueTask<Result> Handle(TransactionalCommand request, CancellationToken cancellationToken)
        => new(Result.Success());
}

public sealed class RetryableCommandHandler : ICommandHandler<RetryableCommand>
{
    private int _callCount;
    public int CallCount => _callCount;

    public ValueTask<Result> Handle(RetryableCommand request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _callCount);
        if (_callCount < 3) throw new TimeoutException("Transient failure");
        return new(Result.Success());
    }
}

public sealed class IdempotentCommandHandler : ICommandHandler<IdempotentCommand>
{
    public ValueTask<Result> Handle(IdempotentCommand request, CancellationToken cancellationToken)
        => new(Result.Success());
}

public sealed class AdminCommandHandler : ICommandHandler<AdminCommand>
{
    public ValueTask<Result> Handle(AdminCommand request, CancellationToken cancellationToken)
        => new(Result.Success());
}

public sealed class SensitiveCommandHandler : ICommandHandler<SensitiveCommand>
{
    public ValueTask<Result> Handle(SensitiveCommand request, CancellationToken cancellationToken)
        => new(Result.Success());
}

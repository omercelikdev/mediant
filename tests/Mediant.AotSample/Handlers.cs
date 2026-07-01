using System.Runtime.CompilerServices;
using Mediant.Abstractions;
using Mediant.Results;

namespace Mediant.AotSample;

public sealed record PingCommand(string Message) : ICommand<Result<string>>;

public sealed class PingHandler : ICommandHandler<PingCommand, Result<string>>
{
    public ValueTask<Result<string>> Handle(PingCommand request, CancellationToken cancellationToken)
        => ValueTask.FromResult(Result<string>.Success(request.Message));
}

public sealed record PingNotification(string Name) : INotification;

public sealed class PingNotificationHandler : INotificationHandler<PingNotification>
{
    public ValueTask Handle(PingNotification notification, CancellationToken cancellationToken)
        => ValueTask.CompletedTask;
}

public sealed record NumbersRequest(int Count) : IStreamRequest<int>;

public sealed class NumbersRequestHandler : IStreamRequestHandler<NumbersRequest, int>
{
    public async IAsyncEnumerable<int> Handle(NumbersRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (int i = 1; i <= request.Count; i++)
        {
            await Task.Yield();
            yield return i;
        }
    }
}

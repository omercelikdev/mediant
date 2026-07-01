using Microsoft.Extensions.Logging;
using Mediant.Abstractions;
using Mediant.Behaviors.Behaviors;
using Mediant.Results;
using Mediant.UnitTests.Helpers;

namespace Mediant.UnitTests.Behaviors;

public class UnhandledExceptionBehaviorTests
{
    [Fact]
    public async Task Should_Propagate_Exception_After_Logging()
    {
        var logger = Substitute.For<ILogger<UnhandledExceptionBehavior<TestCommand, Result>>>();
        var behavior = new UnhandledExceptionBehavior<TestCommand, Result>(logger);

        RequestHandlerDelegate<Result> next = () => throw new InvalidOperationException("unexpected");

        var act = async () => await behavior.Handle(new TestCommand("test"), next, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("unexpected");
    }

    [Fact]
    public async Task Should_Pass_Through_On_Success()
    {
        var logger = Substitute.For<ILogger<UnhandledExceptionBehavior<TestCommand, Result>>>();
        var behavior = new UnhandledExceptionBehavior<TestCommand, Result>(logger);

        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());

        var result = await behavior.Handle(new TestCommand("test"), next, CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
    }
}

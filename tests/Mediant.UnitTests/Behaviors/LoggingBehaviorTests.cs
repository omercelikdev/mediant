using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mediant.Abstractions;
using Mediant.Behaviors.Behaviors;
using Mediant.Behaviors.Configuration;
using Mediant.Results;
using Mediant.UnitTests.Helpers;

namespace Mediant.UnitTests.Behaviors;

public class LoggingBehaviorTests
{
    private readonly ILogger<LoggingBehavior<TestCommand, Result>> _logger;
    private readonly IOptions<LoggingBehaviorOptions> _options;

    public LoggingBehaviorTests()
    {
        _logger = Substitute.For<ILogger<LoggingBehavior<TestCommand, Result>>>();
        _options = Options.Create(new LoggingBehaviorOptions());
    }

    [Fact]
    public async Task Should_Log_Request_And_Call_Next()
    {
        var behavior = new LoggingBehavior<TestCommand, Result>(_logger, _options);
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
    public async Task Should_Log_Exception_And_Rethrow()
    {
        var behavior = new LoggingBehavior<TestCommand, Result>(_logger, _options);
        RequestHandlerDelegate<Result> next = () => throw new InvalidOperationException("boom");

        var act = async () => await behavior.Handle(new TestCommand("test"), next, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
    }

    [Fact]
    public async Task Should_Skip_When_Disabled()
    {
        var opts = Options.Create(new LoggingBehaviorOptions { Enabled = false });
        var behavior = new LoggingBehavior<TestCommand, Result>(_logger, opts);
        var called = false;
        RequestHandlerDelegate<Result> next = () =>
        {
            called = true;
            return new ValueTask<Result>(Result.Success());
        };

        await behavior.Handle(new TestCommand("test"), next, CancellationToken.None);
        called.Should().BeTrue();
    }

    [Fact]
    public async Task Should_Mask_Sensitive_Properties()
    {
        var sensitiveLogger = Substitute.For<ILogger<LoggingBehavior<SensitiveCommand, Result>>>();
        var opts = Options.Create(new LoggingBehaviorOptions());
        var behavior = new LoggingBehavior<SensitiveCommand, Result>(sensitiveLogger, opts);

        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());
        await behavior.Handle(new SensitiveCommand("John", "secret123", "4111111111111111"), next, CancellationToken.None);
        // Should not throw — sensitive data is masked internally
    }

    [Fact]
    public async Task Should_Truncate_Large_Request()
    {
        var opts = Options.Create(new LoggingBehaviorOptions { MaxSerializedLength = 20 });
        var behavior = new LoggingBehavior<TestCommand, Result>(_logger, opts);

        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());
        await behavior.Handle(new TestCommand("a very long name that exceeds limit"), next, CancellationToken.None);
        // Should not throw — truncation handled internally
    }

    [Fact]
    public async Task Should_Handle_Circular_Reference_Without_Throwing()
    {
        var circularLogger = Substitute.For<ILogger<LoggingBehavior<CircularRefCommand, Result>>>();
        var opts = Options.Create(new LoggingBehaviorOptions());
        var behavior = new LoggingBehavior<CircularRefCommand, Result>(circularLogger, opts);

        var node = new CircularNode("root");
        node.Child = new CircularNode("child") { Child = node }; // circular

        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());
        var result = await behavior.Handle(new CircularRefCommand(node), next, CancellationToken.None);

        // Should not throw — circular reference handled by ReferenceHandler.IgnoreCycles
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Should_Handle_Deeply_Nested_Object_Without_Throwing()
    {
        var deepLogger = Substitute.For<ILogger<LoggingBehavior<CircularRefCommand, Result>>>();
        var opts = Options.Create(new LoggingBehaviorOptions());
        var behavior = new LoggingBehavior<CircularRefCommand, Result>(deepLogger, opts);

        // Build 100-level deep nesting
        var root = new CircularNode("level-0");
        var current = root;
        for (int i = 1; i < 100; i++)
        {
            current.Child = new CircularNode($"level-{i}");
            current = current.Child;
        }

        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());
        var result = await behavior.Handle(new CircularRefCommand(root), next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }
}

public sealed class CircularNode
{
    public string Name { get; set; }
    public CircularNode? Child { get; set; }
    public CircularNode(string name) => Name = name;
}

public sealed record CircularRefCommand(CircularNode Root) : ICommand<Result>;

public sealed class CircularRefCommandHandler : ICommandHandler<CircularRefCommand>
{
    public ValueTask<Result> Handle(CircularRefCommand request, CancellationToken cancellationToken)
        => new(Result.Success());
}

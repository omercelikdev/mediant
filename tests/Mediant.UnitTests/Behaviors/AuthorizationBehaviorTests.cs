using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mediant.Abstractions;
using Mediant.Behaviors.Behaviors;
using Mediant.Behaviors.Configuration;
using Mediant.Results;
using Mediant.UnitTests.Helpers;

namespace Mediant.UnitTests.Behaviors;

public class AuthorizationBehaviorTests
{
    private readonly ILogger<AuthorizationBehavior<AdminCommand, Result>> _logger =
        Substitute.For<ILogger<AuthorizationBehavior<AdminCommand, Result>>>();
    private readonly IOptions<AuthorizationBehaviorOptions> _options =
        Options.Create(new AuthorizationBehaviorOptions());

    [Fact]
    public async Task Should_Return_Unauthorized_When_No_Context()
    {
        var behavior = new AuthorizationBehavior<AdminCommand, Result>(_logger, _options, authContext: null);
        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());

        var result = await behavior.Handle(new AdminCommand("test"), next, CancellationToken.None);
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Unauthorized);
    }

    [Fact]
    public async Task Should_Return_Unauthorized_When_Not_Authenticated()
    {
        var ctx = Substitute.For<IAuthorizationContext>();
        ctx.IsAuthenticated.Returns(false);

        var behavior = new AuthorizationBehavior<AdminCommand, Result>(_logger, _options, ctx);
        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());

        var result = await behavior.Handle(new AdminCommand("test"), next, CancellationToken.None);
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Unauthorized);
    }

    [Fact]
    public async Task Should_Return_Forbidden_When_Missing_Role()
    {
        var ctx = Substitute.For<IAuthorizationContext>();
        ctx.IsAuthenticated.Returns(true);
        ctx.Roles.Returns(new List<string> { "User" });

        var behavior = new AuthorizationBehavior<AdminCommand, Result>(_logger, _options, ctx);
        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());

        var result = await behavior.Handle(new AdminCommand("test"), next, CancellationToken.None);
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Forbidden);
    }

    [Fact]
    public async Task Should_Pass_When_Has_Required_Role()
    {
        var ctx = Substitute.For<IAuthorizationContext>();
        ctx.IsAuthenticated.Returns(true);
        ctx.Roles.Returns(new List<string> { "Admin" });

        var behavior = new AuthorizationBehavior<AdminCommand, Result>(_logger, _options, ctx);
        var called = false;
        RequestHandlerDelegate<Result> next = () => { called = true; return new ValueTask<Result>(Result.Success()); };

        var result = await behavior.Handle(new AdminCommand("test"), next, CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        called.Should().BeTrue();
    }

    [Fact]
    public async Task Should_Require_All_Roles_For_MultiRole()
    {
        var multiLogger = Substitute.For<ILogger<AuthorizationBehavior<MultiRoleCommand, Result>>>();
        var ctx = Substitute.For<IAuthorizationContext>();
        ctx.IsAuthenticated.Returns(true);
        ctx.Roles.Returns(new List<string> { "Admin" }); // Missing "Manager"

        var behavior = new AuthorizationBehavior<MultiRoleCommand, Result>(multiLogger, _options, ctx);
        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());

        var result = await behavior.Handle(new MultiRoleCommand("test"), next, CancellationToken.None);
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Forbidden);
    }

    [Fact]
    public async Task Should_Skip_When_No_Authorize_Attribute()
    {
        var cmdLogger = Substitute.For<ILogger<AuthorizationBehavior<TestCommand, Result>>>();
        var behavior = new AuthorizationBehavior<TestCommand, Result>(cmdLogger, _options, authContext: null);
        var called = false;
        RequestHandlerDelegate<Result> next = () => { called = true; return new ValueTask<Result>(Result.Success()); };

        var result = await behavior.Handle(new TestCommand("test"), next, CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        called.Should().BeTrue();
    }

    [Fact]
    public async Task Should_Skip_When_Disabled()
    {
        var opts = Options.Create(new AuthorizationBehaviorOptions { Enabled = false });
        var behavior = new AuthorizationBehavior<AdminCommand, Result>(_logger, opts, authContext: null);
        var called = false;
        RequestHandlerDelegate<Result> next = () => { called = true; return new ValueTask<Result>(Result.Success()); };

        var result = await behavior.Handle(new AdminCommand("test"), next, CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        called.Should().BeTrue();
    }
}

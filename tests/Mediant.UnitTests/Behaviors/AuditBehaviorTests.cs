using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mediant.Abstractions;
using Mediant.Audit;
using Mediant.Behaviors.Attributes;
using Mediant.Behaviors.Behaviors;
using Mediant.Behaviors.Configuration;
using Mediant.Results;
using Mediant.UnitTests.Helpers;

namespace Mediant.UnitTests.Behaviors;

public class AuditBehaviorTests
{
    private readonly InMemoryAuditStore _store = new();
    private readonly ILogger<AuditBehavior<TransactionalCommand, Result>> _logger =
        Substitute.For<ILogger<AuditBehavior<TransactionalCommand, Result>>>();

    private AuditBehavior<TransactionalCommand, Result> CreateBehavior(
        AuditBehaviorOptions? opts = null,
        IAuditUserContext? userContext = null)
    {
        var options = Options.Create(opts ?? new AuditBehaviorOptions { AuditCommands = true });
        return new AuditBehavior<TransactionalCommand, Result>(_store, _logger, options, userContext);
    }

    [Fact]
    public async Task Should_Create_Audit_Entry_On_Success()
    {
        var behavior = CreateBehavior();
        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());

        await behavior.Handle(new TransactionalCommand("data"), next, CancellationToken.None);

        var entries = _store.GetAll();
        entries.Should().ContainSingle();
        entries[0].IsSuccess.Should().BeTrue();
        entries[0].RequestType.Should().Contain("TransactionalCommand");
        entries[0].DurationMs.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task Should_Create_Audit_Entry_On_Failure()
    {
        var behavior = CreateBehavior();
        RequestHandlerDelegate<Result> next = () => throw new InvalidOperationException("boom");

        var act = async () => await behavior.Handle(new TransactionalCommand("data"), next, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();

        var entries = _store.GetAll();
        entries.Should().ContainSingle();
        entries[0].IsSuccess.Should().BeFalse();
        entries[0].ErrorMessage.Should().Be("boom");
        entries[0].ExceptionType.Should().Contain("InvalidOperationException");
    }

    [Fact]
    public async Task Should_Use_SYSTEM_When_No_User_Context()
    {
        var behavior = CreateBehavior(userContext: null);
        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());

        await behavior.Handle(new TransactionalCommand("data"), next, CancellationToken.None);

        _store.GetAll()[0].UserId.Should().Be("SYSTEM");
    }

    [Fact]
    public async Task Should_Use_User_From_Context()
    {
        var userCtx = Substitute.For<IAuditUserContext>();
        userCtx.GetUserId().Returns("user-42");
        userCtx.GetUserName().Returns("John Doe");

        var behavior = CreateBehavior(userContext: userCtx);
        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());

        await behavior.Handle(new TransactionalCommand("data"), next, CancellationToken.None);

        var entry = _store.GetAll()[0];
        entry.UserId.Should().Be("user-42");
        entry.UserName.Should().Be("John Doe");
    }

    [Fact]
    public async Task Should_Mask_Sensitive_Properties_In_Request_Data()
    {
        var sensitiveLogger = Substitute.For<ILogger<AuditBehavior<SensitiveCommand, Result>>>();
        var opts = new AuditBehaviorOptions { AuditCommands = true };
        opts.SensitivePatterns.Add("Password");
        var behavior = new AuditBehavior<SensitiveCommand, Result>(
            _store, sensitiveLogger, Options.Create(opts));

        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());

        await behavior.Handle(new SensitiveCommand("John", "secret", "4111"), next, CancellationToken.None);

        var entry = _store.GetAll()[0];
        entry.RequestData.Should().NotBeNull();
        entry.RequestData.Should().Contain("***"); // Password masked
        entry.RequestData.Should().NotContain("secret");
    }

    [Fact]
    public async Task Should_Skip_When_Disabled()
    {
        var behavior = CreateBehavior(new AuditBehaviorOptions { Enabled = false });
        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());

        await behavior.Handle(new TransactionalCommand("data"), next, CancellationToken.None);

        _store.GetAll().Should().BeEmpty();
    }

    [Fact]
    public async Task Should_Skip_Queries_When_AuditQueries_False()
    {
        var queryLogger = Substitute.For<ILogger<AuditBehavior<TestQuery, Result<string>>>>();
        var opts = new AuditBehaviorOptions { AuditCommands = true, AuditQueries = false };
        var behavior = new AuditBehavior<TestQuery, Result<string>>(
            _store, queryLogger, Options.Create(opts));

        RequestHandlerDelegate<Result<string>> next = () =>
            new ValueTask<Result<string>>(Result<string>.Success("ok"));

        await behavior.Handle(new TestQuery(1), next, CancellationToken.None);

        _store.GetAll().Should().BeEmpty();
    }

    [Fact]
    public async Task Should_Not_Fail_Request_When_Store_Throws()
    {
        var failingStore = Substitute.For<IAuditStore>();
        failingStore.SaveAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>())
            .Returns(x => throw new Exception("store down"));

        var opts = new AuditBehaviorOptions { AuditCommands = true, FallbackToConsole = true };
        var behavior = new AuditBehavior<TransactionalCommand, Result>(
            failingStore, _logger, Options.Create(opts));

        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());

        // Should NOT throw even though audit store fails
        var result = await behavior.Handle(new TransactionalCommand("data"), next, CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Should_Generate_CorrelationId_When_No_Activity()
    {
        var behavior = CreateBehavior();
        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());

        await behavior.Handle(new TransactionalCommand("data"), next, CancellationToken.None);

        _store.GetAll()[0].CorrelationId.Should().NotBeNullOrEmpty();
    }

    // --- IAuditableRequest metadata tests ---

    [Auditable]
    public sealed record AuditableMetadataCommand(string OrderId) : ICommand<Result>, IAuditableRequest
    {
        public string ActionName => "Order.Create";
        public string? EntityType => "Order";
        public string? EntityId => OrderId;
        public object? AuditMetadata => new { Source = "API", Priority = "High" };
    }

    public sealed record AuditableWithoutAttributeCommand(string Data) : ICommand<Result>, IAuditableRequest
    {
        public string ActionName => "Data.Process";
        public string? EntityType => null;
        public string? EntityId => null;
        public object? AuditMetadata => null;
    }

    [Fact]
    public async Task Should_Populate_ActionName_EntityType_EntityId_From_IAuditableRequest()
    {
        var logger = Substitute.For<ILogger<AuditBehavior<AuditableMetadataCommand, Result>>>();
        var opts = new AuditBehaviorOptions { AuditCommands = true };
        var behavior = new AuditBehavior<AuditableMetadataCommand, Result>(
            _store, logger, Options.Create(opts));

        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());

        await behavior.Handle(new AuditableMetadataCommand("ORD-123"), next, CancellationToken.None);

        var entries = _store.GetAll();
        entries.Should().ContainSingle();
        var entry = entries[0];
        entry.ActionName.Should().Be("Order.Create");
        entry.EntityType.Should().Be("Order");
        entry.EntityId.Should().Be("ORD-123");
        entry.Metadata.Should().ContainKey("AuditMetadata");
        entry.Metadata["AuditMetadata"].Should().Contain("API");
        entry.Metadata["AuditMetadata"].Should().Contain("High");
    }

    [Fact]
    public async Task Should_Audit_IAuditableRequest_Without_Auditable_Attribute()
    {
        var logger = Substitute.For<ILogger<AuditBehavior<AuditableWithoutAttributeCommand, Result>>>();
        var opts = new AuditBehaviorOptions { AuditCommands = false, AuditQueries = false };
        var behavior = new AuditBehavior<AuditableWithoutAttributeCommand, Result>(
            _store, logger, Options.Create(opts));

        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());

        await behavior.Handle(new AuditableWithoutAttributeCommand("test"), next, CancellationToken.None);

        var entries = _store.GetAll();
        entries.Should().ContainSingle();
        entries[0].ActionName.Should().Be("Data.Process");
        entries[0].EntityType.Should().BeNull();
        entries[0].EntityId.Should().BeNull();
    }

    [Fact]
    public async Task Should_Not_Populate_Metadata_Fields_Without_IAuditableRequest()
    {
        var behavior = CreateBehavior();
        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());

        await behavior.Handle(new TransactionalCommand("data"), next, CancellationToken.None);

        var entry = _store.GetAll()[0];
        entry.ActionName.Should().BeNull();
        entry.EntityType.Should().BeNull();
        entry.EntityId.Should().BeNull();
    }

    [Fact]
    public async Task Should_Capture_Metadata_On_Failure_With_IAuditableRequest()
    {
        var logger = Substitute.For<ILogger<AuditBehavior<AuditableMetadataCommand, Result>>>();
        var opts = new AuditBehaviorOptions { AuditCommands = true };
        var behavior = new AuditBehavior<AuditableMetadataCommand, Result>(
            _store, logger, Options.Create(opts));

        RequestHandlerDelegate<Result> next = () => throw new InvalidOperationException("fail");

        var act = () => behavior.Handle(
            new AuditableMetadataCommand("ORD-456"), next, CancellationToken.None).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>();

        var entries = _store.GetAll();
        entries.Should().ContainSingle();
        var entry = entries[0];
        entry.IsSuccess.Should().BeFalse();
        entry.ActionName.Should().Be("Order.Create");
        entry.EntityId.Should().Be("ORD-456");
        entry.ErrorMessage.Should().Contain("fail");
    }
}

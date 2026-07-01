using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mediant.Abstractions;
using Mediant.Audit;
using Mediant.Behaviors.DependencyInjection;
using Mediant.Behaviors.UserContext;
using Mediant.DependencyInjection;
using Mediant.FluentValidation;
using Mediant.Results;
using Mediant.Sample.ECommerce.Commands;
using Mediant.Sample.ECommerce.Infrastructure;
using Mediant.Sample.ECommerce.Queries;

namespace Mediant.IntegrationTests;

public class FullPipelineTests
{
    private ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));

        services.AddQorpeMediator(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(CreateOrderCommand).Assembly);
            cfg.NotificationPublishStrategy = NotificationPublishStrategy.Sequential;
        });

        services.AddQorpeValidation(typeof(CreateOrderCommand).Assembly);
        services.AddQorpeAllBehaviors(opts =>
        {
            opts.ConfigureAudit = a => { a.AuditCommands = true; a.AuditQueries = false; };
            opts.ConfigurePerformance = p => { p.WarningThresholdMs = 500; };
        });

        services.AddSingleton<InMemoryOrderRepository>();
        services.AddSingleton<FakePaymentGateway>();
        services.AddSingleton<IUnitOfWork, InMemoryUnitOfWork>();
        services.AddSingleton<IAuditStore, InMemoryAuditStore>();
        services.AddSingleton<IAuditUserContext, SystemUserContextProvider>();

        // Add auth context for [Authorize] tests
        var authCtx = Substitute.For<IAuthorizationContext>();
        authCtx.IsAuthenticated.Returns(true);
        authCtx.UserId.Returns("test-user");
        authCtx.Roles.Returns(new List<string> { "Admin", "Owner", "Manager" });
        authCtx.HasClaim(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        services.AddSingleton(authCtx);

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task CreateOrder_FullPipeline_ShouldSucceed()
    {
        using var sp = BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        var command = new CreateOrderCommand
        {
            UserId = "user-1",
            Items = new List<CreateOrderItemDto>
            {
                new() { ProductId = "P1", ProductName = "Widget", Quantity = 2, UnitPrice = 10.00m }
            }
        };

        var result = await mediator.Send(command);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBe(Guid.Empty);

        // Verify audit was created
        var auditStore = sp.GetRequiredService<IAuditStore>() as InMemoryAuditStore;
        auditStore!.GetAll().Should().NotBeEmpty();
    }

    [Fact]
    public async Task CreateOrder_WithValidationError_ShouldReturnFailure()
    {
        using var sp = BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        var command = new CreateOrderCommand
        {
            UserId = "", // Empty — validation should fail
            Items = new List<CreateOrderItemDto>()
        };

        var result = await mediator.Send(command);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public async Task GetOrder_AfterCreate_ShouldReturnOrder()
    {
        using var sp = BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        // Create
        var createResult = await mediator.Send(new CreateOrderCommand
        {
            UserId = "user-2",
            Items = new List<CreateOrderItemDto>
            {
                new() { ProductId = "P1", ProductName = "Gadget", Quantity = 1, UnitPrice = 25.00m }
            }
        });
        createResult.IsSuccess.Should().BeTrue();

        // Query
        var queryResult = await mediator.Send(new GetOrderByIdQuery { Id = createResult.Value });

        queryResult.IsSuccess.Should().BeTrue();
        queryResult.Value.UserId.Should().Be("user-2");
        queryResult.Value.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task GetOrder_NotFound_ShouldReturnNotFoundError()
    {
        using var sp = BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        var result = await mediator.Send(new GetOrderByIdQuery { Id = Guid.NewGuid() });

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task CancelOrder_AfterCreate_ShouldSucceed()
    {
        using var sp = BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        // Create
        var createResult = await mediator.Send(new CreateOrderCommand
        {
            UserId = "user-3",
            Items = new List<CreateOrderItemDto>
            {
                new() { ProductId = "P1", ProductName = "Item", Quantity = 1, UnitPrice = 5.00m }
            }
        });

        // Cancel
        var cancelResult = await mediator.Send(new CancelOrderCommand { Id = createResult.Value });
        cancelResult.IsSuccess.Should().BeTrue();

        // Verify status
        var order = await mediator.Send(new GetOrderByIdQuery { Id = createResult.Value });
        order.Value.Status.Should().Be(Sample.ECommerce.Domain.OrderStatus.Cancelled);
    }

    [Fact]
    public async Task CancelOrder_NotFound_ShouldReturnNotFound()
    {
        using var sp = BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        var result = await mediator.Send(new CancelOrderCommand { Id = Guid.NewGuid() });
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task GetOrdersForUser_ShouldReturnUserOrders()
    {
        using var sp = BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        // Create 2 orders for same user
        await mediator.Send(new CreateOrderCommand
        {
            UserId = "user-4",
            Items = new List<CreateOrderItemDto>
            {
                new() { ProductId = "P1", ProductName = "A", Quantity = 1, UnitPrice = 10m }
            }
        });
        await mediator.Send(new CreateOrderCommand
        {
            UserId = "user-4",
            Items = new List<CreateOrderItemDto>
            {
                new() { ProductId = "P2", ProductName = "B", Quantity = 2, UnitPrice = 20m }
            }
        });

        var result = await mediator.Send(new GetOrdersForUserQuery { UserId = "user-4" });
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
    }

    [Fact]
    public async Task SearchOrders_Stream_ShouldReturnResults()
    {
        using var sp = BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        // Create an order
        await mediator.Send(new CreateOrderCommand
        {
            UserId = "user-5",
            Items = new List<CreateOrderItemDto>
            {
                new() { ProductId = "P1", ProductName = "Stream", Quantity = 1, UnitPrice = 15m }
            }
        });

        var orders = new List<Sample.ECommerce.Domain.Order>();
        await foreach (var order in mediator.CreateStream(
            new SearchOrdersQuery { UserId = "user-5" }))
        {
            orders.Add(order);
        }

        orders.Should().NotBeEmpty();
    }

    [Fact]
    public async Task AuditTrail_ShouldCaptureAllCommands()
    {
        using var sp = BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();
        var auditStore = sp.GetRequiredService<IAuditStore>() as InMemoryAuditStore;
        auditStore!.Clear();

        // Execute several commands
        await mediator.Send(new CreateOrderCommand
        {
            UserId = "audit-user",
            Items = new List<CreateOrderItemDto>
            {
                new() { ProductId = "P1", ProductName = "Audit", Quantity = 1, UnitPrice = 10m }
            }
        });

        var entries = auditStore.GetAll();
        entries.Should().NotBeEmpty();
        entries.Should().Contain(e => e.RequestType.Contains("CreateOrderCommand"));
        entries.Should().Contain(e => e.UserId == "SYSTEM");
    }
}

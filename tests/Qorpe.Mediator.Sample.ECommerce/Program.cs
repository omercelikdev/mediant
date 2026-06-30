using Qorpe.Mediator.Abstractions;
using Qorpe.Mediator.AspNetCore.Extensions;
using Qorpe.Mediator.AspNetCore.Mapping;
using Qorpe.Mediator.Audit;
using Qorpe.Mediator.Behaviors.DependencyInjection;
using Qorpe.Mediator.DependencyInjection;
using Qorpe.Mediator.FluentValidation;
using Qorpe.Mediator.Sample.ECommerce.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Register Qorpe.Mediator with all behaviors
builder.Services.AddQorpeMediator(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(Program).Assembly);
    cfg.NotificationPublishStrategy = NotificationPublishStrategy.Parallel;
});

// Add FluentValidation
builder.Services.AddQorpeValidation(typeof(Program).Assembly);

// Add all 9 behaviors in recommended pipeline order
builder.Services.AddQorpeAllBehaviors(opts =>
{
    opts.ConfigureAudit = audit =>
    {
        audit.AuditCommands = true;
        audit.AuditQueries = false;
        audit.FallbackToConsole = true;
    };
    opts.ConfigureLogging = logging =>
    {
        logging.MaskProperties.Add("CardNumber");
        logging.MaxSerializedLength = 4096;
    };
    opts.ConfigurePerformance = perf =>
    {
        perf.WarningThresholdMs = 500;
        perf.CriticalThresholdMs = 5000;
    };
});

// Add ASP.NET Core endpoint support
builder.Services.AddQorpeEndpoints(opts => opts.UseProblemDetails = true);

// Register infrastructure services
builder.Services.AddSingleton<InMemoryOrderRepository>();
builder.Services.AddSingleton<FakePaymentGateway>();
builder.Services.AddSingleton<IUnitOfWork, InMemoryUnitOfWork>();
builder.Services.AddSingleton<IAuditStore, InMemoryAuditStore>();

var app = builder.Build();

// Map all [HttpEndpoint] attributed commands and queries
app.MapQorpeEndpoints(typeof(Program).Assembly);

// Health check
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTimeOffset.UtcNow }));

app.Run();

// For integration tests
public partial class Program;

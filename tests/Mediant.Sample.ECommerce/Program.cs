using Mediant.Abstractions;
using Mediant.AspNetCore.Extensions;
using Mediant.AspNetCore.Mapping;
using Mediant.Audit;
using Mediant.Behaviors.DependencyInjection;
using Mediant.DependencyInjection;
using Mediant.FluentValidation;
using Mediant.Sample.ECommerce.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Register Mediant with all behaviors
builder.Services.AddMediant(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(Program).Assembly);
    cfg.NotificationPublishStrategy = NotificationPublishStrategy.Parallel;
});

// Add FluentValidation
builder.Services.AddMediantValidation(typeof(Program).Assembly);

// Add all 9 behaviors in recommended pipeline order
builder.Services.AddMediantAllBehaviors(opts =>
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
builder.Services.AddMediantEndpoints(opts => opts.UseProblemDetails = true);

// Register infrastructure services
builder.Services.AddSingleton<InMemoryOrderRepository>();
builder.Services.AddSingleton<FakePaymentGateway>();
builder.Services.AddSingleton<IUnitOfWork, InMemoryUnitOfWork>();
builder.Services.AddSingleton<IAuditStore, InMemoryAuditStore>();

var app = builder.Build();

// Map all [HttpEndpoint] attributed commands and queries
app.MapMediantEndpoints(typeof(Program).Assembly);

// Health check
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTimeOffset.UtcNow }));

app.Run();

// For integration tests
public partial class Program;

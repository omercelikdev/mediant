# Qorpe.Mediator

**Enterprise-Grade CQRS Mediator for .NET**

[![NuGet](https://img.shields.io/nuget/vpre/Qorpe.Mediator.svg)](https://www.nuget.org/packages/Qorpe.Mediator/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Qorpe.Mediator.svg)](https://www.nuget.org/packages/Qorpe.Mediator/)
[![Build](https://github.com/qorpe/mediator/actions/workflows/ci.yml/badge.svg)](https://github.com/qorpe/mediator/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%209.0%20%7C%2010.0-blue)](https://dotnet.microsoft.com/)

A production-ready CQRS mediator library for .NET with Result pattern, pipeline behaviors, DDD support, and attribute-based HTTP endpoint mapping.

- - -

## Why Qorpe.Mediator?

- **Result Pattern built-in** — No more throwing exceptions for control flow
- **Explicit CQRS** — `ICommand<T>`, `IQuery<T>` instead of just `IRequest`
- **10 built-in behaviors** — Audit, logging, validation, auth, transactions, retry, caching, performance, idempotency, cache invalidation
- **Attribute-based HTTP endpoints** — `[HttpEndpoint]` eliminates controller boilerplate
- **DDD native** — `IDomainEvent`, aggregate root patterns
- **Publish performance** — Up to ~65% faster notification dispatch with 4.7x less memory ([benchmarks](docs/BENCHMARKS.md))
- **300 tests** — Unit, integration, load, E2E

- - -

## Performance

Benchmarked against MediatR v12 using BenchmarkDotNet (Apple M5, .NET 10). These measure
**framework dispatch overhead with no-op handlers** — real handlers doing I/O narrow the relative
gap. Full results and method: [docs/BENCHMARKS.md](docs/BENCHMARKS.md)

### Publish (Notification Dispatch) — Qorpe wins

| Handlers | Qorpe | MediatR v12 | Result |
|----------|-------|-------------|--------|
| 1 handler | 24 ns / 88 B | 44 ns / 288 B | **46% faster, 3.3x less memory** |
| 10 handlers | 69 ns / 376 B | 184 ns / 1,656 B | **63% faster, 4.4x less memory** |
| 100 handlers | 527 ns / 3,256 B | 1,521 ns / 15,336 B | **65% faster, 4.7x less memory** |

### Send (Pipeline) — Equal speed, Qorpe uses less memory

| Behaviors | Qorpe | MediatR v12 | Result |
|-----------|-------|-------------|--------|
| 1 behavior | 59 ns / 288 B | 57 ns / 368 B | ~equal speed, **22% less memory** |
| 2 behaviors | 75 ns / 424 B | 70 ns / 512 B | ~equal speed, **17% less memory** |
| 4 behaviors | 102 ns / 696 B | 100 ns / 800 B | ~equal speed, **13% less memory** |
| 8 behaviors | 150 ns / 1,240 B | 159 ns / 1,376 B | **Qorpe 6% faster, 10% less memory** |

- - -

## Quick Start

### 1. Install

```bash
dotnet add package Qorpe.Mediator
```

### 2. Register

```csharp
builder.Services.AddQorpeMediator(cfg =>
    cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));
```

### 3. Define a Command

```csharp
public record CreateOrderCommand(string UserId, List<OrderItem> Items)
    : ICommand<Result<Guid>>;
```

### 4. Handle it

```csharp
public class CreateOrderHandler : ICommandHandler<CreateOrderCommand, Result<Guid>>
{
    public ValueTask<Result<Guid>> Handle(CreateOrderCommand request, CancellationToken ct)
    {
        var orderId = Guid.NewGuid();
        // ... create order
        return new ValueTask<Result<Guid>>(Result<Guid>.Success(orderId));
    }
}
```

### 5. Send

```csharp
var result = await mediator.Send(new CreateOrderCommand("user-1", items));
result.Match(
    id => Console.WriteLine($"Order created: {id}"),
    error => Console.WriteLine($"Failed: {error}")
);
```

- - -

## Features

### CQRS Separation

```csharp
// Commands — change state
public record CreateOrder(string UserId) : ICommand<Result<Guid>>;
public record CancelOrder(Guid Id) : ICommand<Result>;

// Queries — read state, no side effects
public record GetOrderById(Guid Id) : IQuery<Result<Order>>;

// Streaming
public record SearchOrders(string? Status) : IStreamRequest<Order>;
```

### Result Pattern

```csharp
// Success
return Result<Guid>.Success(orderId);

// Failure with typed errors
return Error.NotFound("Order.NotFound", "Order not found");

// Functional operations
var name = result.Map(order => order.Name)
                 .Bind(name => ValidateName(name))
                 .Match(
                     name => $"Hello, {name}",
                     error => $"Error: {error.Description}");
```

### Domain Events

```csharp
public record OrderCreatedEvent(Guid OrderId, string UserId) : IDomainEvent
{
    public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;
}

// Multiple handlers per event
public class SendEmail : INotificationHandler<OrderCreatedEvent> { ... }
public class UpdateInventory : INotificationHandler<OrderCreatedEvent> { ... }

await publisher.Publish(new OrderCreatedEvent(orderId, userId));
```

### HTTP Endpoint Mapping

```csharp
[HttpEndpoint("POST", "/api/orders", Tags = new[] { "Orders" })]
[Transactional]
[Auditable]
public record CreateOrderCommand : ICommand<Result<Guid>> { ... }

// In Program.cs
app.MapQorpeEndpoints(typeof(Program).Assembly);
// Result auto-mapped: Success->200/201, Validation->400, NotFound->404, etc.
```

- - -

## Pipeline Behaviors

All behaviors are attribute-driven, configurable, and automatically ordered via `IBehaviorOrder`.

| # | Behavior | Attribute | Order | Description |
|---|----------|-----------|-------|-------------|
| 1 | **Audit** | `[Auditable]` | 100 | Async batching, store abstraction, sensitive data masking |
| 2 | **Logging** | Auto | 200 | Structured logging, auto-mask, circular reference safe |
| 3 | **UnhandledException** | Auto | 300 | Catch-all safety net, always re-throws |
| 4 | **Authorization** | `[Authorize]` | 400 | Role + policy checking, Result-based responses |
| 5 | **Validation** | Auto | 500 | FluentValidation multi-validator, Result.Failure |
| 6 | **Idempotency** | `[Idempotent]` | 600 | SHA256 or client key, per-key locking, window expiry, `IDistributedCache` store |
| 7 | **Transaction** | `[Transactional]` | 700 | Command-only, rollback, distinct commit/handler errors |
| 8 | **Performance** | `[PerformanceThreshold]` | 800 | Per-request thresholds, 30s hard ceiling |
| 9 | **Retry** | `[Retryable]` | 900 | Exponential backoff with jitter, success attempt logging |
| 10 | **Caching** | `[Cacheable]` | 1000 | Query-only, bounded lock pool, stampede prevention |
| 11 | **Cache Invalidation** | `[InvalidatesCache]` | 1001 | Command-driven cache invalidation by key prefix |

### Full Configuration

```csharp
builder.Services.AddQorpeMediator(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(Program).Assembly);
    cfg.NotificationPublishStrategy = NotificationPublishStrategy.Parallel;
    cfg.EnablePolymorphicNotifications = true;
    cfg.ValidateOnStartup = true;

    // Open-generic behavior that wraps every request (runs in IBehaviorOrder order).
    cfg.AddOpenBehavior(typeof(MyLoggingBehavior<,>));
});

builder.Services.AddQorpeValidation(typeof(Program).Assembly);

builder.Services.AddQorpeAllBehaviors(opts =>
{
    opts.ConfigureLogging = log =>
    {
        log.MaskProperties.Add("CardNumber");
        log.MaxSerializedLength = 4096;
    };
    opts.ConfigurePerformance = perf =>
    {
        perf.WarningThresholdMs = 500;
        perf.CriticalThresholdMs = 5000;
    };
});
```

- - -

## Reliable Events (Transactional Outbox)

For at-least-once delivery that survives crashes, enqueue notifications into the outbox **inside**
your business transaction; a background processor publishes them after commit and retries failures:

```csharp
builder.Services.AddQorpeOutbox();   // InMemoryOutboxStore + processor (provide a durable store for prod)

// in a handler, within the transaction:
await _outbox.EnqueueAsync(new OrderCreatedEvent(orderId), ct);
```

Provide a durable `IOutboxStore` (EF Core, SQL, …) in production; the in-memory store is for
development and tests.

- - -

## Native AOT & Trimming

Add the `Qorpe.Mediator.SourceGenerator` package and register via the generated method — handlers
are discovered and dispatch is precomputed at **compile time**, with no assembly scanning and no
runtime code generation:

```csharp
// Generated: registers every handler + precomputes Send/Publish/Stream dispatch
builder.Services.AddQorpeMediatorGenerated();
```

The core `Qorpe.Mediator` assembly is marked `IsAotCompatible` and its dispatch path is verified
trim/AOT-clean by the analyzers. The reflection-based `AddQorpeMediator(...)` scanning path still
works for JIT scenarios; under trimming/AOT use `AddQorpeMediatorGenerated()`.

Features that serialize JSON (caching, idempotency store, outbox) accept an explicit
`SerializerOptions` — set it to options backed by a `JsonSerializerContext` (System.Text.Json source
generation) for AOT, e.g. `opts.SerializerOptions = MyJsonContext.Default.Options;`.

- - -

## Observability (OpenTelemetry)

The mediator emits OpenTelemetry-compatible **traces** and **metrics** using the built-in
`System.Diagnostics` primitives — no dependency on the OpenTelemetry SDK. Wire them into your
collector:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource(MediatorDiagnostics.ActivitySourceName))   // "Qorpe.Mediator"
    .WithMetrics(m => m.AddMeter(MediatorDiagnostics.MeterName));            // "Qorpe.Mediator"
```

- **Spans**: `mediator.send <Request>` and `mediator.publish <Notification>`, tagged with the
  request/notification type and an Ok/Error status (failures record `error.type`).
- **Metrics**: `qorpe.mediator.send.count` / `.duration`, `qorpe.mediator.publish.count` / `.duration`.

When nothing is listening (the default), instrumentation is effectively free — the hot path only
performs a cheap `HasListeners()`/`Enabled` check and skips all activity and measurement work.

- - -

## MediatR vs Qorpe.Mediator

| Feature | MediatR v12 | Qorpe.Mediator |
|---------|-------------|----------------|
| License | Commercial (2025+) | **MIT** |
| Publish Performance | Baseline | **Up to 66% faster** |
| Publish Memory | Baseline | **Up to 4.7x less** |
| Result Pattern | No (exceptions) | **Built-in Result\<T\>** |
| CQRS Types | IRequest only | **ICommand, IQuery, IRequest** |
| Domain Events | INotification | **IDomainEvent + INotification** |
| Pre/Post Processors | Defined, wired | **Defined, wired** |
| Behavior Ordering | Registration order | **Explicit IBehaviorOrder** |
| Polymorphic Notifications | No | **Opt-in** |
| Startup Validation | No | **ValidateOnStartup** |
| Built-in Behaviors | 0 | **11** |
| HTTP Endpoints | No | **[HttpEndpoint] attribute** |
| Cache Invalidation | No | **[InvalidatesCache] attribute** |
| ValueTask | No (Task) | **Yes (ValueTask)** |
| Cancellation Diagnostics | No | **Pipeline stage tracking** |
| Stream Pipeline Behaviors | No | **IStreamPipelineBehavior** |
| Sensitive Data | No | **[SensitiveData] auto-mask** |
| Telemetry | Yes | **Built-in OpenTelemetry** (ActivitySource + Meter) |

- - -

## Test Coverage

| Layer | Tests | What It Covers |
|-------|-------|----------------|
| **Unit** | 256 | Result, Error, Guard, Mediator, all behaviors, notifications, validation, pre/post processors, serialization |
| **Integration** | 26 | Full pipeline E2E, HTTP endpoints, cross-behavior, DI registration, transactions |
| **Load** | 18 | 50K concurrent, 500K sequential, memory stability, streaming, latency percentiles |
| **Total** | **300** | Production-grade coverage |

- - -

## Packages

| Package | Description |
|---------|-------------|
| `Qorpe.Mediator` | Core — CQRS abstractions, Result pattern, Mediator implementation |
| `Qorpe.Mediator.Behaviors` | 11 built-in pipeline behaviors |
| `Qorpe.Mediator.FluentValidation` | FluentValidation integration — auto-discovery, multi-validator |
| `Qorpe.Mediator.AspNetCore` | HTTP endpoint mapping — [HttpEndpoint], Result-to-HTTP, OpenAPI |
| `Qorpe.Mediator.Contracts` | Shared contracts for multi-project solutions |
| `Qorpe.Mediator.Analyzers` | Roslyn analyzers — catch behavior-attribute misuse at compile time (QM1001–QM1004) |
| `Qorpe.Mediator.SourceGenerator` | Compile-time, AOT-safe handler registration & dispatch (`AddQorpeMediatorGenerated`) |
| `Qorpe.Mediator.EntityFrameworkCore` | Durable EF Core stores for the outbox and audit (`AddQorpeEfCoreOutboxStore`/`AddQorpeEfCoreAuditStore`) |

- - -

## Sample Project

See [`tests/Qorpe.Mediator.Sample.ECommerce/`](tests/Qorpe.Mediator.Sample.ECommerce/) for a complete e-commerce example with:
- Order aggregate root with domain events
- Commands with transactions, audit, authorization, idempotency, and retry
- Queries with caching and streaming
- FluentValidation validators
- Attribute-based HTTP endpoint mapping
- Full behavior pipeline configuration

- - -

## Migration from MediatR

See [docs/MIGRATION_GUIDE.md](docs/MIGRATION_GUIDE.md) for a step-by-step migration guide.

- - -

## Contributing

Contributions are welcome! Please open an issue or submit a pull request.

- - -

## License

[MIT License](LICENSE)

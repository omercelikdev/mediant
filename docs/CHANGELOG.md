# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2026-06-30

First stable release. This release hardens correctness and concurrency across the whole pipeline.

### Added
- **EF Core durable stores** (`Mediant.EntityFrameworkCore` package) — `EfOutboxStore<TContext>` and `EfAuditStore<TContext>` persist outbox messages and audit entries in your DbContext. Map the entities via `ModelBuilder.ConfigureQorpeOutbox()`/`ConfigureQorpeAudit()` and register with `AddQorpeEfCoreOutboxStore<TContext>()`/`AddQorpeEfCoreAuditStore<TContext>()`. The outbox `AddAsync` only tracks the message so it commits atomically with your business data.
- Behaviors that serialize JSON (caching, idempotency store, outbox) now accept an explicit `SerializerOptions`, so a `JsonSerializerContext` (System.Text.Json source generation) can be supplied for trimming/Native AOT.
- A CI job publishes the `IsAotCompatible` sample as a **native binary** (`dotnet publish -p:PublishAot=true`) and runs it, validating the AOT path end to end.
- **Transactional outbox** for reliable, at-least-once notification dispatch. Enqueue events via `IOutbox` inside the business transaction; a background `OutboxProcessor` rehydrates and publishes them after commit, retrying failures. Ships `IOutboxStore`/`OutboxMessage` abstractions + an `InMemoryOutboxStore` (provide a durable store in production); register with `services.AddQorpeOutbox()`.
- **Native AOT / trimming support** via a source generator (`Mediant.SourceGenerator`). `AddQorpeMediatorGenerated()` registers all handlers and precomputes Send/Publish/Stream dispatch at compile time — no assembly scanning, no runtime code generation. The core assembly is `IsAotCompatible` and its dispatch path is verified trim/AOT-clean by the analyzers; the reflection-based `AddQorpeMediator(...)` scanning path remains for JIT scenarios. Validated by an `IsAotCompatible` sample that builds clean and runs Send/Publish/Stream end to end.
- **Roslyn analyzers** (`Mediant.Analyzers` package) — catch behavior-attribute misuse at compile time instead of silently at runtime: `[Cacheable]` on a non-query (QM1001), `[Transactional]` on a non-command (QM1002), `[Idempotent]` on a non-command (QM1003), and `[HttpEndpoint]` on a non-request (QM1004).
- **Frozen public API** — the public surface of every shipped package is captured in approved baselines and verified by tests, so accidental breaking changes are caught before release.
- **Production idempotency store** — `DistributedCacheIdempotencyStore` backed by `IDistributedCache`, so `[Idempotent]` works out of the box with any distributed-cache provider (Redis, SQL Server, …). Register via `services.AddQorpeDistributedCacheIdempotencyStore()`. `Result`/`Result<T>` responses round-trip correctly.
- **Open-generic pipeline behaviors** — register a behavior that applies to every request via `cfg.AddOpenBehavior(typeof(MyBehavior<,>))` (and `AddOpenStreamBehavior` for streams). Multiple are supported and run in `IBehaviorOrder` order. Auto-scanning intentionally does NOT register open generics, so generic helper types aren't swept up as global handlers.
- **OpenTelemetry instrumentation** — the mediator emits OTel-compatible traces (`mediator.send`/`mediator.publish` spans with request/notification tags and Ok/Error status) and metrics (`qorpe.mediator.send.count`/`.duration`, `qorpe.mediator.publish.count`/`.duration`) via built-in `System.Diagnostics` primitives. Zero overhead when no listener is attached. Wire via `AddSource`/`AddMeter` with `MediatorDiagnostics.ActivitySourceName`/`MeterName`.

### Fixed — Critical
- **Notification fanout via assembly scanning** — `AddQorpeMediator(cfg => cfg.RegisterServicesFromAssembly(...))` registered only **one** handler per notification type (the second distinct handler was silently dropped by `TryAdd`). Multi-instance services (notification handlers, behaviors, pre/post processors) are now registered with `TryAddEnumerable`, so all handlers run.
- **`Result<T>` JSON round-trip** — `Result<T>` could not be deserialized (no usable constructor) and serializing a *failed* result threw. A custom `JsonConverter` fixes both, so distributed caching of `Result<T>` responses now actually serves from cache.

### Fixed — Concurrency & correctness
- **BoundedLockPool eviction race** — per-key locks are now reference-counted and cannot be evicted while held/awaited, preserving cache-stampede prevention and idempotency serialization.
- **Idempotency** — a handler failure no longer deletes a previously stored successful result; added client-supplied key support via `[Idempotent(KeyProperty = ...)]`; cache keys use pinned `JsonSerializerOptions`.
- **Transaction post-commit queue** — the queue is cleared on rollback so post-commit side effects never fire for rolled-back work.
- **Retry backoff** — exponential shift no longer overflows; jitter and delays are bounded and validated.
- **Publish dispatch** — generic and non-generic `Publish` overloads now both dispatch on the runtime type.
- **Send delegate cache** — keyed by `(requestType, responseType)` to avoid covariant type confusion.
- **Behavior ordering** — stable sort preserves registration order for equal `Order` values.

### Fixed — ASP.NET Core
- General domain failures (`ErrorType.Failure`) now map to **422** instead of 500.
- `201 Created` no longer emits a malformed empty `Location` header.
- Route-parameter binding failures on non-GET verbs now return **400** instead of silently using defaults.
- Validation failures use `ValidationProblem`; endpoint names use full type names to avoid collisions.
- Per-request reflection in response mapping replaced with cached typed delegates; AOT/trimming stance declared.

### Removed
- Dead second pipeline implementation (`RequestPipeline`, `HandlerResolver`) and unused options (`SetPipelineOrder`, audit `BatchSize`/`FlushIntervalSeconds`, which were never wired).

## [1.0.0-preview.8] - 2026-03-29

### Performance
- **Attribute reflection caching** — All behavior attribute lookups (`[Retryable]`, `[Cacheable]`, `[Authorize]`, etc.) now use `static readonly` fields, eliminating per-request `GetCustomAttributes` reflection (#64)
- **Type check caching** — `IsCommand()`/`IsQuery()` interface checks in CachingBehavior, IdempotencyBehavior, TransactionBehavior cached as `static readonly bool` (#66)
- **Cache key optimization** — CachingBehavior now uses SHA256 hash for cache keys instead of raw JSON strings, preventing excessively long keys (#70)
- **Auth result caching** — AuthorizationBehavior `Result<T>.Failure()` method lookup cached as compiled delegate instead of per-invocation reflection (#72)

### Fixed
- **RetryBehavior TaskCanceledException** — The pattern `TaskCanceledException and not OperationCanceledException` was always unreachable due to inheritance. HttpClient timeout exceptions are now correctly retried using token comparison (#68)
- **Stream behavior ordering** — StreamHandlerWrapper now sorts `IStreamPipelineBehavior` by `IBehaviorOrder.Order`, matching RequestHandlerWrapper behavior (#74)

### Added
- **Post-commit task queue** — `IPostCommitTaskQueue` abstraction for fire-and-forget tasks after transaction commit (emails, notifications). TransactionBehavior executes queued tasks after successful commit. Fully backward compatible (#89)
- **IAuditableRequest metadata** — Rich audit metadata interface with ActionName, EntityType, EntityId, and custom AuditMetadata. Implementing the interface triggers audit logging automatically (#91)

### Testing
- **Behavior ordering tests** — 4 tests verifying IBehaviorOrder-based execution order (ascending, default position, stable sort) (#76)
- **Stream ordering tests** — 2 tests verifying stream behavior ordering (#78)
- **Post-commit queue tests** — 10 tests (queue execution, ordering, failure isolation, concurrent enqueue, commit/rollback integration) (#89)
- **Audit metadata tests** — 4 tests (metadata population, audit without attribute, backward compat, failure capture) (#91)
- **Total tests: 256** (217 unit + 21 integration + 18 load)

### CI/CD
- **CodeQL security scanning** — Added GitHub CodeQL workflow for C# SAST analysis (#81)

## [1.0.0] - 2025-03-28

### Core Architecture
- **Mediant** — Zero-dependency CQRS mediator core
  - CQRS abstractions: `ICommand<T>`, `IQuery<T>`, `IRequest<T>`, `INotification`, `IDomainEvent`
  - Handler interfaces: `IRequestHandler`, `ICommandHandler`, `IQueryHandler`, `INotificationHandler`
  - Pipeline: `IPipelineBehavior<T,R>`, `IRequestPreProcessor<T>`, `IRequestPostProcessor<T,R>`
  - Streaming: `IStreamRequest<T>`, `IStreamRequestHandler<T,R>`
  - Result pattern: `Result`, `Result<T>`, `Error`, `ErrorType`, `ValidationError`
  - Functional extensions: `Map`, `Bind`, `Match` (sync and async)
  - Guard class for argument validation
  - Custom exceptions: `HandlerNotFoundException`, `MultipleHandlersException`, `PipelineException`

### Performance Engine
- Typed `RequestHandlerWrapper<TRequest, TResponse>` with compiled Expression Tree delegates
- Zero reflection on hot path after first call per request type
- Direct notification handler invocation — no wrapper object allocation
- `ForeachNotificationPublisher` (sequential) and `ParallelNotificationPublisher` (concurrent)
- DI registration via `AddQorpeMediator` with assembly scanning

### Pipeline Behaviors (Mediant.Behaviors)
- **AuditBehavior** — async batching, store abstraction, sensitive data masking, console fallback
- **LoggingBehavior** — structured logging, auto-mask by name + attribute, truncation
- **UnhandledExceptionBehavior** — catch-all safety net
- **AuthorizationBehavior** — role + policy checking, `Result.Unauthorized`/`Result.Forbidden`
- **TransactionBehavior** — command-only, rollback, nested savepoints via `IUnitOfWork`
- **IdempotencyBehavior** — SHA256 key, concurrent-safe, window-based expiry
- **PerformanceBehavior** — Stopwatch-based, configurable warning/critical thresholds
- **RetryBehavior** — exponential backoff with jitter, exception type filtering
- **CachingBehavior** — query-only, stampede prevention via per-key `SemaphoreSlim`
- Attributes: `[Auditable]`, `[Cacheable]`, `[Retryable]`, `[Authorize]`, `[Idempotent]`, `[Transactional]`

### FluentValidation (Mediant.FluentValidation)
- `ValidationBehavior` — multi-validator, runs ALL, returns `Result.Failure` (no exceptions)
- Auto-discovery from assemblies

### ASP.NET Core (Mediant.AspNetCore)
- `[HttpEndpoint]` attribute for declarative Minimal API routing
- `EndpointMapper` — auto-discovers and generates endpoints
- `ResultToActionResultMapper` — RFC 7807 ProblemDetails
- Route grouping, OpenAPI metadata

### Contracts (Mediant.Contracts)
- Re-exports core abstractions for multi-project solutions

### Sample Project
- E-Commerce: Order aggregate, domain events, commands, queries, validators
- Full behavior pipeline, attribute-based HTTP endpoints

### Benchmarks (vs MediatR v12)
- Send with behaviors: **19-28% faster**
- Publish: **38-65% faster**, **3.3-4.7x less memory**
- 18 benchmark scenarios, all honest BenchmarkDotNet results

### Tests (149 total)
- **123 unit tests** — Result, Error, Guard, Mediator, all 9 behaviors, notifications, validation, behavior execution verification
- **9 integration tests** — full pipeline E2E, cross-behavior, DI, audit trail
- **17 load tests** — 50K concurrent, 500K sequential, re-entrancy, streaming, latency percentiles (p50/p95/p99), thread pool, scoped DI, cancellation, graceful degradation

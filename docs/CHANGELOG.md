# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed
- **EF Core guide's custom unit-of-work sample now resolves** (#140) — the `EF_CORE_GUIDE.md` template injected the abstract `DbContext`, which `AddDbContext<AppDbContext>()` never registers, so copy-pasting the recommended registration threw `Unable to resolve service for type 'DbContext'` at runtime. The sample now injects the concrete context (with a note explaining why), the DI section distinguishes the built-in `AddMediantEfCoreUnitOfWork<TContext>()` path from the custom one, and a test mirrors the documented sample verbatim to guard against future drift.
- **EF Core store registrations now win regardless of call order** (#139) — `AddMediantEfCoreOutboxStore<TContext>()` and `AddMediantEfCoreAuditStore<TContext>()` previously used `TryAdd`, so calling them *after* `AddMediantOutbox()`/`AddMediantAuditing()` was a silent no-op: audit entries vanished into the `NullAuditStore` default and the outbox quietly stayed in-memory (messages lost on restart) with no error. Both extensions now `Replace` the existing `IOutboxStore`/`IAuditStore` registration, so the explicit durable store wins in either order; standard last-wins DI semantics still apply to registrations made after them (a custom store or `AddMediantAuditBuffering` registered later still wins). **Behavior note:** if you registered a custom store *before* calling the EF extension and relied on `TryAdd` keeping the custom one, remove the now-redundant EF call or move your registration after it.
- **Rolled-back entities can no longer be re-flushed by a failure-audit write on a shared context** (#138) — when a `[Transactional]` + audited command failed and business data shared one scoped `DbContext` with the unbuffered `EfAuditStore`, the failure-audit `SaveChangesAsync` re-flushed the rolled-back handler entities outside any transaction (rollback does not detach tracked entities), leaking half-done data. The built-in `EfCoreUnitOfWork<TContext>` clears the change tracker on rollback; the `EF_CORE_GUIDE.md` custom unit-of-work template now marks `ChangeTracker.Clear()` as required in `RollbackAsync`, and `EfAuditStore` documents the shared-context caveat. Covered by end-to-end regression tests (failure audit persists, business rows do not, and a later command in the same scope stays uncontaminated).

### Added
- **Built-in EF Core unit of work** (#137) — `AddMediantEfCoreUnitOfWork<TContext>()` registers `EfCoreUnitOfWork<TContext>` as the `IUnitOfWork`, so `[Transactional]` works against your DbContext out of the box — no hand-written unit of work needed for the direct-DbContext (no repository) setup. Because it resolves the same scoped context as your handlers and `EfOutboxStore<TContext>`, business writes and outbox messages commit atomically. `BeginTransactionAsync` is a no-op when a transaction is already open (execution-strategy retries), rollback **clears the change tracker** so rolled-back entities can never be re-flushed by a later `SaveChanges` on the same context, and on non-relational providers (EF InMemory in tests) transaction calls degrade to no-ops while `SaveChangesAsync` still flushes.

## [1.2.0] - 2026-07-05

### Added
- **HybridCache-backed query caching** (#130) — opt-in `AddMediantHybridCaching()` registers `HybridCachingBehavior<,>` + `HybridCacheInvalidator` on Microsoft's `HybridCache` (in-process **L1** + distributed **L2**, built-in stampede protection), an alternative to the `IDistributedCache`-only path (`AddMediantCaching`). `[Cacheable(CacheKeyPrefix = "p")]` tags entries with `p`, so `[InvalidatesCache("p")]` maps to an exact, **O(1)** `RemoveByTagAsync` — no key registry or prefix scan; `InvalidateAllAsync` clears a reserved all-entries tag. `Result`/`Result<T>` round-trip via their `[JsonConverter]` attributes. Use it *instead of* `AddMediantCaching` (also call `services.AddHybridCache()`). Adds a `Microsoft.Extensions.Caching.Hybrid` dependency to `Mediant.Behaviors`.
- **Default cache invalidator** (#131) — `AddMediantCaching` now ships a working `ICacheInvalidator` (`DistributedCacheInvalidator`) so `[InvalidatesCache("prefix")]` actually evicts cached queries instead of silently no-op'ing (previously no implementation was registered → stale data with no error). Because `IDistributedCache` cannot enumerate keys, `CachingBehavior` records each written key under its `CacheKeyPrefix` in an `ICacheKeyRegistry` (default `DistributedCacheKeyRegistry`, same store) and the invalidator removes the registered keys; `InvalidateAllAsync` walks all known prefixes. Cross-process registry updates are best-effort — a rare orphaned key expires via its own TTL, bounding worst-case staleness to the cache duration. As a safety net, `CacheInvalidationBehavior` now logs a one-time warning when `[InvalidatesCache]` is present but no invalidator is registered.

### Fixed
- **GET endpoint binding for positional records** (#129) — a GET `[HttpEndpoint]` query declared as a positional record (`record GetOrdersQuery(string? Cursor = null, int Size = 50) : IQuery<...>`) previously failed at runtime with `MissingMethodException` (500) because binding required a parameterless constructor. `EndpointMapper` now binds positional records via their primary constructor — matching parameters to query/route values by name (case-insensitive), falling back to declared defaults for missing optionals, binding null for nullable/reference parameters, and returning a clear **400** (not 500) when a required value-type parameter is absent or invalid. Extra init/settable properties beyond the constructor are still bound. Init-property records and classes are unchanged.

## [1.1.0] - 2026-07-04

### Added
- **Buffered audit persistence** (#117) — opt-in `AddMediantAuditBuffering<TStore>()` decorates the registered `IAuditStore` with a process-wide bounded buffer: writes enqueue (backpressure when full — entries are never dropped by the buffer) and a background flusher batches them into the durable store (`AuditBufferOptions.BatchSize`, default 100, every `FlushInterval`, default 5s), cutting store round-trips for audit-heavy workloads by ~BatchSize×. Graceful shutdown flushes the buffer; a failed durable write re-enqueues the batch instead of losing it; `BufferedAuditStore.FlushAsync()` is the synchronous escape hatch for tests, and queries flush first (read-your-writes). **Durability trade-off (deliberate opt-in):** entries buffered but not yet flushed are lost on a hard process crash — without this call, audit writes stay synchronous as before.
- **Outbox multi-instance coordination** (#116) — `EfOutboxStore` now implements the new `IClaimingOutboxStore`: each processor replica atomically claims its batch under a lease (`ClaimedBy`/`ClaimedUntil` on `OutboxMessage`), so horizontally scaled deployments (2+ replicas on the same store) dispatch each message once under normal operation. A crashed owner's messages become reclaimable after `OutboxProcessorOptions.LeaseDuration` (default 60s; instance identity via `OwnerId`). The claim also filters `Attempts >= MaxAttempts` at the store, keeping abandoned messages out of fetched batches. `OutboxProcessor` uses claiming automatically when the registered store supports it; plain stores (including `InMemoryOutboxStore`) keep single-instance polling semantics. **Migration note:** the outbox entity gains two columns — add an EF migration (or equivalent DDL) for `ClaimedBy` (string, max 256, null) and `ClaimedUntil` (datetimeoffset, null) plus the new `(ProcessedOn, ClaimedUntil)` index when upgrading.
- **Idempotency payload-fingerprint detection** (#115) — `[Idempotent(KeyProperty = ..., DetectPayloadMismatch = true)]` stores a SHA-256 fingerprint of the full request payload with the response; reusing the same key with a *different* payload now throws `IdempotencyKeyReuseException` (client error, 422-style) instead of silently replaying the stored response. Off by default; identical retries replay as before. The behavior now persists entries in the `IdempotencyEntry<TResponse>` envelope (one store round-trip instead of two); entries written by earlier versions re-execute once after upgrade and are then re-stored in the new format.
- **Idempotency coordinator for non-mediator entry points** (#114) — `IIdempotentOperationCoordinator` (default implementation `DefaultIdempotentOperationCoordinator`, registered via `AddMediantIdempotencyCoordinator()` or automatically with `AddMediantDistributedCacheIdempotencyStore()`) exposes the begin/complete idempotency lifecycle — per-key locking, stored-response replay, optional payload-fingerprint verification (`FingerprintMismatch`), and optional lock-wait timeout (`InFlight`, for 409-style HTTP semantics) — so an HTTP `Idempotency-Key` middleware can share the same store and semantics as the `[Idempotent]` behavior. The serialization guarantee is process-local; responses are persisted in an `IdempotencyEntry<TResponse>` envelope.
- **Configurable performance hard ceiling** (#118) — the fixed 30-second ceiling in `PerformanceBehavior` (always logged as Critical) is now configurable: globally via `PerformanceBehaviorOptions.HardCeilingMs` (default 30000, `<= 0` disables) and per request type via `[PerformanceThreshold(CeilingMs = ...)]` (`0` = use global, negative = disabled for that request — long-running batch commands by design).

## [1.0.1] - 2026-07-03

### Fixed
- **Outbox `MaxAttempts` is now enforced** (#120) — `OutboxProcessor` no longer redispatches messages that have already failed `MaxAttempts` times. Previously the limit was documented but never checked, so a poison message (e.g. an unresolvable notification type) was retried on every poll forever. Abandonment is logged once per message. Messages that reached the limit before this fix stop being retried after upgrade.

## [1.0.0] - 2026-07-03

First stable release. This release hardens correctness and concurrency across the whole pipeline.

### Added
- **EF Core durable stores** (`Mediant.EntityFrameworkCore` package) — `EfOutboxStore<TContext>` and `EfAuditStore<TContext>` persist outbox messages and audit entries in your DbContext. Map the entities via `ModelBuilder.ConfigureMediantOutbox()`/`ConfigureMediantAudit()` and register with `AddMediantEfCoreOutboxStore<TContext>()`/`AddMediantEfCoreAuditStore<TContext>()`. The outbox `AddAsync` only tracks the message so it commits atomically with your business data.
- Behaviors that serialize JSON (caching, idempotency store, outbox) now accept an explicit `SerializerOptions`, so a `JsonSerializerContext` (System.Text.Json source generation) can be supplied for trimming/Native AOT.
- A CI job publishes the `IsAotCompatible` sample as a **native binary** (`dotnet publish -p:PublishAot=true`) and runs it, validating the AOT path end to end.
- **Transactional outbox** for reliable, at-least-once notification dispatch. Enqueue events via `IOutbox` inside the business transaction; a background `OutboxProcessor` rehydrates and publishes them after commit, retrying failures. Ships `IOutboxStore`/`OutboxMessage` abstractions + an `InMemoryOutboxStore` (provide a durable store in production); register with `services.AddMediantOutbox()`.
- **Native AOT / trimming support** via a source generator (`Mediant.SourceGenerator`). `AddMediantGenerated()` registers all handlers and precomputes Send/Publish/Stream dispatch at compile time — no assembly scanning, no runtime code generation. The core assembly is `IsAotCompatible` and its dispatch path is verified trim/AOT-clean by the analyzers; the reflection-based `AddMediant(...)` scanning path remains for JIT scenarios. Validated by an `IsAotCompatible` sample that builds clean and runs Send/Publish/Stream end to end.
- **Roslyn analyzers** (`Mediant.Analyzers` package) — catch behavior-attribute misuse at compile time instead of silently at runtime: `[Cacheable]` on a non-query (QM1001), `[Transactional]` on a non-command (QM1002), `[Idempotent]` on a non-command (QM1003), and `[HttpEndpoint]` on a non-request (QM1004).
- **Frozen public API** — the public surface of every shipped package is captured in approved baselines and verified by tests, so accidental breaking changes are caught before release.
- **Production idempotency store** — `DistributedCacheIdempotencyStore` backed by `IDistributedCache`, so `[Idempotent]` works out of the box with any distributed-cache provider (Redis, SQL Server, …). Register via `services.AddMediantDistributedCacheIdempotencyStore()`. `Result`/`Result<T>` responses round-trip correctly.
- **Open-generic pipeline behaviors** — register a behavior that applies to every request via `cfg.AddOpenBehavior(typeof(MyBehavior<,>))` (and `AddOpenStreamBehavior` for streams). Multiple are supported and run in `IBehaviorOrder` order. Auto-scanning intentionally does NOT register open generics, so generic helper types aren't swept up as global handlers.
- **OpenTelemetry instrumentation** — the mediator emits OTel-compatible traces (`mediator.send`/`mediator.publish` spans with request/notification tags and Ok/Error status) and metrics (`mediant.send.count`/`.duration`, `mediant.publish.count`/`.duration`) via built-in `System.Diagnostics` primitives. Zero overhead when no listener is attached. Wire via `AddSource`/`AddMeter` with `MediatorDiagnostics.ActivitySourceName`/`MeterName`.

### Fixed — Critical
- **Notification fanout via assembly scanning** — `AddMediant(cfg => cfg.RegisterServicesFromAssembly(...))` registered only **one** handler per notification type (the second distinct handler was silently dropped by `TryAdd`). Multi-instance services (notification handlers, behaviors, pre/post processors) are now registered with `TryAddEnumerable`, so all handlers run.
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
- DI registration via `AddMediant` with assembly scanning

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

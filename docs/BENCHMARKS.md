# Benchmarks

> All benchmarks use [BenchmarkDotNet](https://benchmarkdotnet.org/) with `MemoryDiagnoser` on real hardware.
> No synthetic inflation — these are honest, reproducible results.
>
> **What these measure:** framework dispatch overhead with **no-op handlers** (the handler returns
> immediately). This isolates the mediator's own cost. Real handlers that do I/O or CPU work narrow
> the relative gap — the absolute ns/allocation differences stay, but they become a smaller fraction
> of total request time. Numbers below are from a single fresh run; expect a few percent of
> run-to-run variance.

## Environment

| Property | Value |
|----------|-------|
| **Runtime** | .NET 10.0.1 (10.0.125.57005), Arm64 RyuJIT AdvSIMD |
| **OS** | macOS (Apple M5) |
| **BenchmarkDotNet** | v0.14.0 |
| **MediatR Version** | v12.4.1 |
| **Mediant** | v1.0.0 |

## Send (Command/Query Pipeline)

The most critical path — every request goes through `Send()`.
Exponential behavior scaling shows how each library handles increasing pipeline depth.

| Behaviors | Mediant | MediatR v12 | Speed | Memory |
|-----------|---------------|-------------|-------|--------|
| **0** | 24 ns / 64 B | 24 ns / 128 B | ~equal | **Qorpe 2x less** |
| **1** | 59 ns / 288 B | 57 ns / 368 B | ~equal | **Qorpe 22% less** |
| **2** | 75 ns / 424 B | 70 ns / 512 B | ~equal | **Qorpe 17% less** |
| **4** | 102 ns / 696 B | 100 ns / 800 B | ~equal | **Qorpe 13% less** |
| **8** | 150 ns / 1,240 B | 159 ns / 1,376 B | **Qorpe 6% faster** | **Qorpe 10% less** |
| **16** | 262 ns / 2,328 B | 277 ns / 2,528 B | **Qorpe 5% faster** | **Qorpe 8% less** |
| **32** | 478 ns / 4,504 B | 507 ns / 4,832 B | **Qorpe 6% faster** | **Qorpe 7% less** |

> Speed is within noise through 4 behaviors; from 8 behaviors up Qorpe pulls ahead and the gap
> widens with pipeline depth. Memory usage is lower in **every single scenario**.

## Query (Return Value)

| Scenario | Mediant | MediatR v12 | Speed | Memory |
|----------|---------------|-------------|-------|--------|
| **Query returning Result\<int\>** | 28 ns / 104 B | 28 ns / 200 B | ~equal | **Qorpe 1.9x less** |

> Qorpe returns `Result<int>` (richer type with error handling) vs MediatR's raw `int`.

## Publish (Notification Fanout)

This is where Qorpe dominates — direct handler invocation without wrapper object allocation.

| Handlers | Mediant | MediatR v12 | Speed | Memory |
|----------|---------------|-------------|-------|--------|
| **1 handler** | 24 ns / 88 B | 44 ns / 288 B | **46% faster** | **3.3x less** |
| **10 handlers** | 69 ns / 376 B | 184 ns / 1,656 B | **63% faster** | **4.4x less** |
| **50 handlers** | 276 ns / 1,656 B | 792 ns / 7,736 B | **65% faster** | **4.7x less** |
| **100 handlers** | 527 ns / 3,256 B | 1,521 ns / 15,336 B | **65% faster** | **4.7x less** |

> MediatR creates `NotificationHandlerExecutor` wrapper objects + closure delegates per handler per call.
> Qorpe invokes handlers directly — zero wrapper allocation.

## Summary Scorecard

| Category | Benchmarks | Qorpe Wins | Tie | MediatR Wins |
|----------|-----------|------------|-----|--------------|
| Send (8+ behaviors) | 3 | 3 | 0 | 0 |
| Send (0-4 behaviors) | 4 | 0 | 4 | 0 |
| Query | 1 | 0 | 1 | 0 |
| Publish | 4 | 4 | 0 | 0 |
| **Total** | **12** | **7** | **5** | **0** |

**Memory: Qorpe uses less memory in all 12 benchmarks.**
**Publish: 46-65% faster with 3.3-4.7x less memory.**
**Send (8+ behaviors): 5-6% faster, 7-10% less memory — gap widens at scale.**
**Send (0-4 behaviors): equal speed (within noise), 13-22% less memory.**

## Load Test Results

> 18 load tests; 300 total tests across unit, integration, load, and E2E.

### Concurrency and Throughput

| Test | Scale | Result |
|------|-------|--------|
| Concurrent Send | 10,000 simultaneous | No deadlocks, all succeed |
| Concurrent Send + Behaviors | 50,000 simultaneous | No deadlocks, all succeed |
| Concurrent Query | 5,000 simultaneous | All succeed, correct results |
| Scoped DI (per-request) | 10,000 scopes | All succeed independently |
| Mixed Operations | 10,000 (cmd + query + notification) | All complete cleanly |

### Memory and Stability

| Test | Scale | Result |
|------|-------|--------|
| Sequential Memory Leak | 100,000 requests | < 10 MB growth |
| Sequential + Behaviors Memory | 500,000 requests | < 20 MB growth |
| Caching High-Cardinality Keys | 10,000 unique keys | < 20 MB growth |
| Thread Pool Exhaustion | 20,000 operations | Pool not depleted |

### Notification Fanout

| Test | Scale | Result |
|------|-------|--------|
| Sequential Fanout | 1,000 x 3 handlers = 3,000 executions | All succeed |
| Parallel Fanout | 5,000 x 10 handlers = 50,000 executions | All succeed |

### Resilience

| Test | Scale | Result |
|------|-------|--------|
| Exception Under Load | 10,000 (33% failures) | All complete, no leaks |
| Cancellation Mid-Flight | 5,000 + cancel | No hanging tasks |
| Graceful Degradation | 10,000 mixed success/fail/cancel | All complete |
| Re-entrant Send | 1,000 x depth 3 = 4,000 nested calls | No deadlocks |

### Streaming

| Test | Scale | Result |
|------|-------|--------|
| Concurrent Consumers | 100 consumers x 1,000 items = 100,000 items | All correct |

### Latency Percentiles (10-second sustained)

| Percentile | Latency |
|------------|---------|
| **p50** | < 1 ms |
| **p95** | < 5 ms |
| **p99** | < 10 ms |
| **Throughput** | > 10,000 req/sec |

## Running Benchmarks

```bash
# BenchmarkDotNet comparison vs MediatR
cd tests/Mediant.Benchmarks
dotnet run -c Release

# Load tests
dotnet test tests/Mediant.LoadTests
```

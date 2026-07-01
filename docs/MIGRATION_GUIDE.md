# Migration Guide: MediatR to Mediant

## Step 1: Replace Packages

```bash
# Remove MediatR
dotnet remove package MediatR

# Add Mediant
dotnet add package Mediant
dotnet add package Mediant.Behaviors        # Optional: the built-in behaviors
dotnet add package Mediant.FluentValidation  # Optional: FluentValidation integration
dotnet add package Mediant.AspNetCore         # Optional: HTTP endpoint mapping
```

## Step 2: Change Registration

```csharp
// Before (MediatR)
services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));

// After (Mediant)
services.AddMediant(cfg => cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));
```

## Step 3: Update Namespaces

```csharp
// Before
using MediatR;

// After
using Mediant.Abstractions;
using Mediant.Results;
```

## Step 4: Gradual Type Migration (Optional)

Your existing `IRequest<T>` will still work. When ready, migrate to explicit CQRS types:

```csharp
// Before
public record CreateOrder(string Name) : IRequest<OrderDto>;

// After (explicit CQRS)
public record CreateOrder(string Name) : ICommand<Result<OrderDto>>;
```

## Step 5: Update Handlers

```csharp
// Before (MediatR)
public class CreateOrderHandler : IRequestHandler<CreateOrder, OrderDto>
{
    public Task<OrderDto> Handle(CreateOrder request, CancellationToken ct)
    {
        // ...
        return Task.FromResult(dto);
    }
}

// After (Mediant) — ValueTask + Result pattern
public class CreateOrderHandler : ICommandHandler<CreateOrder, Result<OrderDto>>
{
    public ValueTask<Result<OrderDto>> Handle(CreateOrder request, CancellationToken ct)
    {
        // ...
        return new ValueTask<Result<OrderDto>>(Result<OrderDto>.Success(dto));
    }
}
```

## Step 6: Update Pipeline Behaviors

```csharp
// Before (MediatR)
public class LoggingBehavior<TReq, TResp> : IPipelineBehavior<TReq, TResp>
{
    public async Task<TResp> Handle(TReq req, RequestHandlerDelegate<TResp> next, CancellationToken ct)
    {
        // ...
        return await next();
    }
}

// After (Mediant) — ValueTask, same pattern
public class LoggingBehavior<TReq, TResp> : IPipelineBehavior<TReq, TResp>
    where TReq : IRequest<TResp>
{
    public async ValueTask<TResp> Handle(TReq req, RequestHandlerDelegate<TResp> next, CancellationToken ct)
    {
        // ...
        return await next();
    }
}
```

Or just use the the built-in behaviors instead of writing your own.

## Step 7: Add Behaviors (Optional)

```csharp
services.AddMediantValidation(typeof(Program).Assembly);
services.AddMediantAllBehaviors();
```

## Step 8: Add HTTP Endpoints (Optional)

```csharp
// Add attribute to commands/queries
[HttpEndpoint("POST", "/api/orders")]
public record CreateOrder : ICommand<Result<Guid>> { ... }

// In Program.cs
app.MapMediantEndpoints(typeof(Program).Assembly);
```

## API Comparison

| MediatR | Mediant |
|---------|----------------|
| `IRequest<T>` | `IRequest<T>`, `ICommand<T>`, `IQuery<T>` |
| `IRequestHandler<TReq, TResp>` | `IRequestHandler<TReq, TResp>`, `ICommandHandler<T>`, `IQueryHandler<T, R>` |
| `INotification` | `INotification`, `IDomainEvent` |
| `INotificationHandler<T>` | `INotificationHandler<T>` |
| `IStreamRequest<T>` | `IStreamRequest<T>` |
| `IPipelineBehavior<T, R>` | `IPipelineBehavior<T, R>` |
| `Task<T>` | `ValueTask<T>` |
| `AddMediatR(...)` | `AddMediant(...)` |
| N/A | `Result<T>`, `Error`, `ErrorType` |
| N/A | `[HttpEndpoint]`, `[Transactional]`, `[Cacheable]`, etc. |

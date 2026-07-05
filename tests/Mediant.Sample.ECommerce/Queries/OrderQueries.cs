using Mediant.Abstractions;
using Mediant.AspNetCore.Attributes;
using Mediant.Behaviors.Attributes;
using Mediant.Results;
using Mediant.Sample.ECommerce.Domain;

namespace Mediant.Sample.ECommerce.Queries;

[HttpEndpoint("GET", "/api/orders/{Id}", Summary = "Get order by ID", Tags = new[] { "Orders" })]
[Cacheable(60)]
public sealed record GetOrderByIdQuery : IQuery<Result<Order>>
{
    public Guid Id { get; init; }
}

[HttpEndpoint("GET", "/api/users/{UserId}/orders", Summary = "Get orders for a user", Tags = new[] { "Orders" })]
public sealed record GetOrdersForUserQuery : IQuery<Result<List<Order>>>
{
    public string UserId { get; init; } = string.Empty;
}

// Positional record with optional parameters — exercises constructor-based GET binding.
[HttpEndpoint("GET", "/api/orders", Summary = "List orders (paged)", Tags = new[] { "Orders" })]
public sealed record ListOrdersQuery(string? Cursor = null, int Size = 20) : IQuery<Result<OrderPage>>;

public sealed record OrderPage(string Cursor, int Size, int Count);

// Streaming requests use CreateStream() directly, not HTTP endpoints
public sealed record SearchOrdersQuery : IStreamRequest<Order>
{
    public string? Status { get; init; }
    public string? UserId { get; init; }
}

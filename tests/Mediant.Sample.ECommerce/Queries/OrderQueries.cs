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

// Streaming requests use CreateStream() directly, not HTTP endpoints
public sealed record SearchOrdersQuery : IStreamRequest<Order>
{
    public string? Status { get; init; }
    public string? UserId { get; init; }
}

using Mediant.Results;

namespace Mediant.Abstractions;

/// <summary>
/// Marker interface for a query with a response.
/// Queries represent operations that read state without side effects.
/// </summary>
/// <typeparam name="TResponse">The response type.</typeparam>
public interface IQuery<out TResponse> : IRequest<TResponse>, IBaseRequest
{
}

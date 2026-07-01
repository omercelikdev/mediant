using Mediant.Results;

namespace Mediant.Abstractions;

/// <summary>
/// Marker interface for a request with no response value.
/// </summary>
public interface IRequest : IRequest<Result>
{
}

/// <summary>
/// Marker interface for a request with a response.
/// </summary>
/// <typeparam name="TResponse">The response type.</typeparam>
public interface IRequest<out TResponse>
{
}

/// <summary>
/// Marker interface for a base request. Used internally for pipeline resolution.
/// </summary>
public interface IBaseRequest
{
}

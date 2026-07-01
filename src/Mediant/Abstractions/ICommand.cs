using Mediant.Results;

namespace Mediant.Abstractions;

/// <summary>
/// Marker interface for a command with no response value.
/// Commands represent operations that change state.
/// </summary>
public interface ICommand : ICommand<Result>
{
}

/// <summary>
/// Marker interface for a command with a response.
/// Commands represent operations that change state.
/// </summary>
/// <typeparam name="TResponse">The response type.</typeparam>
public interface ICommand<out TResponse> : IRequest<TResponse>, IBaseRequest
{
}

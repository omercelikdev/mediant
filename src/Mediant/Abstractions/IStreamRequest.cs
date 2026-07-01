namespace Mediant.Abstractions;

/// <summary>
/// Marker interface for a streaming request that returns an <see cref="IAsyncEnumerable{TResponse}"/>.
/// </summary>
/// <typeparam name="TResponse">The type of each item in the stream.</typeparam>
public interface IStreamRequest<out TResponse>
{
}

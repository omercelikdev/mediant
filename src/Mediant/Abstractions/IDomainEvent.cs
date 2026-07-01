namespace Mediant.Abstractions;

/// <summary>
/// Marker interface for a domain event. Domain events are a special type of notification
/// that represent something significant that happened in the domain.
/// </summary>
public interface IDomainEvent : INotification
{
    /// <summary>
    /// Gets the date and time when the domain event occurred.
    /// </summary>
    DateTimeOffset OccurredOn { get; }
}

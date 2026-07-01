namespace Mediant.Results;

/// <summary>
/// Represents a validation error associated with a specific property.
/// </summary>
public sealed class ValidationError : Error
{
    /// <summary>
    /// Gets the property name that caused the validation error.
    /// </summary>
    public string PropertyName { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="ValidationError"/>.
    /// </summary>
    /// <param name="propertyName">The property name that caused the error.</param>
    /// <param name="description">The error description.</param>
    public ValidationError(string propertyName, string description)
        : base($"Validation.{propertyName}", description, ErrorType.Validation)
    {
        PropertyName = propertyName ?? string.Empty;
    }

    /// <summary>
    /// Initializes a new instance of <see cref="ValidationError"/>.
    /// </summary>
    /// <param name="propertyName">The property name that caused the error.</param>
    /// <param name="code">The error code.</param>
    /// <param name="description">The error description.</param>
    public ValidationError(string propertyName, string code, string description)
        : base(code, description, ErrorType.Validation)
    {
        PropertyName = propertyName ?? string.Empty;
    }
}

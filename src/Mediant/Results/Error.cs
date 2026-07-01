namespace Mediant.Results;

/// <summary>
/// Represents an error with a code, description, and type.
/// </summary>
public class Error : IEquatable<Error>
{
    /// <summary>
    /// Represents no error.
    /// </summary>
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.None);

    /// <summary>
    /// Gets the error code.
    /// </summary>
    public string Code { get; }

    /// <summary>
    /// Gets the error description.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Gets the error type.
    /// </summary>
    public ErrorType Type { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="Error"/>.
    /// </summary>
    /// <param name="code">The error code.</param>
    /// <param name="description">The error description.</param>
    /// <param name="type">The error type.</param>
    public Error(string code, string description, ErrorType type = ErrorType.Failure)
    {
        Code = code ?? string.Empty;
        Description = description ?? string.Empty;
        Type = type;
    }

    /// <summary>
    /// Creates a failure error.
    /// </summary>
    public static Error Failure(string code, string description) =>
        new(code, description, ErrorType.Failure);

    /// <summary>
    /// Creates a validation error.
    /// </summary>
    public static Error Validation(string code, string description) =>
        new(code, description, ErrorType.Validation);

    /// <summary>
    /// Creates a not found error.
    /// </summary>
    public static Error NotFound(string code, string description) =>
        new(code, description, ErrorType.NotFound);

    /// <summary>
    /// Creates a conflict error.
    /// </summary>
    public static Error Conflict(string code, string description) =>
        new(code, description, ErrorType.Conflict);

    /// <summary>
    /// Creates an unauthorized error.
    /// </summary>
    public static Error Unauthorized(string code, string description) =>
        new(code, description, ErrorType.Unauthorized);

    /// <summary>
    /// Creates a forbidden error.
    /// </summary>
    public static Error Forbidden(string code, string description) =>
        new(code, description, ErrorType.Forbidden);

    /// <summary>
    /// Creates an internal error.
    /// </summary>
    public static Error Internal(string code, string description) =>
        new(code, description, ErrorType.Internal);

    /// <summary>
    /// Creates a service unavailable error.
    /// </summary>
    public static Error Unavailable(string code, string description) =>
        new(code, description, ErrorType.Unavailable);

    /// <inheritdoc />
    public bool Equals(Error? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return string.Equals(Code, other.Code, StringComparison.Ordinal) &&
               string.Equals(Description, other.Description, StringComparison.Ordinal) &&
               Type == other.Type;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as Error);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Code, Description, Type);

    /// <inheritdoc />
    public override string ToString() => Code.Length > 0 ? $"{Code}: {Description}" : Description;

    /// <summary>
    /// Equality operator.
    /// </summary>
    public static bool operator ==(Error? left, Error? right) =>
        left is null ? right is null : left.Equals(right);

    /// <summary>
    /// Inequality operator.
    /// </summary>
    public static bool operator !=(Error? left, Error? right) => !(left == right);
}

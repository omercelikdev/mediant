namespace Mediant.Results;

/// <summary>
/// Defines the types of errors that can occur.
/// </summary>
public enum ErrorType
{
    /// <summary>
    /// No error. Represents a successful operation.
    /// </summary>
    None = 0,

    /// <summary>
    /// A general failure error.
    /// </summary>
    Failure = 1,

    /// <summary>
    /// A validation error.
    /// </summary>
    Validation = 2,

    /// <summary>
    /// A not found error.
    /// </summary>
    NotFound = 3,

    /// <summary>
    /// A conflict error (e.g., duplicate resource).
    /// </summary>
    Conflict = 4,

    /// <summary>
    /// An unauthorized error (authentication required).
    /// </summary>
    Unauthorized = 5,

    /// <summary>
    /// A forbidden error (insufficient permissions).
    /// </summary>
    Forbidden = 6,

    /// <summary>
    /// An internal/unexpected error.
    /// </summary>
    Internal = 7,

    /// <summary>
    /// A service unavailable error.
    /// </summary>
    Unavailable = 8
}

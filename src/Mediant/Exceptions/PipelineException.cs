namespace Mediant.Exceptions;

/// <summary>
/// Thrown when an error occurs during pipeline execution.
/// </summary>
public sealed class PipelineException : Exception
{
    /// <summary>
    /// Gets the request type being processed when the error occurred.
    /// </summary>
    public Type? RequestType { get; }

    /// <summary>
    /// Gets the behavior type that caused the error, if applicable.
    /// </summary>
    public Type? BehaviorType { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="PipelineException"/>.
    /// </summary>
    /// <param name="message">The error message.</param>
    public PipelineException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="PipelineException"/>.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public PipelineException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="PipelineException"/>.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="requestType">The request type being processed.</param>
    /// <param name="behaviorType">The behavior type that caused the error.</param>
    /// <param name="innerException">The inner exception.</param>
    public PipelineException(string message, Type requestType, Type? behaviorType, Exception innerException)
        : base(message, innerException)
    {
        RequestType = requestType;
        BehaviorType = behaviorType;
    }
}

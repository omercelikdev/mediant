using System.Text.Json.Serialization;

namespace Qorpe.Mediator.Results;

/// <summary>
/// Represents the result of an operation that may succeed or fail.
/// </summary>
[JsonConverter(typeof(ResultJsonConverterFactory))]
public class Result
{
    private static readonly Result SuccessResult = new(true, Error.None, Array.Empty<Error>());

    /// <summary>
    /// Gets whether the operation was successful.
    /// </summary>
    [JsonInclude]
    public bool IsSuccess { get; }

    /// <summary>
    /// Gets whether the operation failed.
    /// </summary>
    [JsonIgnore]
    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// Gets the primary error. Returns <see cref="Error.None"/> on success.
    /// </summary>
    [JsonInclude]
    public Error Error { get; }

    /// <summary>
    /// Gets all errors. Returns empty list on success.
    /// </summary>
    [JsonInclude]
    public IReadOnlyList<Error> Errors { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="Result"/>.
    /// </summary>
    protected Result(bool isSuccess, Error error, IReadOnlyList<Error> errors)
    {
        IsSuccess = isSuccess;
        Error = error;
        Errors = errors;
    }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static Result Success() => SuccessResult;

    /// <summary>
    /// Creates a failed result with a single error.
    /// </summary>
    /// <param name="error">The error.</param>
    /// <returns>A failed result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="error"/> is null.</exception>
    public static Result Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result(false, error, new[] { error });
    }

    /// <summary>
    /// Creates a failed result with multiple errors.
    /// </summary>
    /// <param name="errors">The errors.</param>
    /// <returns>A failed result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="errors"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="errors"/> is empty.</exception>
    public static Result Failure(IReadOnlyList<Error> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (errors.Count == 0)
        {
            throw new ArgumentException("At least one error is required for a failure result.", nameof(errors));
        }

        return new Result(false, errors[0], errors);
    }

    /// <summary>
    /// Creates a successful result with a value.
    /// </summary>
    /// <typeparam name="TValue">The value type.</typeparam>
    /// <param name="value">The value.</param>
    /// <returns>A successful result with the value.</returns>
    public static Result<TValue> Success<TValue>(TValue value) => Result<TValue>.Success(value);

    /// <summary>
    /// Creates a failed result with a value type.
    /// </summary>
    /// <typeparam name="TValue">The value type.</typeparam>
    /// <param name="error">The error.</param>
    /// <returns>A failed result.</returns>
    public static Result<TValue> Failure<TValue>(Error error) => Result<TValue>.Failure(error);

    /// <summary>
    /// Creates a failed result with validation errors.
    /// </summary>
    /// <param name="validationErrors">The validation errors.</param>
    /// <returns>A failed result.</returns>
    public static Result ValidationFailure(params ValidationError[] validationErrors)
    {
        ArgumentNullException.ThrowIfNull(validationErrors);
        if (validationErrors.Length == 0)
        {
            throw new ArgumentException("At least one validation error is required.", nameof(validationErrors));
        }

        var errors = new Error[validationErrors.Length];
        for (int i = 0; i < validationErrors.Length; i++)
        {
            errors[i] = validationErrors[i];
        }

        return new Result(false, errors[0], errors);
    }

    /// <summary>
    /// Creates a failed result with validation errors.
    /// </summary>
    /// <typeparam name="TValue">The value type.</typeparam>
    /// <param name="validationErrors">The validation errors.</param>
    /// <returns>A failed result.</returns>
    public static Result<TValue> ValidationFailure<TValue>(params ValidationError[] validationErrors)
    {
        ArgumentNullException.ThrowIfNull(validationErrors);
        if (validationErrors.Length == 0)
        {
            throw new ArgumentException("At least one validation error is required.", nameof(validationErrors));
        }

        var errors = new Error[validationErrors.Length];
        for (int i = 0; i < validationErrors.Length; i++)
        {
            errors[i] = validationErrors[i];
        }

        return new Result<TValue>(default, false, errors[0], errors);
    }

    /// <summary>
    /// Matches the result to one of two functions based on success or failure.
    /// </summary>
    /// <typeparam name="T">The return type.</typeparam>
    /// <param name="onSuccess">Function to call on success.</param>
    /// <param name="onFailure">Function to call on failure.</param>
    /// <returns>The result of the matched function.</returns>
    public T Match<T>(Func<T> onSuccess, Func<Error, T> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        return IsSuccess ? onSuccess() : onFailure(Error);
    }

    /// <summary>
    /// Matches the result to one of two async functions based on success or failure.
    /// </summary>
    /// <typeparam name="T">The return type.</typeparam>
    /// <param name="onSuccess">Function to call on success.</param>
    /// <param name="onFailure">Function to call on failure.</param>
    /// <returns>The result of the matched function.</returns>
    public ValueTask<T> MatchAsync<T>(Func<ValueTask<T>> onSuccess, Func<Error, ValueTask<T>> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        return IsSuccess ? onSuccess() : onFailure(Error);
    }
}

/// <summary>
/// Represents the result of an operation that returns a value and may succeed or fail.
/// </summary>
/// <typeparam name="TValue">The value type.</typeparam>
[JsonConverter(typeof(ResultJsonConverterFactory))]
public sealed class Result<TValue> : Result
{
    private readonly TValue? _value;

    /// <summary>
    /// Gets the value. Throws <see cref="InvalidOperationException"/> if the result is a failure.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the result is a failure.</exception>
    [JsonInclude]
    public TValue Value
    {
        get
        {
            if (IsFailure)
            {
                throw new InvalidOperationException(
                    $"Cannot access Value on a failed result. Error: {Error}. " +
                    "Check IsSuccess before accessing Value.");
            }

            return _value!;
        }
    }

    internal Result(TValue? value, bool isSuccess, Error error, IReadOnlyList<Error> errors)
        : base(isSuccess, error, errors)
    {
        _value = value;
    }

    /// <summary>
    /// Creates a successful result with a value.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>A successful result.</returns>
    public static Result<TValue> Success(TValue value) =>
        new(value, true, Error.None, Array.Empty<Error>());

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    /// <param name="error">The error.</param>
    /// <returns>A failed result.</returns>
    public static new Result<TValue> Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result<TValue>(default, false, error, new[] { error });
    }

    /// <summary>
    /// Creates a failed result with multiple errors.
    /// </summary>
    /// <param name="errors">The errors.</param>
    /// <returns>A failed result.</returns>
    public static new Result<TValue> Failure(IReadOnlyList<Error> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (errors.Count == 0)
        {
            throw new ArgumentException("At least one error is required for a failure result.", nameof(errors));
        }

        return new Result<TValue>(default, false, errors[0], errors);
    }

    /// <summary>
    /// Maps the value to a new type if successful, preserving errors on failure.
    /// </summary>
    /// <typeparam name="TNew">The new value type.</typeparam>
    /// <param name="mapper">The mapping function.</param>
    /// <returns>A new result with the mapped value or the original errors.</returns>
    public Result<TNew> Map<TNew>(Func<TValue, TNew> mapper)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        return IsSuccess
            ? Result<TNew>.Success(mapper(_value!))
            : Result<TNew>.Failure(Errors);
    }

    /// <summary>
    /// Binds the value to a new result if successful, preserving errors on failure.
    /// </summary>
    /// <typeparam name="TNew">The new value type.</typeparam>
    /// <param name="binder">The binding function.</param>
    /// <returns>The new result or the original errors.</returns>
    public Result<TNew> Bind<TNew>(Func<TValue, Result<TNew>> binder)
    {
        ArgumentNullException.ThrowIfNull(binder);
        return IsSuccess ? binder(_value!) : Result<TNew>.Failure(Errors);
    }

    /// <summary>
    /// Async version of Map.
    /// </summary>
    public async ValueTask<Result<TNew>> MapAsync<TNew>(Func<TValue, ValueTask<TNew>> mapper)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        if (IsFailure) return Result<TNew>.Failure(Errors);
        var newValue = await mapper(_value!).ConfigureAwait(false);
        return Result<TNew>.Success(newValue);
    }

    /// <summary>
    /// Async version of Bind.
    /// </summary>
    public async ValueTask<Result<TNew>> BindAsync<TNew>(Func<TValue, ValueTask<Result<TNew>>> binder)
    {
        ArgumentNullException.ThrowIfNull(binder);
        if (IsFailure) return Result<TNew>.Failure(Errors);
        return await binder(_value!).ConfigureAwait(false);
    }

    /// <summary>
    /// Matches the result to one of two functions based on success or failure.
    /// </summary>
    /// <typeparam name="T">The return type.</typeparam>
    /// <param name="onSuccess">Function to call on success with the value.</param>
    /// <param name="onFailure">Function to call on failure with the error.</param>
    /// <returns>The result of the matched function.</returns>
    public T Match<T>(Func<TValue, T> onSuccess, Func<Error, T> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        return IsSuccess ? onSuccess(_value!) : onFailure(Error);
    }

    /// <summary>
    /// Async Match.
    /// </summary>
    public ValueTask<T> MatchAsync<T>(Func<TValue, ValueTask<T>> onSuccess, Func<Error, ValueTask<T>> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        return IsSuccess ? onSuccess(_value!) : onFailure(Error);
    }

    /// <summary>
    /// Implicitly converts a value to a successful result.
    /// </summary>
    public static implicit operator Result<TValue>(TValue? value) =>
        value is null
            ? Failure(Error.Failure("Result.NullValue", "The provided value is null."))
            : Success(value);

    /// <summary>
    /// Implicitly converts an error to a failed result.
    /// </summary>
    public static implicit operator Result<TValue>(Error error) => Failure(error);
}

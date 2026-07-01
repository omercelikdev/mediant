using FluentValidation;
using Mediant.Abstractions;
using Mediant.Results;

namespace Mediant.FluentValidation;

/// <summary>
/// Pipeline behavior that runs all FluentValidation validators for the request.
/// Multi-validator support: ALL validators run, all errors collected.
/// <para>
/// When <typeparamref name="TResponse"/> is <see cref="Result"/> or <see cref="Result{T}"/> the
/// failures are returned as a <c>Result.Failure</c> — no exception is thrown. For any other
/// response type there is no failure value to return, so a <c>FluentValidation.ValidationException</c>
/// is thrown; map it to a 400 in your host if you use non-Result handlers.
/// </para>
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    // Built once per closed generic type, not per request — no reflection on the hot path.
    private static readonly Func<IReadOnlyList<Error>, TResponse>? FailureFactory = BuildFailureFactory();

    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators ?? throw new ArgumentNullException(nameof(validators));
    }

    public async ValueTask<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var validatorArray = _validators as IValidator<TRequest>[] ?? _validators.ToArray();

        if (validatorArray.Length == 0)
        {
            return await next().ConfigureAwait(false);
        }

        var context = new ValidationContext<TRequest>(request);
        var errors = new List<ValidationError>();

        // Run ALL validators — collect all errors
        for (int i = 0; i < validatorArray.Length; i++)
        {
            var result = await validatorArray[i].ValidateAsync(context, cancellationToken).ConfigureAwait(false);

            if (!result.IsValid)
            {
                for (int j = 0; j < result.Errors.Count; j++)
                {
                    var failure = result.Errors[j];
                    errors.Add(new ValidationError(
                        failure.PropertyName,
                        failure.ErrorCode,
                        failure.ErrorMessage));
                }
            }
        }

        if (errors.Count > 0)
        {
            return CreateValidationFailureResult(errors);
        }

        return await next().ConfigureAwait(false);
    }

    private static TResponse CreateValidationFailureResult(List<ValidationError> validationErrors)
    {
        var errorArray = new Error[validationErrors.Count];
        for (int i = 0; i < validationErrors.Count; i++)
        {
            errorArray[i] = validationErrors[i];
        }

        if (FailureFactory is not null)
        {
            return FailureFactory(errorArray);
        }

        // No Result-shaped response to carry the failures — surface as an exception.
        throw new global::FluentValidation.ValidationException(
            validationErrors.Select(e => new global::FluentValidation.Results.ValidationFailure(
                e.PropertyName, e.Description) { ErrorCode = e.Code }));
    }

    private static Func<IReadOnlyList<Error>, TResponse>? BuildFailureFactory()
    {
        if (typeof(TResponse) == typeof(Result))
        {
            return errors => (TResponse)(object)Result.Failure(errors);
        }

        if (typeof(TResponse).IsGenericType && typeof(TResponse).GetGenericTypeDefinition() == typeof(Result<>))
        {
            var failureMethod = typeof(TResponse).GetMethod(
                "Failure",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                null,
                new[] { typeof(IReadOnlyList<Error>) },
                null);

            if (failureMethod is not null)
            {
                return (Func<IReadOnlyList<Error>, TResponse>)Delegate.CreateDelegate(
                    typeof(Func<IReadOnlyList<Error>, TResponse>), failureMethod);
            }
        }

        return null;
    }
}

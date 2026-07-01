using Microsoft.AspNetCore.Http;
using Mediant.Results;

namespace Mediant.AspNetCore.Mapping;

/// <summary>
/// Maps Result types to HTTP responses with RFC 7807 ProblemDetails.
/// </summary>
public static class ResultToActionResultMapper
{
    /// <summary>
    /// Maps a Result to an IResult for Minimal API endpoints.
    /// </summary>
    public static IResult ToHttpResult(Result result, int successStatusCode = StatusCodes.Status200OK)
    {
        if (result.IsSuccess)
        {
            // 201 without a known resource URI: emit 201 with no Location rather than a malformed
            // empty Location header. The handler can return a Location itself if it has one.
            return successStatusCode == StatusCodes.Status201Created
                ? Microsoft.AspNetCore.Http.Results.StatusCode(StatusCodes.Status201Created)
                : Microsoft.AspNetCore.Http.Results.Ok();
        }

        return MapErrorToHttpResult(result.Error, result.Errors);
    }

    /// <summary>
    /// Maps a Result of T to an IResult for Minimal API endpoints.
    /// </summary>
    public static IResult ToHttpResult<T>(Result<T> result, int successStatusCode = StatusCodes.Status200OK)
    {
        if (result.IsSuccess)
        {
            // Pass a null location (not string.Empty) so 201 responses don't carry an invalid
            // empty Location header.
            return successStatusCode == StatusCodes.Status201Created
                ? Microsoft.AspNetCore.Http.Results.Created((string?)null, result.Value)
                : Microsoft.AspNetCore.Http.Results.Ok(result.Value);
        }

        return MapErrorToHttpResult(result.Error, result.Errors);
    }

    private static IResult MapErrorToHttpResult(Error error, IReadOnlyList<Error> errors)
    {
        return error.Type switch
        {
            ErrorType.Validation => Microsoft.AspNetCore.Http.Results.ValidationProblem(
                CreateValidationErrors(errors),
                detail: error.Description,
                title: "Validation Error"),

            // A general domain failure is not a server error — 422 Unprocessable Entity, not 500.
            ErrorType.Failure => Microsoft.AspNetCore.Http.Results.Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "Unprocessable Entity",
                detail: error.Description),

            ErrorType.NotFound => Microsoft.AspNetCore.Http.Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Not Found",
                detail: error.Description),

            ErrorType.Conflict => Microsoft.AspNetCore.Http.Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Conflict",
                detail: error.Description),

            ErrorType.Unauthorized => Microsoft.AspNetCore.Http.Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized",
                detail: error.Description),

            ErrorType.Forbidden => Microsoft.AspNetCore.Http.Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Forbidden",
                detail: error.Description),

            ErrorType.Unavailable => Microsoft.AspNetCore.Http.Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Service Unavailable",
                detail: error.Description),

            _ => Microsoft.AspNetCore.Http.Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: error.Description)
        };
    }

    private static Dictionary<string, string[]> CreateValidationErrors(IReadOnlyList<Error> errors)
    {
        var validationErrors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        for (int i = 0; i < errors.Count; i++)
        {
            var error = errors[i];
            // Group by property name; non-validation errors fall under a generic key so they are
            // still surfaced rather than dropped.
            var key = error is ValidationError validationError ? validationError.PropertyName : string.Empty;

            if (!validationErrors.TryGetValue(key, out var existing))
            {
                validationErrors[key] = new[] { error.Description };
            }
            else
            {
                var newArray = new string[existing.Length + 1];
                existing.CopyTo(newArray, 0);
                newArray[existing.Length] = error.Description;
                validationErrors[key] = newArray;
            }
        }

        return validationErrors;
    }
}

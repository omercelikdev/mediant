using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Mediant.Guards;

/// <summary>
/// Provides guard clause methods for argument validation.
/// </summary>
public static class Guard
{
    /// <summary>
    /// Throws <see cref="ArgumentNullException"/> if the value is null.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="value">The value to check.</param>
    /// <param name="parameterName">The name of the parameter.</param>
    /// <returns>The non-null value.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="value"/> is null.</exception>
    public static T Against<T>(
        [NotNull] T? value,
        [CallerArgumentExpression(nameof(value))] string? parameterName = null) where T : class
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        return value;
    }

    /// <summary>
    /// Throws <see cref="ArgumentException"/> if the string is null or empty.
    /// </summary>
    /// <param name="value">The string value to check.</param>
    /// <param name="parameterName">The name of the parameter.</param>
    /// <returns>The non-null, non-empty string.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="value"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> is empty.</exception>
    public static string AgainstNullOrEmpty(
        [NotNull] string? value,
        [CallerArgumentExpression(nameof(value))] string? parameterName = null)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (value.Length == 0)
        {
            throw new ArgumentException("Value cannot be empty.", parameterName);
        }

        return value;
    }

    /// <summary>
    /// Throws <see cref="ArgumentException"/> if the string is null, empty, or whitespace.
    /// </summary>
    /// <param name="value">The string value to check.</param>
    /// <param name="parameterName">The name of the parameter.</param>
    /// <returns>The non-null, non-whitespace string.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="value"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> is empty or whitespace.</exception>
    public static string AgainstNullOrWhiteSpace(
        [NotNull] string? value,
        [CallerArgumentExpression(nameof(value))] string? parameterName = null)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be empty or whitespace.", parameterName);
        }

        return value;
    }

    /// <summary>
    /// Throws <see cref="ArgumentOutOfRangeException"/> if the value is negative.
    /// </summary>
    /// <param name="value">The value to check.</param>
    /// <param name="parameterName">The name of the parameter.</param>
    /// <returns>The non-negative value.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="value"/> is negative.</exception>
    public static int AgainstNegative(
        int value,
        [CallerArgumentExpression(nameof(value))] string? parameterName = null)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Value cannot be negative.");
        }

        return value;
    }

    /// <summary>
    /// Throws <see cref="ArgumentOutOfRangeException"/> if the value is zero or negative.
    /// </summary>
    /// <param name="value">The value to check.</param>
    /// <param name="parameterName">The name of the parameter.</param>
    /// <returns>The positive value.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="value"/> is zero or negative.</exception>
    public static int AgainstZeroOrNegative(
        int value,
        [CallerArgumentExpression(nameof(value))] string? parameterName = null)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Value must be positive.");
        }

        return value;
    }

    /// <summary>
    /// Throws <see cref="ArgumentException"/> if the collection is null or empty.
    /// </summary>
    /// <typeparam name="T">The type of items in the collection.</typeparam>
    /// <param name="value">The collection to check.</param>
    /// <param name="parameterName">The name of the parameter.</param>
    /// <returns>The non-null, non-empty collection.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="value"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> is empty.</exception>
    public static IReadOnlyList<T> AgainstNullOrEmpty<T>(
        [NotNull] IReadOnlyList<T>? value,
        [CallerArgumentExpression(nameof(value))] string? parameterName = null)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (value.Count == 0)
        {
            throw new ArgumentException("Collection cannot be empty.", parameterName);
        }

        return value;
    }
}

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Qorpe.Mediator.Results;

/// <summary>
/// <see cref="JsonConverterFactory"/> that enables round-trip JSON serialization of
/// <see cref="Result"/> and <see cref="Result{TValue}"/>.
/// <para>
/// Without it, <see cref="Result{TValue}"/> cannot be deserialized (it has no public
/// parameterless constructor) and serializing a failed result throws, because the
/// <see cref="Result{TValue}.Value"/> getter intentionally throws on failure. This converter
/// never touches <c>Value</c> on the failure path, so caching and HTTP serialization of any
/// result are safe.
/// </para>
/// </summary>
public sealed class ResultJsonConverterFactory : JsonConverterFactory
{
    internal const string JsonAotMessage =
        "JSON (de)serialization of Result<T> uses reflection-based System.Text.Json. For trimming/Native AOT, " +
        "use System.Text.Json source generation for the value types.";

    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert)
        => typeToConvert == typeof(Result)
           || (typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Result<>));

    /// <inheritdoc />
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = JsonAotMessage)]
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        if (typeToConvert == typeof(Result))
        {
            return new ResultConverter();
        }

        var valueType = typeToConvert.GetGenericArguments()[0];
        return (JsonConverter)Activator.CreateInstance(
            typeof(ResultValueConverter<>).MakeGenericType(valueType))!;
    }

    internal static bool TryGetProperty(in JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    internal static bool ReadIsSuccess(in JsonElement root)
    {
        if (TryGetProperty(root, "IsSuccess", out var s) &&
            (s.ValueKind == JsonValueKind.True || s.ValueKind == JsonValueKind.False))
        {
            return s.GetBoolean();
        }

        // Fall back to presence of errors.
        return !(TryGetProperty(root, "Errors", out var e) && e.ValueKind == JsonValueKind.Array && e.GetArrayLength() > 0);
    }

    [RequiresUnreferencedCode(JsonAotMessage)]
    [RequiresDynamicCode(JsonAotMessage)]
    internal static IReadOnlyList<Error> ReadErrors(in JsonElement root, JsonSerializerOptions options)
    {
        var errors = new List<Error>();
        if (TryGetProperty(root, "Errors", out var errEl) && errEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in errEl.EnumerateArray())
            {
                var err = item.Deserialize<Error>(options);
                if (err is not null)
                {
                    errors.Add(err);
                }
            }
        }

        if (errors.Count == 0 && TryGetProperty(root, "Error", out var single) && single.ValueKind == JsonValueKind.Object)
        {
            var err = single.Deserialize<Error>(options);
            if (err is not null && err != Error.None)
            {
                errors.Add(err);
            }
        }

        if (errors.Count == 0)
        {
            errors.Add(Error.Failure("Result.Failure", "The result is a failure."));
        }

        return errors;
    }
}

internal sealed class ResultConverter : JsonConverter<Result>
{
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = ResultJsonConverterFactory.JsonAotMessage)]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = ResultJsonConverterFactory.JsonAotMessage)]
    public override Result Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        return ResultJsonConverterFactory.ReadIsSuccess(root)
            ? Result.Success()
            : Result.Failure(ResultJsonConverterFactory.ReadErrors(root, options));
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = ResultJsonConverterFactory.JsonAotMessage)]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = ResultJsonConverterFactory.JsonAotMessage)]
    public override void Write(Utf8JsonWriter writer, Result value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteBoolean("IsSuccess", value.IsSuccess);
        if (!value.IsSuccess)
        {
            writer.WritePropertyName("Errors");
            JsonSerializer.Serialize(writer, value.Errors, options);
        }
        writer.WriteEndObject();
    }
}

internal sealed class ResultValueConverter<TValue> : JsonConverter<Result<TValue>>
{
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = ResultJsonConverterFactory.JsonAotMessage)]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = ResultJsonConverterFactory.JsonAotMessage)]
    public override Result<TValue> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        if (!ResultJsonConverterFactory.ReadIsSuccess(root))
        {
            return Result<TValue>.Failure(ResultJsonConverterFactory.ReadErrors(root, options));
        }

        TValue? value = default;
        if (ResultJsonConverterFactory.TryGetProperty(root, "Value", out var valueEl) &&
            valueEl.ValueKind != JsonValueKind.Null)
        {
            value = valueEl.Deserialize<TValue>(options);
        }

        return Result<TValue>.Success(value!);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = ResultJsonConverterFactory.JsonAotMessage)]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = ResultJsonConverterFactory.JsonAotMessage)]
    public override void Write(Utf8JsonWriter writer, Result<TValue> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteBoolean("IsSuccess", value.IsSuccess);
        if (value.IsSuccess)
        {
            // Safe: Value is only accessed on the success path.
            writer.WritePropertyName("Value");
            JsonSerializer.Serialize(writer, value.Value, options);
        }
        else
        {
            writer.WritePropertyName("Errors");
            JsonSerializer.Serialize(writer, value.Errors, options);
        }

        writer.WriteEndObject();
    }
}

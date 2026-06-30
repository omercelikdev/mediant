using System.Text.Json;
using Qorpe.Mediator.Results;

namespace Qorpe.Mediator.UnitTests.Results;

/// <summary>
/// Regression tests for JSON round-trip of <see cref="Result"/> and <see cref="Result{TValue}"/>.
/// Before the custom converter, <see cref="Result{TValue}"/> could not be deserialized (no usable
/// constructor) — silently disabling distributed caching of result responses — and serializing a
/// failed result threw because the Value getter throws on failure.
/// </summary>
public class ResultSerializationTests
{
    [Fact]
    public void ResultOfT_Success_Should_RoundTrip()
    {
        var original = Result<string>.Success("hello");

        var json = JsonSerializer.Serialize(original);
        var back = JsonSerializer.Deserialize<Result<string>>(json);

        back.Should().NotBeNull();
        back!.IsSuccess.Should().BeTrue();
        back.Value.Should().Be("hello");
    }

    [Fact]
    public void ResultOfT_Failure_Should_Serialize_Without_Throwing_And_RoundTrip()
    {
        var original = Result<string>.Failure(Error.NotFound("Order.NotFound", "missing"));

        var act = () => JsonSerializer.Serialize(original);
        act.Should().NotThrow("serializing a failed result must not touch the throwing Value getter");

        var json = JsonSerializer.Serialize(original);
        var back = JsonSerializer.Deserialize<Result<string>>(json);

        back!.IsSuccess.Should().BeFalse();
        back.Error.Code.Should().Be("Order.NotFound");
        back.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public void ResultOfT_Failure_With_Multiple_Errors_Should_RoundTrip_All()
    {
        var errors = new Error[]
        {
            Error.Validation("Name.Required", "Name is required"),
            Error.Validation("Email.Invalid", "Email is invalid"),
        };
        var original = Result<int>.Failure(errors);

        var json = JsonSerializer.Serialize(original);
        var back = JsonSerializer.Deserialize<Result<int>>(json);

        back!.IsSuccess.Should().BeFalse();
        back.Errors.Should().HaveCount(2);
        back.Errors.Select(e => e.Code).Should().Contain(new[] { "Name.Required", "Email.Invalid" });
    }

    [Fact]
    public void NonGeneric_Result_Should_RoundTrip()
    {
        var ok = JsonSerializer.Deserialize<Result>(JsonSerializer.Serialize(Result.Success()));
        ok!.IsSuccess.Should().BeTrue();

        var fail = JsonSerializer.Deserialize<Result>(
            JsonSerializer.Serialize(Result.Failure(Error.Conflict("Dup", "duplicate"))));
        fail!.IsFailure.Should().BeTrue();
        fail.Error.Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public void ResultOfT_With_Complex_Value_Should_RoundTrip()
    {
        var original = Result<Person>.Success(new Person("Ada", 36));

        var json = JsonSerializer.Serialize(original);
        var back = JsonSerializer.Deserialize<Result<Person>>(json);

        back!.IsSuccess.Should().BeTrue();
        back.Value.Name.Should().Be("Ada");
        back.Value.Age.Should().Be(36);
    }

    public sealed record Person(string Name, int Age);
}

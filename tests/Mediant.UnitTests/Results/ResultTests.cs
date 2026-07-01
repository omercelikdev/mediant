using Mediant.Results;

namespace Mediant.UnitTests.Results;

public class ResultTests
{
    [Fact]
    public void Success_ShouldCreateSuccessResult()
    {
        var result = Result.Success();
        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
        result.Error.Should().Be(Error.None);
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Failure_WithError_ShouldCreateFailureResult()
    {
        var error = Error.Failure("Code", "Description");
        var result = Result.Failure(error);
        result.IsSuccess.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
        result.Errors.Should().ContainSingle().Which.Should().Be(error);
    }

    [Fact]
    public void Failure_WithMultipleErrors_ShouldContainAllErrors()
    {
        var errors = new Error[]
        {
            Error.Validation("V1", "Error 1"),
            Error.Validation("V2", "Error 2")
        };
        var result = Result.Failure(errors);
        result.Errors.Should().HaveCount(2);
        result.Error.Should().Be(errors[0]);
    }

    [Fact]
    public void Failure_WithEmptyErrors_ShouldThrow()
    {
        var act = () => Result.Failure(Array.Empty<Error>());
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Failure_WithNullError_ShouldThrow()
    {
        var act = () => Result.Failure((Error)null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Match_OnSuccess_ShouldCallOnSuccess()
    {
        var result = Result.Success();
        var value = result.Match(() => "success", e => "failure");
        value.Should().Be("success");
    }

    [Fact]
    public void Match_OnFailure_ShouldCallOnFailure()
    {
        var result = Result.Failure(Error.Failure("E", "err"));
        var value = result.Match(() => "success", e => $"failure:{e.Code}");
        value.Should().Be("failure:E");
    }

    [Fact]
    public void ValidationFailure_ShouldCreateValidationResult()
    {
        var result = Result.ValidationFailure(
            new ValidationError("Name", "Name is required"));
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }
}

public class ResultOfTTests
{
    [Fact]
    public void Success_ShouldCreateSuccessResult()
    {
        var result = Result<int>.Success(42);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void Value_OnFailure_ShouldThrowInvalidOperation()
    {
        var result = Result<int>.Failure(Error.Failure("E", "err"));
        var act = () => _ = result.Value;
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cannot access Value*");
    }

    [Fact]
    public void ImplicitConversion_FromValue_ShouldCreateSuccess()
    {
        Result<string> result = "hello";
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("hello");
    }

    [Fact]
    public void ImplicitConversion_FromNull_ShouldCreateFailure()
    {
        Result<string> result = (string)null!;
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void ImplicitConversion_FromError_ShouldCreateFailure()
    {
        Result<string> result = Error.NotFound("NF", "Not found");
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public void Map_OnSuccess_ShouldTransformValue()
    {
        var result = Result<int>.Success(5);
        var mapped = result.Map(x => x * 2);
        mapped.IsSuccess.Should().BeTrue();
        mapped.Value.Should().Be(10);
    }

    [Fact]
    public void Map_OnFailure_ShouldPreserveErrors()
    {
        var error = Error.Failure("E", "err");
        var result = Result<int>.Failure(error);
        var mapped = result.Map(x => x * 2);
        mapped.IsFailure.Should().BeTrue();
        mapped.Error.Should().Be(error);
    }

    [Fact]
    public void Bind_OnSuccess_ShouldChainResults()
    {
        var result = Result<int>.Success(5);
        var bound = result.Bind(x => Result<string>.Success($"Value: {x}"));
        bound.IsSuccess.Should().BeTrue();
        bound.Value.Should().Be("Value: 5");
    }

    [Fact]
    public void Bind_OnFailure_ShouldPreserveErrors()
    {
        var result = Result<int>.Failure(Error.Failure("E", "err"));
        var bound = result.Bind(x => Result<string>.Success("never"));
        bound.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Match_OnSuccess_ShouldCallOnSuccessWithValue()
    {
        var result = Result<int>.Success(42);
        var output = result.Match(v => $"Value: {v}", e => $"Error: {e.Code}");
        output.Should().Be("Value: 42");
    }

    [Fact]
    public void Match_OnFailure_ShouldCallOnFailureWithError()
    {
        var result = Result<int>.Failure(Error.Failure("E", "err"));
        var output = result.Match(v => $"Value: {v}", e => $"Error: {e.Code}");
        output.Should().Be("Error: E");
    }

    [Fact]
    public async Task MapAsync_OnSuccess_ShouldTransformValue()
    {
        var result = Result<int>.Success(5);
        var mapped = await result.MapAsync(x => new ValueTask<string>($"V:{x}"));
        mapped.IsSuccess.Should().BeTrue();
        mapped.Value.Should().Be("V:5");
    }

    [Fact]
    public async Task BindAsync_OnSuccess_ShouldChainResults()
    {
        var result = Result<int>.Success(5);
        var bound = await result.BindAsync(x =>
            new ValueTask<Result<string>>(Result<string>.Success($"B:{x}")));
        bound.IsSuccess.Should().BeTrue();
        bound.Value.Should().Be("B:5");
    }
}

public class ErrorTests
{
    [Fact]
    public void None_ShouldHaveNoneType()
    {
        Error.None.Type.Should().Be(ErrorType.None);
        Error.None.Code.Should().BeEmpty();
    }

    [Fact]
    public void Factory_Methods_ShouldSetCorrectType()
    {
        Error.Failure("F", "d").Type.Should().Be(ErrorType.Failure);
        Error.Validation("V", "d").Type.Should().Be(ErrorType.Validation);
        Error.NotFound("NF", "d").Type.Should().Be(ErrorType.NotFound);
        Error.Conflict("C", "d").Type.Should().Be(ErrorType.Conflict);
        Error.Unauthorized("U", "d").Type.Should().Be(ErrorType.Unauthorized);
        Error.Forbidden("F", "d").Type.Should().Be(ErrorType.Forbidden);
        Error.Internal("I", "d").Type.Should().Be(ErrorType.Internal);
        Error.Unavailable("UA", "d").Type.Should().Be(ErrorType.Unavailable);
    }

    [Fact]
    public void Equals_SameCodeAndDescription_ShouldBeEqual()
    {
        var a = Error.Failure("F", "desc");
        var b = Error.Failure("F", "desc");
        a.Should().Be(b);
        (a == b).Should().BeTrue();
    }

    [Fact]
    public void Equals_DifferentCode_ShouldNotBeEqual()
    {
        var a = Error.Failure("F1", "desc");
        var b = Error.Failure("F2", "desc");
        a.Should().NotBe(b);
    }

    [Fact]
    public void ToString_ShouldContainCodeAndDescription()
    {
        var error = Error.Failure("MyCode", "Something went wrong");
        error.ToString().Should().Contain("MyCode").And.Contain("Something went wrong");
    }
}

public class ValidationErrorTests
{
    [Fact]
    public void ShouldHavePropertyName()
    {
        var error = new ValidationError("Email", "Email is required");
        error.PropertyName.Should().Be("Email");
        error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public void ShouldSupportCustomCode()
    {
        var error = new ValidationError("Name", "Custom.Code", "Name too long");
        error.Code.Should().Be("Custom.Code");
        error.PropertyName.Should().Be("Name");
    }
}

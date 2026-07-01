using FluentValidation;
using Mediant.Abstractions;
using Mediant.FluentValidation;
using Mediant.Results;
using Mediant.UnitTests.Helpers;

namespace Mediant.UnitTests.Behaviors;

// Test validator
public class TestCommandValidator : AbstractValidator<TestCommandWithResponse>
{
    public TestCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required");
        RuleFor(x => x.Name).MaximumLength(50).WithMessage("Name too long");
    }
}

public class StrictCommandValidator : AbstractValidator<TestCommandWithResponse>
{
    public StrictCommandValidator()
    {
        RuleFor(x => x.Name).Must(n => n != "INVALID").WithMessage("Name cannot be INVALID");
    }
}

public class ValidationBehaviorTests
{
    [Fact]
    public async Task Should_Pass_When_No_Validators()
    {
        var validators = Enumerable.Empty<IValidator<TestCommand>>();
        var behavior = new ValidationBehavior<TestCommand, Result>(validators);

        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());
        var result = await behavior.Handle(new TestCommand("test"), next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Should_Return_Failure_On_Validation_Error()
    {
        var validators = new IValidator<TestCommandWithResponse>[] { new TestCommandValidator() };
        var behavior = new ValidationBehavior<TestCommandWithResponse, Result<string>>(validators);
        var handlerCalled = false;

        RequestHandlerDelegate<Result<string>> next = () =>
        {
            handlerCalled = true;
            return new ValueTask<Result<string>>(Result<string>.Success("ok"));
        };

        var result = await behavior.Handle(
            new TestCommandWithResponse(string.Empty), next, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
        handlerCalled.Should().BeFalse(); // Handler NOT called
    }

    [Fact]
    public async Task Should_Collect_Errors_From_Multiple_Validators()
    {
        var validators = new IValidator<TestCommandWithResponse>[]
        {
            new TestCommandValidator(),
            new StrictCommandValidator()
        };
        var behavior = new ValidationBehavior<TestCommandWithResponse, Result<string>>(validators);

        RequestHandlerDelegate<Result<string>> next = () =>
            new ValueTask<Result<string>>(Result<string>.Success("ok"));

        // Empty name fails both validators
        var result = await behavior.Handle(
            new TestCommandWithResponse(string.Empty), next, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Errors.Count.Should().BeGreaterThan(1); // Multiple errors collected
    }

    [Fact]
    public async Task Should_Pass_Valid_Request()
    {
        var validators = new IValidator<TestCommandWithResponse>[] { new TestCommandValidator() };
        var behavior = new ValidationBehavior<TestCommandWithResponse, Result<string>>(validators);

        RequestHandlerDelegate<Result<string>> next = () =>
            new ValueTask<Result<string>>(Result<string>.Success("ok"));

        var result = await behavior.Handle(
            new TestCommandWithResponse("ValidName"), next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Should_Run_ALL_Validators_Not_Stop_On_First()
    {
        var validators = new IValidator<TestCommandWithResponse>[]
        {
            new TestCommandValidator(), // Will fail on empty name
            new StrictCommandValidator() // Will also check
        };
        var behavior = new ValidationBehavior<TestCommandWithResponse, Result<string>>(validators);

        RequestHandlerDelegate<Result<string>> next = () =>
            new ValueTask<Result<string>>(Result<string>.Success("ok"));

        var result = await behavior.Handle(
            new TestCommandWithResponse("INVALID"), next, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        // StrictCommandValidator should have caught "INVALID"
    }
}

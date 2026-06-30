using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Qorpe.Mediator.AspNetCore.Mapping;
using Qorpe.Mediator.Results;

namespace Qorpe.Mediator.UnitTests.AspNetCore;

public class ResultToActionResultMapperTests
{
    private static async Task<int> ExecuteStatusAsync(IResult result)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        };
        context.Response.Body = new MemoryStream();
        await result.ExecuteAsync(context);
        return context.Response.StatusCode;
    }

    [Fact]
    public async Task General_Failure_Maps_To_422_Not_500()
    {
        var result = ResultToActionResultMapper.ToHttpResult(
            Result.Failure(Error.Failure("Order.Rejected", "Business rule failed")));

        (await ExecuteStatusAsync(result)).Should().Be(StatusCodes.Status422UnprocessableEntity);
    }

    [Fact]
    public async Task Internal_Error_Maps_To_500()
    {
        var result = ResultToActionResultMapper.ToHttpResult(
            Result.Failure(Error.Internal("Unexpected", "boom")));

        (await ExecuteStatusAsync(result)).Should().Be(StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public async Task NotFound_Maps_To_404()
    {
        var result = ResultToActionResultMapper.ToHttpResult(
            Result.Failure(Error.NotFound("Order.NotFound", "missing")));

        (await ExecuteStatusAsync(result)).Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task Validation_Maps_To_400()
    {
        var result = ResultToActionResultMapper.ToHttpResult(
            Result.Failure(new ValidationError("Name", "Name is required")));

        (await ExecuteStatusAsync(result)).Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task Success_With_201_Returns_201()
    {
        var result = ResultToActionResultMapper.ToHttpResult(
            Result<int>.Success(42), StatusCodes.Status201Created);

        (await ExecuteStatusAsync(result)).Should().Be(StatusCodes.Status201Created);
    }

    [Fact]
    public async Task Success_With_200_Returns_200()
    {
        var result = ResultToActionResultMapper.ToHttpResult(
            Result<int>.Success(42));

        (await ExecuteStatusAsync(result)).Should().Be(StatusCodes.Status200OK);
    }
}

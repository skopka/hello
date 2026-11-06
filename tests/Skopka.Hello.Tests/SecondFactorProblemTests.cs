using Microsoft.AspNetCore.Http;
using Skopka.Abstraction.OperationResult;
using Skopka.Hello.Endpoints;

namespace Skopka.Hello.Tests;

public sealed class SecondFactorProblemTests
{
    [Fact]
    public void RequiredContinuationIsExplicitlyWhitelisted()
    {
        var context = new DefaultHttpContext();
        var details = new HelloSecondFactorRequired("protected-ticket", DateTimeOffset.UtcNow.AddMinutes(5));
        var result = OperationResultFactory.Fail(new Error(HelloSecondFactorErrorCodes.Required,
            "Continue sign-in.", ErrorType.Unauthorized, details));
        var problem = OperationResultProblemMapper.ToResult(result, context).ProblemDetails;
        Assert.Equal(StatusCodes.Status401Unauthorized, problem.Status);
        Assert.True(problem.Extensions.ContainsKey("secondFactor"));
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.Equal("no-referrer", context.Response.Headers["Referrer-Policy"].ToString());
    }

    [Fact]
    public void ArbitraryDetailsAreNotExposed()
    {
        var context = new DefaultHttpContext();
        var result = OperationResultFactory.Fail(new Error("test.error", "Failed.",
            ErrorType.Validation, new { Secret = "must-not-leak" }));
        var problem = OperationResultProblemMapper.ToResult(result, context).ProblemDetails;
        Assert.False(problem.Extensions.ContainsKey("secondFactor"));
        Assert.DoesNotContain(problem.Extensions.Values, value => value?.ToString()?.Contains("must-not-leak") == true);
    }
}

using Microsoft.AspNetCore.Http;
using Skopka.Abstraction.OperationResult;
using Skopka.Hello.Endpoints;
using Skopka.Identity.Errors;
using Skopka.Identity.RateLimiting;

namespace Skopka.Hello.Tests;

public sealed class OperationResultProblemMapperTests
{
    [Fact]
    public void MapReturnsValidationDetails()
    {
        var mapping = OperationResultProblemMapper.Map(
        [
            new Error(
                IdentityErrorCodes.Validation,
                "Validation failed.",
                ErrorType.Validation,
                new ValidationDetails(
                    new Dictionary<string, string[]>
                    {
                        ["Email"] = ["Email is invalid."],
                    })),
        ]);

        Assert.Equal(StatusCodes.Status400BadRequest, mapping.Status);
        Assert.Equal(
            "Email is invalid.",
            Assert.Single(mapping.ValidationErrors["Email"]));
    }

    [Fact]
    public void MapReturns429ForRateLimit()
    {
        var retryAfter = DateTimeOffset.UtcNow.AddMinutes(1);
        var mapping = OperationResultProblemMapper.Map(
        [
            new Error(
                IdentityErrorCodes.RateLimitExceeded,
                "Too many requests.",
                ErrorType.Forbidden,
                new RateLimitDetails(retryAfter)),
        ]);

        Assert.Equal(
            StatusCodes.Status429TooManyRequests,
            mapping.Status);
        Assert.Equal(retryAfter, mapping.RetryAfter);
    }
}

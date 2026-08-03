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

    [Theory]
    [InlineData(HelloDeliveryErrorCodes.NotConfigured)]
    [InlineData(HelloDeliveryErrorCodes.Failed)]
    public void MapReturns503ForDeliveryAvailabilityFailures(
        string errorCode)
    {
        var mapping = OperationResultProblemMapper.Map(
        [
            new Error(
                errorCode,
                "Delivery is unavailable.",
                ErrorType.Failure),
        ]);

        Assert.Equal(
            StatusCodes.Status503ServiceUnavailable,
            mapping.Status);
        Assert.Equal("Service Unavailable", mapping.Title);
    }

    [Fact]
    public void PasswordChangeRestartMarkerIsPrimaryConflict()
    {
        var mapping = OperationResultProblemMapper.Map(
        [
            new Error(
                HelloPasswordChangeErrorCodes.RestartRequired,
                "Request a new code.",
                ErrorType.Conflict),
            new Error(
                IdentityErrorCodes.PasswordRejected,
                "Password rejected.",
                ErrorType.Validation,
                new ValidationDetails(
                    new Dictionary<string, string[]>
                    {
                        ["newPassword"] = ["Use a stronger password."],
                    })),
        ]);

        Assert.Equal(StatusCodes.Status409Conflict, mapping.Status);
        Assert.Equal(
            HelloPasswordChangeErrorCodes.RestartRequired,
            mapping.Code);
        Assert.Equal("Request a new code.", mapping.Detail);
        Assert.Equal(
            "Use a stronger password.",
            Assert.Single(mapping.ValidationErrors["newPassword"]));
    }

    [Fact]
    public void PasswordChangedCleanupMarkerIsPrimaryConflict()
    {
        var mapping = OperationResultProblemMapper.Map(
        [
            new Error(
                HelloPasswordChangeErrorCodes.SessionCleanupRequired,
                "Sign in again with the new password.",
                ErrorType.Conflict),
            new Error(
                "test.session.revoke_failed",
                "Session revocation failed.",
                ErrorType.Failure),
        ]);

        Assert.Equal(StatusCodes.Status409Conflict, mapping.Status);
        Assert.Equal(
            HelloPasswordChangeErrorCodes.SessionCleanupRequired,
            mapping.Code);
        Assert.Equal(
            "Sign in again with the new password.",
            mapping.Detail);
    }
}

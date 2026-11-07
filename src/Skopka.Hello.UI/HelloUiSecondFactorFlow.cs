using Microsoft.AspNetCore.Http;
using Skopka.Abstraction.OperationResult;

namespace Skopka.Hello.UI;

/// <summary>
/// Short-lived server-side presentation state for a prepared sign-in challenge.
/// GET requests may redisplay it without starting another identity operation.
/// </summary>
internal sealed record HelloUiSecondFactorState(string Ticket, string View, DateTimeOffset ExpiresAt,
    string? Secret = null, string? ProvisioningUri = null, IReadOnlyList<string>? RecoveryCodes = null)
{
    public override string ToString() => "[protected second-factor presentation state]";
}

internal static class HelloUiSecondFactorFlow
{
    internal static async Task<(HelloUiSecondFactorState? State, IReadOnlyCollection<Error> Errors)> BeginAsync(
        HelloSecondFactorRequired required, IHelloUiSecondFactorApplication application, HttpContext context, CancellationToken ct)
    {
        var status = await application.InspectAsync(required.Ticket, ct);
        if (!status.IsSuccess) return (null, status.Errors);
        if (status.Value.Stage == HelloSecondFactorStage.Enroll)
        {
            var enrollment = await application.BeginEnrollmentAsync(required.Ticket, context, ct);
            return enrollment.IsSuccess
                ? (new(enrollment.Value.Ticket, "enroll", enrollment.Value.ExpiresAt, enrollment.Value.Secret, enrollment.Value.ProvisioningUri), [])
                : (null, enrollment.Errors);
        }
        var verification = await application.BeginVerificationAsync(required.Ticket, context, ct);
        return verification.IsSuccess
            ? (new(verification.Value.Ticket, "verify", verification.Value.ExpiresAt), [])
            : (null, verification.Errors);
    }
}


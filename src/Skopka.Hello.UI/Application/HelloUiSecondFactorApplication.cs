using Microsoft.AspNetCore.Http;
using Skopka.Abstraction.OperationResult;

namespace Skopka.Hello.UI;

public interface IHelloUiSecondFactorApplication
{
    Task<OperationResult<HelloSecondFactorStatus>> InspectAsync(string ticket, CancellationToken ct);
    Task<OperationResult<HelloSecondFactorContinuation>> BeginVerificationAsync(string ticket, HttpContext context, CancellationToken ct);
    Task<OperationResult<HelloSecondFactorEnrollment>> BeginEnrollmentAsync(string ticket, HttpContext context, CancellationToken ct);
    Task<OperationResult<HelloSecondFactorRecovery>> ConfirmEnrollmentAsync(string ticket, string code, HttpContext context, CancellationToken ct);
    Task<OperationResult<HelloUiSignIn>> CompleteVerificationAsync(string ticket, string code, HttpContext context, CancellationToken ct);
    Task<OperationResult<HelloUiSignIn>> AcknowledgeAsync(string ticket, CancellationToken ct);
}

internal sealed class HelloUiSecondFactorApplication<TProfile>(
    IHelloSecondFactorApplication<TProfile> application,
    IHelloUiProfileFactory<TProfile> profiles,
    IHelloRequestContext requestContext) : IHelloUiSecondFactorApplication
{
    public Task<OperationResult<HelloSecondFactorStatus>> InspectAsync(string ticket, CancellationToken ct)
        => application.InspectAsync(ticket, ct);
    public Task<OperationResult<HelloSecondFactorContinuation>> BeginVerificationAsync(string ticket, HttpContext context, CancellationToken ct)
        => application.BeginVerificationAsync(ticket, requestContext.CreateClientKey(context), ct);
    public Task<OperationResult<HelloSecondFactorEnrollment>> BeginEnrollmentAsync(string ticket, HttpContext context, CancellationToken ct)
        => application.BeginEnrollmentAsync(ticket, requestContext.CreateClientKey(context), ct);
    public Task<OperationResult<HelloSecondFactorRecovery>> ConfirmEnrollmentAsync(string ticket, string code, HttpContext context, CancellationToken ct)
        => application.ConfirmEnrollmentAsync(ticket, code, requestContext.CreateClientKey(context), ct);
    public async Task<OperationResult<HelloUiSignIn>> CompleteVerificationAsync(string ticket, string code, HttpContext context, CancellationToken ct)
        => Convert(await application.CompleteVerificationAsync(ticket, code, requestContext.CreateClientKey(context), ct));
    public async Task<OperationResult<HelloUiSignIn>> AcknowledgeAsync(string ticket, CancellationToken ct)
        => Convert(await application.AcknowledgeEnrollmentAsync(ticket, ct));
    private OperationResult<HelloUiSignIn> Convert(OperationResult<HelloSignIn<TProfile>> result)
        => result.IsSuccess
            ? OperationResultFactory.Success(new HelloUiSignIn(
                HelloUiPrincipalFactory.Create(result.Value.Account, result.Value.Session.SessionId, profiles), result.Value.Session))
            : OperationResultFactory.Fail<HelloUiSignIn>(result.Errors);
}

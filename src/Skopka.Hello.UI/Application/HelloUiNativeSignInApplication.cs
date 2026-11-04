using Microsoft.AspNetCore.Http;
using Skopka.Abstraction.OperationResult;

namespace Skopka.Hello.UI;

internal interface IHelloUiNativeSignInApplication
{
    Task<OperationResult<HelloNativeAppBeginResult>> BeginAsync(string clientId, string callback, string state, string challenge, string? returnUrl, HttpContext context, CancellationToken cancellationToken);
    Task<OperationResult<HelloNativeAppAttachResult>> AttachAsync(string flowId, string secret, HttpContext context, CancellationToken cancellationToken);
    Task<OperationResult<HelloNativeAppBrowserDetails>> DetailsAsync(string flowId, string binder, HttpContext context, CancellationToken cancellationToken);
    Task<OperationResult<HelloNativeAppConfirmResult>> ConfirmAsync(string flowId, string binder, string token, Guid sessionId, bool approve, HttpContext context, CancellationToken cancellationToken);
    Task<OperationResult<HelloNativeAppStatus>> StatusAsync(string flowId, string binder, HttpContext context, CancellationToken cancellationToken);
    Task<OperationResult<(HelloUiSignIn SignIn, string? ReturnUrl)>> CompleteAsync(string flowId, string binder, string verifier, string receipt, HttpContext context, CancellationToken cancellationToken);
    Task<OperationResult> CancelAsync(string flowId, string binder, HttpContext context, CancellationToken cancellationToken);
}

internal sealed class HelloUiNativeSignInApplication<TProfile>(IHelloNativeAppSignInApplication<TProfile> application, IHelloRequestContext requestContext, SkopkaHelloOptions options, IHelloUiProfileFactory<TProfile> profiles) : IHelloUiNativeSignInApplication
{
    private string? Key(HttpContext context) => requestContext.CreateClientKey(context);
    public Task<OperationResult<HelloNativeAppBeginResult>> BeginAsync(string clientId, string callback, string state, string challenge, string? returnUrl, HttpContext context, CancellationToken cancellationToken) => application.BeginAsync(new(clientId, callback, state, challenge, returnUrl, requestContext.CreateSessionMetadata(context, options.ClientName), Key(context)), cancellationToken);
    public Task<OperationResult<HelloNativeAppAttachResult>> AttachAsync(string flowId, string secret, HttpContext context, CancellationToken cancellationToken) => application.AttachAsync(flowId, secret, Key(context), cancellationToken);
    public Task<OperationResult<HelloNativeAppBrowserDetails>> DetailsAsync(string flowId, string binder, HttpContext context, CancellationToken cancellationToken) => application.GetBrowserDetailsAsync(flowId, binder, Key(context), cancellationToken);
    public Task<OperationResult<HelloNativeAppConfirmResult>> ConfirmAsync(string flowId, string binder, string token, Guid sessionId, bool approve, HttpContext context, CancellationToken cancellationToken) => application.ConfirmAsync(flowId, binder, token, sessionId, approve, Key(context), cancellationToken);
    public Task<OperationResult<HelloNativeAppStatus>> StatusAsync(string flowId, string binder, HttpContext context, CancellationToken cancellationToken) => application.GetStatusAsync(flowId, binder, Key(context), cancellationToken);
    public Task<OperationResult> CancelAsync(string flowId, string binder, HttpContext context, CancellationToken cancellationToken) => application.CancelAsync(flowId, binder, Key(context), cancellationToken);
    public async Task<OperationResult<(HelloUiSignIn SignIn, string? ReturnUrl)>> CompleteAsync(string flowId, string binder, string verifier, string receipt, HttpContext context, CancellationToken cancellationToken)
    {
        var result = await application.CompleteAsync(flowId, binder, verifier, receipt, Key(context), cancellationToken);
        if (!result.IsSuccess) return OperationResultFactory.Fail<(HelloUiSignIn, string?)>(result.Errors);
        var signIn = result.Value.SignIn;
        return OperationResultFactory.Success((new HelloUiSignIn(HelloUiPrincipalFactory.Create(signIn.Account, signIn.Session.SessionId, profiles), signIn.Session), result.Value.ReturnUrl));
    }
}

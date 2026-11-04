using Skopka.Abstraction.OperationResult;
using Skopka.Identity.Sessions;

namespace Skopka.Hello;

public sealed class HelloNativeAppSignInOptions
{
    public bool Enabled { get; set; }
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromMinutes(10);
    public TimeSpan LaunchLifetime { get; set; } = TimeSpan.FromMinutes(1);
    public TimeSpan ApprovalLifetime { get; set; } = TimeSpan.FromMinutes(1);
    public TimeSpan RetentionAfterExpiration { get; set; } = TimeSpan.FromDays(1);
    public int CleanupBatchSize { get; set; } = 500;
    public int BeginClientPermitLimit { get; set; } = 5;
    public TimeSpan BeginClientWindow { get; set; } = TimeSpan.FromMinutes(5);
    public int BeginApplicationPermitLimit { get; set; } = 500;
    public TimeSpan BeginApplicationWindow { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan StatusMinimumInterval { get; set; } = TimeSpan.FromSeconds(2);
    public Dictionary<string, string[]> AllowedCallbacks { get; set; } = new(StringComparer.Ordinal);
}

public sealed record HelloNativeAppBeginCommand(
    string ClientId,
    string Callback,
    string State,
    string CodeChallenge,
    string? ReturnUrl,
    IdentitySessionMetadata SessionMetadata,
    string? ClientKey = null);

public sealed record HelloNativeAppBeginResult(
    string FlowId,
    string WebViewBinder,
    string LaunchSecret,
    DateTimeOffset ExpiresAt);

public sealed record HelloNativeAppAttachResult(
    string FlowId,
    string BrowserBinder,
    string ClientDisplayName,
    DateTimeOffset ExpiresAt);

public enum HelloNativeAppSignInState
{
    Pending,
    BrowserAttached,
    Approved,
    Consuming,
    Completed,
    Cancelled,
    Expired,
}

public sealed record HelloNativeAppBrowserDetails(
    string ClientDisplayName,
    DateTimeOffset ExpiresAt);

public sealed record HelloNativeAppConfirmResult(
    string Callback,
    string State,
    string? CompletionReceipt);

public sealed record HelloNativeAppStatus(
    HelloNativeAppSignInState State,
    DateTimeOffset ExpiresAt);

public sealed record HelloNativeAppCompleted<TProfile>(
    HelloSignIn<TProfile> SignIn,
    string? ReturnUrl);

public interface IHelloNativeAppSignInApplication<TProfile>
{
    Task<OperationResult<HelloNativeAppBeginResult>> BeginAsync(
        HelloNativeAppBeginCommand command,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloNativeAppAttachResult>> AttachAsync(
        string flowId,
        string launchSecret,
        string? clientKey,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloNativeAppBrowserDetails>> GetBrowserDetailsAsync(
        string flowId,
        string browserBinder,
        string? clientKey,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloNativeAppConfirmResult>> ConfirmAsync(
        string flowId,
        string browserBinder,
        string accessToken,
        Guid sessionId,
        bool approve,
        string? clientKey,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloNativeAppStatus>> GetStatusAsync(
        string flowId,
        string webViewBinder,
        string? clientKey,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloNativeAppCompleted<TProfile>>> CompleteAsync(
        string flowId,
        string webViewBinder,
        string codeVerifier,
        string completionReceipt,
        string? clientKey,
        CancellationToken cancellationToken);

    Task<OperationResult> CancelAsync(
        string flowId,
        string webViewBinder,
        string? clientKey,
        CancellationToken cancellationToken);
}

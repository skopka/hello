using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using OpenIddict.Abstractions;
using Skopka.Abstraction.OperationResult;
using Skopka.Identity.RateLimiting;
using Skopka.Identity.Errors;
using Skopka.Identity.Sessions;
using Skopka.Identity.Users;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Skopka.Hello.AuthorizationServer;

internal sealed class HelloNativeAppSignInApplication<TProfile>(
    HelloNativeAppSignInOptions options, IHelloNativeAppFlowStore store,
    IOpenIddictApplicationManager clients, IIdentitySessionService<TProfile> sessions,
    IIdentitySessionRegistry<TProfile> registry, IEnumerable<IHelloAccessTokenValidator<TProfile>> validators,
    IIdentityRateLimiter<TProfile> limiter, IDataProtectionProvider protection,
    IHelloSecurityEventSink audit, TimeProvider? timeProvider = null) : IHelloNativeAppSignInApplication<TProfile>
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private DateTimeOffset? retryAfter;
    private bool rateDenied;
    private readonly IDataProtector protector = protection.CreateProtector("Skopka.Hello.NativeApp.Source.v1");
    private sealed record Source(Guid UserId, Guid SessionId, string Stamp);
    private static string RandomValue() => Base64UrlEncoder(RandomNumberGenerator.GetBytes(32));
    private static string Base64UrlEncoder(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+',
        '-').Replace('/',
        '_');
    private static string Hash(string value) => Base64UrlEncoder(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static bool Match(string value, string? hash) => hash is not null
        && value.Length <= 256
        && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash(value)), Encoding.ASCII.GetBytes(hash));
    private static bool IsLocal(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 2048 || value.Any(char.IsControl))
        {
            return false;
        }

        if (value[0] == '/')
        {
            return value.Length == 1 || value[1] is not ('/' or '\\');
        }

        return value.Length > 1
            && value[0] == '~'
            && value[1] == '/'
            && (value.Length == 2 || value[2] is not ('/' or '\\'));
    }
    private static bool Challenge(string? value)
    {
        if (value is null || value.Length != 43 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
        {
            return false;
        }
        try
        {
            return Base64UrlEncoder(Convert.FromBase64String(
                value.Replace('-', '+').Replace('_', '/') + "=")) == value;
        }
        catch (FormatException)
        {
            return false;
        }
    }
    private static bool Token(string? value, int min, int max) => value is not null
        && value.Length >= min
        && value.Length <= max
        && value.All(c => char.IsAsciiLetterOrDigit(c)
        || c is '-' or '_' or '.' or '~');
    private static Error Invalid() => new("hello.native_app.invalid",
        "The sign-in request is invalid or expired.",
        ErrorType.Validation);
    private Error Failure(string operation)
    {
        Audit(rateDenied ? "rate_limited" : "rejected", operation: operation);
        return rateDenied ? new Error(IdentityErrorCodes.RateLimitExceeded,
            "Too many requests.",
            ErrorType.Forbidden,
            new RateLimitDetails(retryAfter)) : Invalid();
    }
    private OperationResult<T> Fail<T>([CallerMemberName] string operation = "")
        => OperationResultFactory.Fail<T>(Failure(operation));
    private OperationResult Fail([CallerMemberName] string operation = "")
        => OperationResultFactory.Fail(Failure(operation));
    private async Task<bool> Rate(string scope,
        string? key,
        int limit,
        TimeSpan window,
        CancellationToken cancellationToken,
        TimeSpan? spacing = null)
    {
        var decision = await limiter.HitAsync(new RateLimitRequest("hello.native_app." + scope,
            string.IsNullOrWhiteSpace(key) ? "unavailable" : key[..Math.Min(key.Length,
            128)],
            limit,
            window,
            spacing),
            cancellationToken);
        if (!decision.IsAllowed)
        {
            rateDenied = true;
            retryAfter = decision.RetryAfter;
        }
        return decision.IsAllowed;
    }
    private async Task<(long Revision, string Name)?> Client(string id, string callback, CancellationToken cancellationToken)
    {
        if (!options.Enabled
            || !options.AllowedCallbacks.TryGetValue(id, out var allowed)
            || !allowed.Contains(callback, StringComparer.Ordinal))
        {
            return null;
        }
        var app = await clients.FindByClientIdAsync(id, cancellationToken);
        if (app is null)
        {
            return null;
        }
        var descriptor = await HelloClientRegistration.DescribeAsync(clients, app, cancellationToken);
        var metadata = HelloClientRegistration.Read(descriptor);
        if (metadata is null
            || descriptor.ClientType != ClientTypes.Public
            || !descriptor.RedirectUris.Any(x => x.OriginalString == callback))
        {
            return null;
        }
        return (metadata.Revision, descriptor.DisplayName ?? id);
    }
    private async Task<HelloNativeAppFlow?> Read(string id, CancellationToken cancellationToken)
        => Token(id, 43, 43) ? await store.FindAsync(id, cancellationToken) : null;
    private bool Live(HelloNativeAppFlow? flow) => flow is not null && flow.ExpiresAt > clock.GetUtcNow();
    private async Task<bool> Current(HelloNativeAppFlow flow, CancellationToken cancellationToken)
        => (await Client(flow.ClientId, flow.Callback, cancellationToken))?.Revision == flow.ClientRevision;
    private async Task<bool> Save(HelloNativeAppFlow flow,
        CancellationToken cancellationToken) => await store.ReplaceAsync(flow,
        flow.Version,
        cancellationToken);
    private void Audit(string type, Guid? user = null, string? operation = null, string? clientId = null)
    {
        try
        {
            var metadata = new Dictionary<string, string>();
            if (operation is not null) metadata["operation"] = operation;
            if (clientId is not null) metadata["client_id"] = clientId;
            audit.Write(new HelloSecurityEventEnvelope(
                Guid.NewGuid(), "hello.native_app." + type, user, user, null, null,
                clock.GetUtcNow(), metadata)
            {
                DeliveryStage = HelloSecurityEventDeliveryStage.AfterHelloCommit,
            });
        }
        catch
        {
            // Audit is post-commit and cannot reverse the operation.
        }
    }

    public async Task<OperationResult<HelloNativeAppBeginResult>> BeginAsync(HelloNativeAppBeginCommand command,
        CancellationToken cancellationToken)
    {
        rateDenied = false;
        retryAfter = null;
        if (!options.Enabled
            || command is null
            || !Challenge(command.CodeChallenge)
            || (string.IsNullOrEmpty(command.State)
            || command.State.Length > 1024
            || command.State.Any(char.IsControl))
            || command.ClientId is null
            || command.ClientId.Length > 200
            || command.Callback is null
            || command.Callback.Length > 2048
            || (command.ReturnUrl is not null
            && !IsLocal(command.ReturnUrl)))
        {
            return Fail<HelloNativeAppBeginResult>();
        }
        if (!await Rate("begin", command.ClientKey, options.BeginClientPermitLimit,
                options.BeginClientWindow, cancellationToken))
        {
            return Fail<HelloNativeAppBeginResult>();
        }

        var client = await Client(command.ClientId, command.Callback, cancellationToken);
        if (client is null)
        {
            return Fail<HelloNativeAppBeginResult>();
        }

        if (!await Rate("global", command.ClientId, options.BeginApplicationPermitLimit,
                options.BeginApplicationWindow, cancellationToken))
        {
            return Fail<HelloNativeAppBeginResult>();
        }
        var now = clock.GetUtcNow();
        var binder = RandomValue();
        var launch = RandomValue();
        var flow = new HelloNativeAppFlow
        {
            Id = RandomValue(),
            ClientId = command.ClientId,
            ClientRevision = client.Value.Revision,
            Callback = command.Callback,
            State = command.State,
            Challenge = command.CodeChallenge,
            WebViewBinderHash = Hash(binder),
            LaunchSecretHash = Hash(launch),
            ReturnUrl = command.ReturnUrl,
            SessionMetadata = JsonSerializer.Serialize(command.SessionMetadata),
            Status = HelloNativeAppSignInState.Pending,
            CreatedAt = now,
            ExpiresAt = now + options.Lifetime,
            LaunchExpiresAt = now + options.LaunchLifetime
        };
        await store.AddAsync(flow, cancellationToken);
        Audit("begun", operation: nameof(BeginAsync), clientId: flow.ClientId);
        return OperationResultFactory.Success(new HelloNativeAppBeginResult(flow.Id, binder, launch, flow.ExpiresAt));
    }
    public async Task<OperationResult<HelloNativeAppAttachResult>> AttachAsync(string flowId,
        string launchSecret,
        string? clientKey,
        CancellationToken cancellationToken)
    {
        rateDenied = false;
        retryAfter = null;
        if (!Token(launchSecret, 43, 43) || !await Rate("attach", clientKey, 30, TimeSpan.FromMinutes(5), cancellationToken))
        {
            return Fail<HelloNativeAppAttachResult>();
        }
        var flow = await Read(flowId, cancellationToken);
        if (!Live(flow)
            || flow!.Status != HelloNativeAppSignInState.Pending
            || flow.LaunchExpiresAt <= clock.GetUtcNow()
            || !Match(launchSecret, flow.LaunchSecretHash))
        {
            return Fail<HelloNativeAppAttachResult>();
        }
        var client = await Client(flow.ClientId, flow.Callback, cancellationToken);
        if (client is null || client.Value.Revision != flow.ClientRevision)
        {
            return Fail<HelloNativeAppAttachResult>();
        }
        var binder = RandomValue();
        flow.BrowserBinderHash = Hash(binder);
        flow.LaunchSecretHash = "";
        flow.Status = HelloNativeAppSignInState.BrowserAttached;
        if (!Live(flow) || flow.LaunchExpiresAt <= clock.GetUtcNow() || !await Save(flow, cancellationToken))
        {
            return Fail<HelloNativeAppAttachResult>();
        }
        Audit("attached", operation: nameof(AttachAsync), clientId: flow.ClientId);
        return OperationResultFactory.Success(new HelloNativeAppAttachResult(flow.Id, binder, client.Value.Name, flow.ExpiresAt));
    }
    public async Task<OperationResult<HelloNativeAppBrowserDetails>> GetBrowserDetailsAsync(string flowId,
        string browserBinder,
        string? clientKey,
        CancellationToken cancellationToken)
    {
        rateDenied = false;
        retryAfter = null;
        if (!Token(browserBinder, 43, 43) || !await Rate("details", clientKey, 120, TimeSpan.FromMinutes(5), cancellationToken))
        {
            return Fail<HelloNativeAppBrowserDetails>();
        }
        var flow = await Read(flowId, cancellationToken);
        if (!Live(flow) || flow!.Status != HelloNativeAppSignInState.BrowserAttached || !Match(browserBinder, flow.BrowserBinderHash))
        {
            return Fail<HelloNativeAppBrowserDetails>();
        }
        var client = await Client(flow.ClientId, flow.Callback, cancellationToken);
        return client is not null
            && client.Value.Revision == flow.ClientRevision ? OperationResultFactory.Success(new HelloNativeAppBrowserDetails(client.Value.Name,
                flow.ExpiresAt)) : Fail<HelloNativeAppBrowserDetails>();
    }
    public async Task<OperationResult<HelloNativeAppConfirmResult>> ConfirmAsync(string flowId,
        string browserBinder,
        string accessToken,
        Guid sessionId,
        bool approve,
        string? clientKey,
        CancellationToken cancellationToken)
    {
        rateDenied = false;
        retryAfter = null;
        if (!Token(browserBinder, 43, 43) || !await Rate("confirm", clientKey, 30, TimeSpan.FromMinutes(5), cancellationToken))
        {
            return Fail<HelloNativeAppConfirmResult>();
        }
        var flow = await Read(flowId, cancellationToken);
        if (!Live(flow)
            || flow!.Status != HelloNativeAppSignInState.BrowserAttached
            || !Match(browserBinder, flow.BrowserBinderHash)
            || !await Current(flow, cancellationToken))
        {
            return Fail<HelloNativeAppConfirmResult>();
        }
        OperationResult<IdentityUser<TProfile>>? user = null;
        foreach (var validator in validators)
        {
            var validation = await validator.ValidateAsync(accessToken, cancellationToken);
            if (validation.IsSuccess)
            {
                user = validation;
                break;
            }
        }
        if (user is null)
        {
            return Fail<HelloNativeAppConfirmResult>();
        }
        var source = await registry.ValidateAsync(new ValidateIdentitySessionCommand(user.Value.Id, sessionId), cancellationToken);
        if (!source.IsSuccess || source.Value.SecurityStamp != user.Value.SecurityStamp || !Live(flow))
        {
            return Fail<HelloNativeAppConfirmResult>();
        }
        flow.Status = approve ? HelloNativeAppSignInState.Approved : HelloNativeAppSignInState.Cancelled;
        string? completionReceipt = null;
        if (approve)
        {
            completionReceipt = RandomValue();
            flow.CompletionReceiptHash = Hash(completionReceipt);
            flow.ProtectedSource = protector.Protect(JsonSerializer.Serialize(
                new Source(user.Value.Id, sessionId, user.Value.SecurityStamp)));
            flow.ApprovalExpiresAt = clock.GetUtcNow() + options.ApprovalLifetime;
        }
        if (!await Save(flow, cancellationToken))
        {
            return Fail<HelloNativeAppConfirmResult>();
        }
        Audit(approve ? "approved" : "denied", user.Value.Id, nameof(ConfirmAsync), flow.ClientId);
        return OperationResultFactory.Success(new HelloNativeAppConfirmResult(flow.Callback, flow.State, completionReceipt));
    }
    public async Task<OperationResult<HelloNativeAppStatus>> GetStatusAsync(string flowId,
        string webViewBinder,
        string? clientKey,
        CancellationToken cancellationToken)
    {
        rateDenied = false;
        retryAfter = null;
        if (!Token(flowId, 43, 43)
            || !Token(webViewBinder, 43, 43)
            || !await Rate("status", flowId, 300, TimeSpan.FromMinutes(10), cancellationToken, options.StatusMinimumInterval)
            || !await Rate("status_client", clientKey, 500, TimeSpan.FromMinutes(10), cancellationToken))
        {
            return Fail<HelloNativeAppStatus>();
        }
        var flow = await Read(flowId, cancellationToken);
        if (flow is null || !Match(webViewBinder, flow.WebViewBinderHash))
        {
            return Fail<HelloNativeAppStatus>();
        }
        return OperationResultFactory.Success(new HelloNativeAppStatus(!Live(flow)
            || flow.Status == HelloNativeAppSignInState.Approved
            && flow.ApprovalExpiresAt <= clock.GetUtcNow() ? HelloNativeAppSignInState.Expired : flow.Status, flow.ExpiresAt));
    }
    public async Task<OperationResult<HelloNativeAppCompleted<TProfile>>> CompleteAsync(string flowId,
        string webViewBinder,
        string codeVerifier,
        string completionReceipt,
        string? clientKey,
        CancellationToken cancellationToken)
    {
        rateDenied = false;
        retryAfter = null;
        if (!Token(webViewBinder, 43, 43)
            || !Token(codeVerifier, 43, 128)
            || !Challenge(completionReceipt)
            || !await Rate("complete", clientKey, 30, TimeSpan.FromMinutes(5), cancellationToken))
        {
            return Fail<HelloNativeAppCompleted<TProfile>>();
        }
        var flow = await Read(flowId, cancellationToken);
        if (!Live(flow)
            || flow!.Status != HelloNativeAppSignInState.Approved
            || flow.ApprovalExpiresAt <= clock.GetUtcNow()
            || !Match(webViewBinder, flow.WebViewBinderHash)
            || !Match(codeVerifier, flow.Challenge)
            || !Match(completionReceipt, flow.CompletionReceiptHash)
            || !await Current(flow, cancellationToken)
            || flow.ProtectedSource is null)
        {
            return Fail<HelloNativeAppCompleted<TProfile>>();
        }
        Source? source;
        try
        {
            source = JsonSerializer.Deserialize<Source>(
                protector.Unprotect(flow.ProtectedSource));
        }
        catch
        {
            return Fail<HelloNativeAppCompleted<TProfile>>();
        }
        if (source is null)
        {
            return Fail<HelloNativeAppCompleted<TProfile>>();
        }
        var user = await registry.ValidateWithAssuranceAsync(new ValidateIdentitySessionCommand(source.UserId, source.SessionId), cancellationToken);
        if (!user.IsSuccess || user.Value.User.SecurityStamp != source.Stamp || !Live(flow) || flow.ApprovalExpiresAt <= clock.GetUtcNow())
        {
            return Fail<HelloNativeAppCompleted<TProfile>>();
        }
        flow.Status = HelloNativeAppSignInState.Consuming;
        flow.ProtectedSource = null;
        flow.CompletionReceiptHash = null;
        if (!await Save(flow, cancellationToken))
        {
            return Fail<HelloNativeAppCompleted<TProfile>>();
        }
        var metadata = JsonSerializer.Deserialize<IdentitySessionMetadata>(flow.SessionMetadata);
        var issued = await sessions.CreateAsync(new CreateIdentitySessionCommand(user.Value.User.Id,
            user.Value.User.SecurityStamp,
            metadata)
        { Assurance = user.Value.Assurance, Context = user.Value.Context },
            cancellationToken);
        if (!issued.IsSuccess)
        {
            Audit("rejected", operation: nameof(CompleteAsync), clientId: flow.ClientId);
            return OperationResultFactory.Fail<HelloNativeAppCompleted<TProfile>>(issued.Errors);
        }
        try
        {
            flow.Status = HelloNativeAppSignInState.Completed;
            if (!await Save(flow, cancellationToken))
            {
                await sessions.RevokeByIdAsync(
                    new RevokeIdentitySessionByIdCommand(user.Value.User.Id, issued.Value.SessionId),
                    CancellationToken.None);
                return Fail<HelloNativeAppCompleted<TProfile>>();
            }
        }
        catch
        {
            await sessions.RevokeByIdAsync(
                new RevokeIdentitySessionByIdCommand(user.Value.User.Id, issued.Value.SessionId),
                CancellationToken.None);
            throw;
        }
        var accountUser = user.Value.User;
        var issuedSession = issued.Value;
        Audit("completed", accountUser.Id, nameof(CompleteAsync), flow.ClientId);
        return OperationResultFactory.Success(new HelloNativeAppCompleted<TProfile>(new HelloSignIn<TProfile>(new HelloAccount<TProfile>(accountUser.Id,
            accountUser.Flags,
            accountUser.UserName,
            accountUser.Email,
            accountUser.EmailConfirmed,
            accountUser.Phone,
            accountUser.PhoneConfirmed,
            accountUser.Profile,
            accountUser.Version,
            accountUser.CreatedAt,
            accountUser.ModifiedAt),
            new HelloSession(issuedSession.SessionId,
            issuedSession.AccessToken,
            issuedSession.AccessTokenExpiresAt,
            issuedSession.RefreshToken,
            issuedSession.RefreshTokenExpiresAt)),
            flow.ReturnUrl));
    }
    public async Task<OperationResult> CancelAsync(string flowId,
        string webViewBinder,
        string? clientKey,
        CancellationToken cancellationToken)
    {
        rateDenied = false;
        retryAfter = null;
        if (!Token(webViewBinder, 43, 43) || !await Rate("cancel", clientKey, 30, TimeSpan.FromMinutes(5), cancellationToken))
        {
            return Fail();
        }
        var flow = await Read(flowId, cancellationToken);
        if (!Live(flow)
            || !Match(webViewBinder, flow!.WebViewBinderHash)
            || flow.Status is not (HelloNativeAppSignInState.Pending or HelloNativeAppSignInState.BrowserAttached or HelloNativeAppSignInState.Approved))
        {
            return Fail();
        }
        flow.Status = HelloNativeAppSignInState.Cancelled;
        flow.ProtectedSource = null;
        flow.CompletionReceiptHash = null;
        if (!await Save(flow, cancellationToken))
        {
            return Fail();
        }
        Audit("cancelled", operation: nameof(CancelAsync), clientId: flow.ClientId);
        return OperationResultFactory.Success();
    }
}

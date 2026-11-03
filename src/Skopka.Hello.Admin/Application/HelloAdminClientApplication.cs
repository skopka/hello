using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Skopka.Abstraction.OperationResult;
using Skopka.Hello.AuthorizationServer;
using Skopka.Identity.Errors;
using Skopka.Identity.Sessions;
using Skopka.Identity.StepUp;
using Skopka.Identity.StepUp.Commands;
using Skopka.Identity.Verification;
using Skopka.Identity.Users;

namespace Skopka.Hello.Admin;

internal sealed partial class HelloAdminClientApplication<TProfile>(
    IHelloAuthorizationClientManagement clients,
    IIdentitySessionService<TProfile> sessions,
    IIdentityStepUpService<TProfile> stepUp,
    IIdentityVerificationService<TProfile> verification,
    IHelloAccountMessageSender sender,
    HelloDeliveryOptions delivery,
    IHelloSecurityEventSink events,
    IHttpContextAccessor context,
    ILogger<HelloAdminClientApplication<TProfile>> logger,
    HelloStepUpMethodResolver<TProfile>? resolver = null)
    : IHelloAdminClientApplication
{
    private readonly HelloStepUpMethodResolver<TProfile> methods =
        resolver ?? new(delivery, sender);

    public async Task<OperationResult<IReadOnlyList<HelloAuthorizationClientView>>> ListAsync(
        string token,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        var actor = await sessions.ValidateAccessTokenAsync(token, cancellationToken);
        return actor.IsSuccess ? await clients.ListAsync(offset, count, cancellationToken) : OperationResultFactory.Fail<IReadOnlyList<HelloAuthorizationClientView>>(actor.Errors);
    }

    public async Task<OperationResult<HelloStepUpChallenge>> BeginAsync(
        string token,
        HelloAdminClientAction action,
        string? clientKey,
        CancellationToken cancellationToken)
    {
        if (!Valid(action))
        {
            return Invalid<HelloStepUpChallenge>();
        }
        var actor = await sessions.ValidateAccessTokenAsync(token, cancellationToken);
        if (!actor.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloStepUpChallenge>(actor.Errors);
        }
        var preflight = await PreflightAsync(action, cancellationToken);
        if (!preflight.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloStepUpChallenge>(preflight.Errors);
        }
        var selected = await methods.SelectAsync(actor.Value, cancellationToken);
        if (!selected.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloStepUpChallenge>(selected.Errors);
        }
        var issued = await stepUp.BeginAsync(
            new BeginStepUpCommand(
                actor.Value.Id,
                ActionName(action),
                Binding(actor.Value.Id, action, selected.Value),
                selected.Value.Method,
                clientKey),
            cancellationToken);
        if (!issued.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloStepUpChallenge>(issued.Errors);
        }
        if (selected.Value.Channel != HelloDeliveryChannel.Authenticator)
        {
            if (string.IsNullOrWhiteSpace(issued.Value.DeliveryCode) || string.IsNullOrWhiteSpace(selected.Value.Destination))
            {
                return Invalid<HelloStepUpChallenge>();
            }
            var sent = await sender.SendAsync(
                new HelloAccountMessage(
                    Guid.NewGuid(),
                    HelloAccountMessageKind.AdminActionVerification,
                    selected.Value.Channel,
                    selected.Value.Destination,
                    null,
                    issued.Value.ExpiresAt,
                    issued.Value.DeliveryCode),
                cancellationToken);
            if (!sent.IsSuccess)
            {
                return OperationResultFactory.Fail<HelloStepUpChallenge>(sent.Errors);
            }
        }
        else if (!string.IsNullOrWhiteSpace(issued.Value.DeliveryCode))
        {
            return Invalid<HelloStepUpChallenge>();
        }

        return OperationResultFactory.Success(new HelloStepUpChallenge(
            issued.Value.ChallengeId,
            issued.Value.ExpiresAt,
            selected.Value.Channel));
    }

    public async Task<OperationResult<HelloAuthorizationClientMutation>> CompleteAsync(
        string token,
        HelloAdminClientCompletion completion,
        string? clientKey,
        CancellationToken cancellationToken)
    {
        if (completion is null)
        {
            return Invalid<HelloAuthorizationClientMutation>();
        }
        var action = completion.Action;
        if (!Valid(action))
        {
            return Invalid<HelloAuthorizationClientMutation>();
        }
        var actor = await sessions.ValidateAccessTokenAsync(token, cancellationToken);
        if (!actor.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloAuthorizationClientMutation>(actor.Errors);
        }
        var verified = await verification.VerifyAsync(
            new VerifyVerificationChallengeCommand(
                completion.ChallengeId, actor.Value.Id,
                completion.VerificationCode, clientKey),
            cancellationToken);
        if (!verified.IsSuccess)
        {
            return verified.Errors.Count == 1
                && verified.Errors.First().Code == IdentityErrorCodes.VerificationResponseInvalid
                ? OperationResultFactory.Fail<HelloAuthorizationClientMutation>(verified.Errors)
                : Restart<HelloAuthorizationClientMutation>(verified.Errors);
        }
        var selected = methods.Resolve(actor.Value, verified.Value.Method, requireDelivery: false);
        if (!selected.IsSuccess)
        {
            return Restart<HelloAuthorizationClientMutation>(selected.Errors);
        }
        var authorized = await stepUp.AuthorizeAsync(
            new AuthorizeStepUpCommand(
                actor.Value.Id,
                ActionName(action),
                Binding(actor.Value.Id, action, selected.Value),
                completion.ChallengeId,
                verified.Value.Token),
            cancellationToken);
        if (!authorized.IsSuccess)
        {
            return Restart<HelloAuthorizationClientMutation>(authorized.Errors);
        }
        OperationResult<HelloAuthorizationClientMutation> result;
        if (action.Action == "delete")
        {
            var deleted = await clients.DeleteAsync(action.Input.ClientId, action.ExpectedRevision!.Value, cancellationToken);
            result = deleted.IsSuccess
                ? OperationResultFactory.Success(new HelloAuthorizationClientMutation(
                    new HelloAuthorizationClientView(
                        action.Input.ClientId,
                        action.Input.DisplayName,
                        action.Input.Type,
                        action.Input.Resource,
                        action.Input.RedirectUris,
                        action.Input.PostLogoutRedirectUris,
                        action.Input.Scopes,
                        action.Input.HonorReauthenticationPrompts,
                        "administration",
                        action.ExpectedRevision.Value),
                    Secret: null))
                : OperationResultFactory.Fail<HelloAuthorizationClientMutation>(deleted.Errors);
        }
        else
        {
            result = action.Action switch
            {
                "create" => await clients.CreateAsync(action.Input, cancellationToken),
                "update" => await clients.UpdateAsync(
                    action.Input.ClientId, action.ExpectedRevision!.Value,
                    action.Input, cancellationToken),
                "rotate" => await clients.RotateSecretAsync(
                    action.Input.ClientId, action.ExpectedRevision!.Value,
                    cancellationToken),
                _ => throw new InvalidOperationException(),
            };
        }
        if (!result.IsSuccess)
        {
            return Restart<HelloAuthorizationClientMutation>(result.Errors);
        }
        try
        {
            var audit = events.Write(new HelloSecurityEventEnvelope(
                Guid.NewGuid(),
                "hello.admin.client." + action.Action,
                SubjectUserId: null,
                actor.Value.Id,
                null,
                context.HttpContext?.TraceIdentifier,
                DateTimeOffset.UtcNow,
                new Dictionary<string, string>
                {
                    ["clientId"] = action.Input.ClientId,
                    ["revision"] = result.Value.Client.Revision.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                }));
            if (!audit.IsSuccess)
            {
                AuditFailed(logger, null);
            }
        }
        catch (Exception exception)
        {
            AuditFailed(logger, exception);
        }
        return result;
    }

    private async Task<OperationResult<bool>> PreflightAsync(
        HelloAdminClientAction action,
        CancellationToken cancellationToken)
    {
        if (action.Action == "create")
        {
            return await clients.ValidateCreateAsync(action.Input, cancellationToken);
        }
        if (action.Action == "update")
        {
            return await clients.ValidateUpdateAsync(
                action.Input.ClientId, action.ExpectedRevision!.Value,
                action.Input, cancellationToken);
        }
        var target = await clients.GetAsync(action.Input.ClientId, cancellationToken);
        if (!target.IsSuccess)
        {
            return OperationResultFactory.Fail<bool>(target.Errors);
        }
        if (target.Value.Ownership != "administration")
        {
            return OperationResultFactory.Fail<bool>(new Error(
                "authorization_client_read_only", "Client is read-only.", ErrorType.Forbidden));
        }
        if (target.Value.Revision != action.ExpectedRevision)
        {
            return OperationResultFactory.Fail<bool>(new Error(
                "authorization_client_conflict", "Client has changed.", ErrorType.Conflict));
        }
        if (action.Action == "rotate"
            && target.Value.Type != HelloAuthorizationClientType.Confidential)
        {
            return Invalid<bool>();
        }
        return OperationResultFactory.Success(true);
    }
    [LoggerMessage(Level = LogLevel.Warning, Message = "Client administration audit event failed after the mutation committed.")]
    private static partial void AuditFailed(ILogger logger, Exception? exception);
    private static bool Valid(HelloAdminClientAction? action)
    {
        if (action?.Input is not { } input
            || string.IsNullOrWhiteSpace(input.ClientId)
            || input.ClientId.Length > 512
            || input.DisplayName is null || input.DisplayName.Length > 512
            || input.Resource is null || input.Resource.Length > 2048
            || !Bounded(input.RedirectUris) || !Bounded(input.PostLogoutRedirectUris)
            || !Bounded(input.Scopes))
        {
            return false;
        }

        return action.Action == "create"
            ? action.ExpectedRevision is null
            : action.Action is "update" or "delete" or "rotate"
                && action.ExpectedRevision > 0;
    }

    private static bool Bounded(IReadOnlyList<string>? values)
        => values is not null && values.Count <= 100
            && values.All(value => value is not null && value.Length <= 2048);

    private static string ActionName(HelloAdminClientAction action) => "admin.client." + action.Action;
    private static string Binding(
        Guid actor,
        HelloAdminClientAction action,
        HelloStepUpMethodSelection selection)
    {
        var value = JsonSerializer.Serialize(new
        {
            actor,
            action,
            selection.Channel,
            Destination = selection.Destination ?? "totp",
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private static OperationResult<T> Invalid<T>()
        => OperationResultFactory.Fail<T>(new Error(
            IdentityErrorCodes.Validation,
            "Invalid client action.",
            ErrorType.Validation));
    private static OperationResult<T> Restart<T>(IReadOnlyCollection<Error> errors)
        => OperationResultFactory.Fail<T>(
            errors.Prepend(HelloAdminSecurity.RestartRequired()).ToArray());
}

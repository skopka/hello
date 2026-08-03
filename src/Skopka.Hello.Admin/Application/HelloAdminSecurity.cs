using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Skopka.Abstraction.OperationResult;
using Skopka.Identity.Errors;
using Skopka.Identity.StepUp;
using Skopka.Identity.Verification;

namespace Skopka.Hello.Admin;

internal static class HelloAdminSecurity
{
    public const int MaximumReasonLength = 512;

    public const string BlockAction = "admin.user.block";
    public const string UnblockAction = "admin.user.unblock";
    public const string DeleteAction = "admin.user.delete";
    public const string RestoreAction = "admin.user.restore";
    public const string RevokeSessionsAction =
        "admin.user.sessions.revoke";

    public static string GetAction(HelloAdminUserAction action)
        => action switch
        {
            HelloAdminUserAction.Block => BlockAction,
            HelloAdminUserAction.Unblock => UnblockAction,
            HelloAdminUserAction.Delete => DeleteAction,
            HelloAdminUserAction.Restore => RestoreAction,
            HelloAdminUserAction.RevokeSessions =>
                RevokeSessionsAction,
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };

    public static bool TryParseActionSlug(
        string? value,
        out HelloAdminUserAction action)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            action = (HelloAdminUserAction)(-1);
            return false;
        }

        action = value.ToLowerInvariant() switch
        {
            "block" => HelloAdminUserAction.Block,
            "unblock" => HelloAdminUserAction.Unblock,
            "delete" => HelloAdminUserAction.Delete,
            "restore" => HelloAdminUserAction.Restore,
            "revoke-sessions" =>
                HelloAdminUserAction.RevokeSessions,
            _ => (HelloAdminUserAction)(-1),
        };
        return Enum.IsDefined(action);
    }

    public static string GetActionSlug(HelloAdminUserAction action)
        => action switch
        {
            HelloAdminUserAction.Block => "block",
            HelloAdminUserAction.Unblock => "unblock",
            HelloAdminUserAction.Delete => "delete",
            HelloAdminUserAction.Restore => "restore",
            HelloAdminUserAction.RevokeSessions => "revoke-sessions",
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };

    public static string CreateBinding(
        Guid actorUserId,
        Guid targetUserId,
        HelloAdminUserAction action,
        HelloAdminUserActionParameters parameters,
        HelloDeliveryChannel channel,
        string destination)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);

        var reason = parameters.Reason ?? string.Empty;
        var reasonHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(reason)));
        var destinationHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(destination)));
        var until = parameters.BlockedUntil?
            .ToUniversalTime()
            .ToString("O", CultureInfo.InvariantCulture)
            ?? "-";
        var version = parameters.ExpectedVersion?
            .ToString(CultureInfo.InvariantCulture)
            ?? "-";
        var value = string.Join(
            '|',
            "hello-admin-binding:v1",
            actorUserId.ToString("D"),
            targetUserId.ToString("D"),
            GetAction(action),
            version,
            until,
            reasonHash,
            ((int)channel).ToString(CultureInfo.InvariantCulture),
            destinationHash);
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    public static Error? Validate(
        Guid targetUserId,
        HelloAdminUserAction action,
        HelloAdminUserActionParameters parameters)
    {
        if (targetUserId == Guid.Empty)
        {
            return Validation("targetUserId", "A target user is required.");
        }

        if (!Enum.IsDefined(action))
        {
            return Validation("action", "The admin action is invalid.");
        }

        if (parameters.Reason?.Length > MaximumReasonLength)
        {
            return Validation(
                "reason",
                $"Reason cannot exceed {MaximumReasonLength} characters.");
        }

        var requiresVersion = action != HelloAdminUserAction.RevokeSessions;
        if (requiresVersion && parameters.ExpectedVersion is null)
        {
            return Validation(
                "expectedVersion",
                "ExpectedVersion is required for this action.");
        }

        if (!requiresVersion && parameters.ExpectedVersion is not null)
        {
            return Validation(
                "expectedVersion",
                "ExpectedVersion is not accepted for session revocation.");
        }

        if (action != HelloAdminUserAction.Block
            && parameters.BlockedUntil is not null)
        {
            return Validation(
                "blockedUntil",
                "BlockedUntil is accepted only for block actions.");
        }

        if (action == HelloAdminUserAction.Block
            && parameters.BlockedUntil <= DateTimeOffset.UtcNow)
        {
            return Validation(
                "blockedUntil",
                "BlockedUntil must be in the future.");
        }

        if (action is HelloAdminUserAction.Unblock
            or HelloAdminUserAction.Restore
            or HelloAdminUserAction.RevokeSessions
            && !string.IsNullOrEmpty(parameters.Reason))
        {
            return Validation(
                "reason",
                "Reason is not accepted for this action.");
        }

        return null;
    }

    public static Error SelfMutationForbidden()
        => new(
            HelloAdminErrorCodes.SelfMutationForbidden,
            "An administrator cannot block or delete their own account.",
            ErrorType.Forbidden);

    public static Error ConfirmedDestinationRequired(
        HelloDeliveryChannel channel)
        => new(
            HelloAdminErrorCodes.ConfirmedDestinationRequired,
            channel == HelloDeliveryChannel.Email
                ? "A confirmed administrator email address is required."
                : "A confirmed administrator phone number is required.",
            ErrorType.Forbidden);

    public static Error RestartRequired()
        => new(
            HelloAdminErrorCodes.RestartRequired,
            "The verification code can no longer be used. Request a new code and try again.",
            ErrorType.Conflict);

    public static Error SessionCleanupRequired()
        => new(
            HelloAdminErrorCodes.SessionCleanupRequired,
            "The user state changed, but active session cleanup did not complete.",
            ErrorType.Conflict);

    private static Error Validation(string field, string message)
        => new(
            IdentityErrorCodes.Validation,
            "Validation failed.",
            ErrorType.Validation,
            new ValidationDetails(
                new Dictionary<string, string[]>
                {
                    [field] = [message],
                }));
}

internal sealed class HelloAdminStepUpRequirementProvider<TProfile>
    : IHelloStepUpRequirementProvider<TProfile>
{
    private static readonly Dictionary<string, StepUpRequirement>
        Requirements = new(
            StringComparer.Ordinal)
        {
            [HelloAdminSecurity.BlockAction] = Create("block"),
            [HelloAdminSecurity.UnblockAction] = Create("unblock"),
            [HelloAdminSecurity.DeleteAction] = Create("delete"),
            [HelloAdminSecurity.RestoreAction] = Create("restore"),
            [HelloAdminSecurity.RevokeSessionsAction] =
                Create("sessions.revoke"),
        };

    public Task<StepUpRequirement?> GetRequirementAsync(
        StepUpAuthorizationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        Requirements.TryGetValue(context.Action, out var requirement);
        return Task.FromResult(requirement);
    }

    private static StepUpRequirement Create(string purposeSuffix)
        => new(
            $"hello:admin.user.{purposeSuffix}",
            [VerificationMethods.OneTimeCode],
            AssuranceLevel: 2,
            MaximumAge: TimeSpan.FromMinutes(5));
}

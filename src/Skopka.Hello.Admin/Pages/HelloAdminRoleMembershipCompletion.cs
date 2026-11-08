using Skopka.Hello.UI;

namespace Skopka.Hello.Admin.Pages;

/// <summary>
/// Completes a role assignment or removal the same way from the Users and the
/// Roles page, including duplicate submissions of one confirmation.
/// </summary>
internal static class HelloAdminRoleMembershipCompletion
{
    public static Task<HelloAdminCompletionOutcome> CompleteAsync(
        HelloAdminCompletionGuard guard,
        IHelloAdminRoleApplication roles,
        IHelloUiLocalizer text,
        Guid actorUserId,
        string accessToken,
        HelloAdminRoleAction action,
        Guid roleId,
        Guid userId,
        Guid challengeId,
        string verificationCode,
        string? clientKey)
        => guard.RunAsync(
            actorUserId,
            challengeId,
            $"role:{HelloAdminSecurity.GetActionSlug(action)}:{roleId:N}:{userId:N}",
            async () =>
            {
                // The confirmation runs to completion even when the browser
                // abandons this request for a duplicate submission.
                var result = await roles.CompleteRoleActionAsync(
                    new HelloAdminCompleteRoleActionCommand(
                        accessToken,
                        action,
                        roleId,
                        userId,
                        new HelloAdminRoleActionParameters(),
                        challengeId,
                        verificationCode,
                        clientKey),
                    CancellationToken.None);
                if (result.IsSuccess && result.Value.CurrentActorSessionRevoked)
                {
                    return new HelloAdminCompletionOutcome(
                        true, null, [], SignOut: true, LoginFlag: "rolesChanged");
                }

                if (!result.IsSuccess
                    && action == HelloAdminRoleAction.Remove
                    && actorUserId == userId
                    && HelloAdminErrorText.HasCode(result.Errors, HelloAdminErrorCodes.SessionCleanupRequired))
                {
                    return new HelloAdminCompletionOutcome(
                        true, null, [], SignOut: true, LoginFlag: "rolesChangedSessionCleanup");
                }

                return UsersModel.ToOutcome(
                    result.IsSuccess,
                    result.Errors,
                    text["Admin.Common.ActionCompleted", text[UsersModel.GetRoleActionTextKey(action)]],
                    text);
            });
}

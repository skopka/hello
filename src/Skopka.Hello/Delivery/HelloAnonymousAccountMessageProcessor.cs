using Microsoft.AspNetCore.WebUtilities;
using Skopka.Abstraction.OperationResult;
using Skopka.Identity.Authentication;
using Skopka.Identity.Errors;
using Skopka.Identity.Tokens;
using Skopka.Identity.Users;

namespace Skopka.Hello;

internal sealed class HelloAnonymousAccountMessageProcessor<TProfile>(
    IIdentityUserLookupService<TProfile> userLookup,
    IEnumerable<IIdentityActionTokenIssuer<TProfile>> actionTokenIssuers,
    IHelloAccountMessageSender messageSender,
    SkopkaHelloOptions options,
    HelloUiRoutePaths uiRoutes)
{
    private readonly IIdentityActionTokenIssuer<TProfile>?
        actionTokenIssuer = actionTokenIssuers.FirstOrDefault();

    public async Task<OperationResult> ProcessAsync(
        HelloAnonymousAccountMessageRequest request,
        CancellationToken cancellationToken)
    {
        var lookedUp = request.Kind ==
                HelloAccountMessageKind.PhoneConfirmation
            ? await userLookup.FindActiveByPhoneAsync(
                request.NormalizedTarget,
                cancellationToken)
            : await userLookup.FindActiveByEmailAsync(
                request.NormalizedTarget,
                cancellationToken);
        if (!lookedUp.IsSuccess)
        {
            return lookedUp.Errors.Any(
                error => error.Code == IdentityErrorCodes.UserNotFound)
                ? OperationResultFactory.Success()
                : OperationResultFactory.Fail(lookedUp.Errors);
        }

        var user = lookedUp.Value;
        var recipient = request.Kind ==
                HelloAccountMessageKind.PhoneConfirmation
            ? user.Phone
            : user.Email;
        var alreadyConfirmed = request.Kind switch
        {
            HelloAccountMessageKind.EmailConfirmation =>
                user.EmailConfirmed,
            HelloAccountMessageKind.PhoneConfirmation =>
                user.PhoneConfirmed,
            _ => false,
        };
        if (string.IsNullOrWhiteSpace(recipient)
            || alreadyConfirmed)
        {
            return OperationResultFactory.Success();
        }

        var channel = request.Kind ==
                HelloAccountMessageKind.PhoneConfirmation
            ? HelloDeliveryChannel.Sms
            : HelloDeliveryChannel.Email;
        var deliveryAvailable = messageSender.CheckAvailability(
            channel);
        if (!deliveryAvailable.IsSuccess)
        {
            return OperationResultFactory.Fail(
                deliveryAvailable.Errors);
        }

        if (actionTokenIssuer is null
            || options.PublicOrigin is null)
        {
            return NotConfigured();
        }

        var issued = request.Kind switch
        {
            HelloAccountMessageKind.PasswordReset =>
                await actionTokenIssuer.IssuePasswordResetAsync(
                    user.Id,
                    cancellationToken),
            HelloAccountMessageKind.EmailConfirmation =>
                await actionTokenIssuer.IssueEmailConfirmationAsync(
                    user.Id,
                    cancellationToken),
            HelloAccountMessageKind.PhoneConfirmation =>
                await actionTokenIssuer.IssuePhoneConfirmationAsync(
                    user.Id,
                    cancellationToken),
            _ => throw new ArgumentOutOfRangeException(
                nameof(request),
                request.Kind,
                "The queued account message kind is unsupported."),
        };
        if (!issued.IsSuccess)
        {
            return OperationResultFactory.Fail(issued.Errors);
        }

        return await messageSender.SendAsync(
            new HelloAccountMessage(
                request.MessageId,
                request.Kind,
                channel,
                recipient,
                CreateActionUrl(
                    request.Kind,
                    user,
                    issued.Value.Token),
                issued.Value.ExpiresAt),
            cancellationToken);
    }

    private Uri CreateActionUrl(
        HelloAccountMessageKind kind,
        IdentityUser<TProfile> user,
        string token)
    {
        var path = kind switch
        {
            HelloAccountMessageKind.PasswordReset =>
                QueryHelpers.AddQueryString(
                    uiRoutes.ResetPasswordPath,
                    new Dictionary<string, string?>
                    {
                        ["userId"] = user.Id.ToString("D"),
                        ["token"] = token,
                    }),
            HelloAccountMessageKind.EmailConfirmation =>
                QueryHelpers.AddQueryString(
                    uiRoutes.ConfirmEmailPath,
                    new Dictionary<string, string?>
                    {
                        ["userId"] = user.Id.ToString("D"),
                        ["email"] = user.Email,
                        ["token"] = token,
                    }),
            HelloAccountMessageKind.PhoneConfirmation =>
                QueryHelpers.AddQueryString(
                    uiRoutes.ConfirmPhonePath,
                    new Dictionary<string, string?>
                    {
                        ["userId"] = user.Id.ToString("D"),
                        ["phone"] = user.Phone,
                        ["token"] = token,
                    }),
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "The queued account message kind is unsupported."),
        };

        return new Uri(
            options.PublicOrigin!,
            path.TrimStart('/'));
    }

    private static OperationResult NotConfigured()
        => OperationResultFactory.Fail(
            new Error(
                HelloDeliveryErrorCodes.NotConfigured,
                "Account message delivery is not configured.",
                ErrorType.Failure));
}

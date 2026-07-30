using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Skopka.Abstraction.OperationResult;
using Skopka.Identity;
using Skopka.Identity.Authentication;
using Skopka.Identity.Credentials;
using Skopka.Identity.Errors;
using Skopka.Identity.Registration;
using Skopka.Identity.Sessions;
using Skopka.Identity.StepUp;
using Skopka.Identity.StepUp.Commands;
using Skopka.Identity.Tokens;
using Skopka.Identity.Users;
using Skopka.Identity.Users.Commands;
using Skopka.Identity.Verification;

namespace Skopka.Hello;

internal sealed partial class HelloIdentityApplication<TProfile>(
    IIdentityRegistrationService<TProfile> registration,
    IPasswordAuthenticationService<TProfile> authentication,
    IIdentitySessionService<TProfile> sessions,
    IPasswordCredentialService<TProfile> credentials,
    IIdentityUserService<TProfile> users,
    IIdentityUserLookupService<TProfile> userLookup,
    IIdentityStepUpService<TProfile> stepUp,
    IIdentityVerificationService<TProfile> verification,
    IEnumerable<IIdentityActionTokenIssuer<TProfile>> actionTokenIssuers,
    IHelloAccountMessageSender messageSender,
    SkopkaHelloOptions options,
    ILogger<HelloIdentityApplication<TProfile>> logger)
    : IHelloIdentityApplication<TProfile>
{
    private readonly IIdentityActionTokenIssuer<TProfile>? actionTokenIssuer =
        actionTokenIssuers.FirstOrDefault();

    public async Task<OperationResult<HelloAccount<TProfile>>> RegisterAsync(
        HelloRegisterCommand<TProfile> command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var result = await registration.RegisterPasswordAsync(
            new RegisterPasswordUserCommand<TProfile>(
                new CreateUserCommand<TProfile>(
                    command.UserName,
                    command.Email,
                    command.Phone,
                    command.Profile),
                command.Password),
            cancellationToken);

        return result.IsSuccess
            ? OperationResultFactory.Success(ToAccount(result.Value))
            : OperationResultFactory.Fail<HelloAccount<TProfile>>(
                result.Errors);
    }

    public async Task<OperationResult<HelloSignIn<TProfile>>> LoginAsync(
        HelloLoginCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var authenticated = await authentication.AuthenticateAsync(
            new AuthenticatePasswordCommand(
                command.Handle,
                command.Login,
                command.Password,
                command.ClientKey),
            cancellationToken);
        if (!authenticated.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloSignIn<TProfile>>(
                authenticated.Errors);
        }

        var issued = await sessions.CreateAsync(
            new CreateIdentitySessionCommand(
                authenticated.Value.Id,
                authenticated.Value.SecurityStamp,
                command.SessionMetadata),
            cancellationToken);
        return issued.IsSuccess
            ? OperationResultFactory.Success(
                new HelloSignIn<TProfile>(
                    ToAccount(authenticated.Value),
                    ToSession(issued.Value)))
            : OperationResultFactory.Fail<HelloSignIn<TProfile>>(
                issued.Errors);
    }

    public async Task<OperationResult<HelloSession>> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);

        var result = await sessions.RefreshAsync(
            new RefreshIdentitySessionCommand(refreshToken),
            cancellationToken);
        return result.IsSuccess
            ? OperationResultFactory.Success(ToSession(result.Value))
            : OperationResultFactory.Fail<HelloSession>(result.Errors);
    }

    public async Task<OperationResult<HelloAccount<TProfile>>>
        ValidateAccessTokenAsync(
            string accessToken,
            CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        var result = await sessions.ValidateAccessTokenAsync(
            accessToken,
            cancellationToken);
        return result.IsSuccess
            ? OperationResultFactory.Success(ToAccount(result.Value))
            : OperationResultFactory.Fail<HelloAccount<TProfile>>(
                result.Errors);
    }

    public Task<OperationResult> LogoutAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);

        return sessions.RevokeAsync(
            new RevokeIdentitySessionCommand(refreshToken),
            cancellationToken);
    }

    public Task<OperationResult> LogoutAllAsync(
        Guid userId,
        CancellationToken cancellationToken)
        => sessions.RevokeAllAsync(
            new RevokeAllIdentitySessionsCommand(userId),
            cancellationToken);

    public async Task<OperationResult<IReadOnlyList<HelloSessionInfo>>>
        ListSessionsAsync(
            Guid userId,
            CancellationToken cancellationToken)
    {
        var result = await sessions.ListAsync(
            new ListIdentitySessionsCommand(userId),
            cancellationToken);
        return result.IsSuccess
            ? OperationResultFactory.Success<IReadOnlyList<HelloSessionInfo>>(
                result.Value.Select(ToSessionInfo).ToArray())
            : OperationResultFactory.Fail<IReadOnlyList<HelloSessionInfo>>(
                result.Errors);
    }

    public Task<OperationResult> RevokeSessionAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken)
        => sessions.RevokeByIdAsync(
            new RevokeIdentitySessionByIdCommand(userId, sessionId),
            cancellationToken);

    public Task<OperationResult> RequestPasswordResetAsync(
        string email,
        CancellationToken cancellationToken)
        => RequestAccountMessageAsync(
            email,
            HelloAccountMessageKind.PasswordReset,
            cancellationToken);

    public Task<OperationResult> RequestEmailConfirmationAsync(
        string email,
        CancellationToken cancellationToken)
        => RequestAccountMessageAsync(
            email,
            HelloAccountMessageKind.EmailConfirmation,
            cancellationToken);

    public Task<OperationResult> ResetPasswordAsync(
        HelloResetPasswordCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return credentials.ResetPasswordAsync(
            new ResetPasswordCommand(
                command.UserId,
                command.Token,
                command.NewPassword),
            cancellationToken);
    }

    public async Task<OperationResult<HelloAccount<TProfile>>>
        ConfirmEmailAsync(
            HelloConfirmEmailCommand command,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var result = await users.ConfirmEmailAsync(
            new ConfirmEmailCommand(
                command.UserId,
                command.Email,
                command.Token),
            cancellationToken);
        return result.IsSuccess
            ? OperationResultFactory.Success(ToAccount(result.Value))
            : OperationResultFactory.Fail<HelloAccount<TProfile>>(
                result.Errors);
    }

    public async Task<OperationResult<HelloStepUpChallenge>>
        BeginPasswordChangeAsync(
            HelloBeginPasswordChangeCommand command,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validated = await sessions.ValidateAccessTokenAsync(
            command.AccessToken,
            cancellationToken);
        if (!validated.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloStepUpChallenge>(
                validated.Errors);
        }

        var user = validated.Value;
        if (!HasConfirmedEmail(user))
        {
            return OperationResultFactory.Fail<HelloStepUpChallenge>(
                HelloAccountSecurity.ConfirmedEmailRequired());
        }

        var issued = await stepUp.BeginAsync(
            new BeginStepUpCommand(
                user.Id,
                HelloAccountSecurity.PasswordChangeAction,
                HelloAccountSecurity.CreateBinding(user.Id),
                VerificationMethods.OneTimeCode,
                command.ClientKey),
            cancellationToken);
        if (!issued.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloStepUpChallenge>(
                issued.Errors);
        }

        if (string.IsNullOrWhiteSpace(issued.Value.DeliveryCode))
        {
            return OperationResultFactory.Fail<HelloStepUpChallenge>(
                new Error(
                    IdentityErrorCodes.VerificationMethodUnavailable,
                    "The verification code could not be delivered.",
                    ErrorType.Failure));
        }

        var delivered = await messageSender.SendAsync(
            new HelloAccountMessage(
                HelloAccountMessageKind.StepUpVerification,
                user.Email!,
                null,
                issued.Value.ExpiresAt,
                issued.Value.DeliveryCode),
            cancellationToken);
        return delivered.IsSuccess
            ? OperationResultFactory.Success(
                new HelloStepUpChallenge(
                    issued.Value.ChallengeId,
                    issued.Value.ExpiresAt))
            : OperationResultFactory.Fail<HelloStepUpChallenge>(
                delivered.Errors);
    }

    public async Task<OperationResult> CompletePasswordChangeAsync(
        HelloCompletePasswordChangeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validated = await sessions.ValidateAccessTokenAsync(
            command.AccessToken,
            cancellationToken);
        if (!validated.IsSuccess)
        {
            return OperationResultFactory.Fail(validated.Errors);
        }

        var user = validated.Value;
        if (!HasConfirmedEmail(user))
        {
            return OperationResultFactory.Fail(
                HelloAccountSecurity.ConfirmedEmailRequired());
        }

        var verified = await verification.VerifyAsync(
            new VerifyVerificationChallengeCommand(
                command.ChallengeId,
                user.Id,
                command.VerificationCode),
            cancellationToken);
        if (!verified.IsSuccess)
        {
            return OperationResultFactory.Fail(verified.Errors);
        }

        var authorized = await stepUp.AuthorizeAsync(
            new AuthorizeStepUpCommand(
                user.Id,
                HelloAccountSecurity.PasswordChangeAction,
                HelloAccountSecurity.CreateBinding(user.Id),
                command.ChallengeId,
                verified.Value.Token),
            cancellationToken);
        if (!authorized.IsSuccess)
        {
            return OperationResultFactory.Fail(authorized.Errors);
        }

        var changed = await credentials.ChangePasswordAsync(
            new ChangePasswordCommand(
                user.Id,
                user.Version,
                command.CurrentPassword,
                command.NewPassword),
            cancellationToken);
        if (!changed.IsSuccess)
        {
            return OperationResultFactory.Fail(changed.Errors);
        }

        return await sessions.RevokeAllAsync(
            new RevokeAllIdentitySessionsCommand(user.Id),
            cancellationToken);
    }

    private async Task<OperationResult> RequestAccountMessageAsync(
        string email,
        HelloAccountMessageKind kind,
        CancellationToken cancellationToken)
    {
        var validation = ValidateEmail(email);
        if (validation is not null)
        {
            return OperationResultFactory.Fail(validation);
        }

        var lookedUp = await userLookup.FindActiveByEmailAsync(
            email,
            cancellationToken);
        if (!lookedUp.IsSuccess)
        {
            if (!lookedUp.Errors.Any(
                    error =>
                        error.Code == IdentityErrorCodes.UserNotFound))
            {
                LogSuppressed(kind, lookedUp.Errors);
            }

            return OperationResultFactory.Success();
        }

        var user = lookedUp.Value;
        if (string.IsNullOrWhiteSpace(user.Email)
            || (kind == HelloAccountMessageKind.EmailConfirmation
                && user.EmailConfirmed))
        {
            return OperationResultFactory.Success();
        }

        if (actionTokenIssuer is null
            || options.PublicOrigin is null)
        {
            LogSuppressed(
                kind,
                HelloDeliveryErrorCodes.NotConfigured);
            return OperationResultFactory.Success();
        }

        var issued = kind switch
        {
            HelloAccountMessageKind.PasswordReset =>
                await actionTokenIssuer.IssuePasswordResetAsync(
                    user.Id,
                    cancellationToken),
            HelloAccountMessageKind.EmailConfirmation =>
                await actionTokenIssuer.IssueEmailConfirmationAsync(
                    user.Id,
                    cancellationToken),
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "The account message kind is unsupported."),
        };
        if (!issued.IsSuccess)
        {
            LogSuppressed(kind, issued.Errors);
            return OperationResultFactory.Success();
        }

        var message = new HelloAccountMessage(
            kind,
            user.Email,
            CreateActionUrl(
                kind,
                user,
                issued.Value.Token),
            issued.Value.ExpiresAt);
        var delivered = await messageSender.SendAsync(
            message,
            cancellationToken);
        if (!delivered.IsSuccess)
        {
            LogSuppressed(kind, delivered.Errors);
        }

        return OperationResultFactory.Success();
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
                    "/hello/reset-password",
                    new Dictionary<string, string?>
                    {
                        ["userId"] = user.Id.ToString("D"),
                        ["token"] = token,
                    }),
            HelloAccountMessageKind.EmailConfirmation =>
                QueryHelpers.AddQueryString(
                    "/hello/confirm-email",
                    new Dictionary<string, string?>
                    {
                        ["userId"] = user.Id.ToString("D"),
                        ["email"] = user.Email,
                        ["token"] = token,
                    }),
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "The account message kind is unsupported."),
        };

        return new Uri(
            options.PublicOrigin!,
            path.TrimStart('/'));
    }

    private static Error? ValidateEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)
            || !new EmailAddressAttribute().IsValid(email))
        {
            return new Error(
                IdentityErrorCodes.Validation,
                "Validation failed.",
                ErrorType.Validation,
                new ValidationDetails(
                    new Dictionary<string, string[]>
                    {
                        ["email"] =
                        [
                            "Enter a valid email address.",
                        ],
                    }));
        }

        return null;
    }

    private static bool HasConfirmedEmail(
        IdentityUser<TProfile> user)
        => user.EmailConfirmed
            && !string.IsNullOrWhiteSpace(user.Email);

    private void LogSuppressed(
        HelloAccountMessageKind kind,
        IReadOnlyCollection<Error> errors)
        => LogSuppressed(
            kind,
            errors.FirstOrDefault()?.Code
                ?? "hello.operation.failed");

    private void LogSuppressed(
        HelloAccountMessageKind kind,
        string errorCode)
        => AccountMessageNotDelivered(
            logger,
            kind,
            errorCode);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Warning,
        Message =
            "An account message was not delivered. Kind: {messageKind}; error code: {errorCode}.")]
    private static partial void AccountMessageNotDelivered(
        ILogger logger,
        HelloAccountMessageKind messageKind,
        string errorCode);

    private static HelloAccount<TProfile> ToAccount(
        IdentityUser<TProfile> user)
        => new(
            user.Id,
            user.Flags,
            user.UserName,
            user.Email,
            user.EmailConfirmed,
            user.Phone,
            user.PhoneConfirmed,
            user.Profile,
            user.Version,
            user.CreatedAt,
            user.ModifiedAt);

    private static HelloSession ToSession(IssuedIdentitySession session)
        => new(
            session.SessionId,
            session.AccessToken,
            session.AccessTokenExpiresAt,
            session.RefreshToken,
            session.RefreshTokenExpiresAt);

    private static HelloSessionInfo ToSessionInfo(
        IdentitySessionInfo session)
        => new(
            session.SessionId,
            session.Metadata.ClientName,
            session.Metadata.DeviceName,
            session.ExpiresAt,
            session.CreatedAt,
            session.LastRefreshedAt);
}

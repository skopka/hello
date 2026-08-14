using Microsoft.AspNetCore.Http;
using Skopka.Abstraction.OperationResult;
using Skopka.Identity.Errors;
using Skopka.Identity.ExternalLogins;
using Skopka.Identity.SignInMethods;

namespace Skopka.Hello.Oidc;

internal sealed class HelloOidcApplication<TProfile>(
    IHelloExternalIdentityApplication<TProfile> externalIdentity,
    HelloOidcProviderCatalog providers,
    HelloOidcTicketService tickets,
    IHelloOidcFlowStore flows,
    IHelloOidcChallengeService challenges,
    HelloOidcOptions options,
    SkopkaHelloOptions helloOptions,
    HelloUiRoutePaths uiRoutes,
    IHelloRequestContext? requestContext = null)
    : IHelloOidcApplication<TProfile>
{
    public async Task<OperationResult<HelloOidcCompletion<TProfile>>>
        CompleteChallengeAsync(
            HttpContext httpContext,
            HelloOidcLocalSession? localSession,
            Skopka.Identity.Sessions.IdentitySessionMetadata sessionMetadata,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(sessionMetadata);

        var read = await tickets.ReadExternalAsync(httpContext);
        if (!read.IsSuccess)
        {
            await HelloOidcTicketService.DeleteExternalAsync(httpContext);
            return Fail<HelloOidcCompletion<TProfile>>(read.Errors);
        }

        var ticket = read.Value;
        if (!providers.TryGet(ticket.Login.Provider, out var provider))
        {
            await HelloOidcTicketService.DeleteExternalAsync(httpContext);
            return Fail<HelloOidcCompletion<TProfile>>(
                HelloOidcErrors.ProviderUnavailable());
        }

        ticket = ticket with
        {
            Login = new ExternalLoginKey(
                provider.Id,
                ticket.Login.Subject),
        };
        var publicProvider = ToProvider(provider);

        if (!await flows.TryConsumeAsync(
                ticket.FlowId,
                ticket.ExpiresAt,
                cancellationToken))
        {
            await HelloOidcTicketService.DeleteExternalAsync(httpContext);
            return Fail<HelloOidcCompletion<TProfile>>(
                HelloOidcErrors.PendingIdentityInvalid());
        }

        if (ticket.Intent == HelloOidcProperties.SignInIntent)
        {
            if (localSession is not null)
            {
                await HelloOidcTicketService.DeleteExternalAsync(
                    httpContext);
                return Fail<HelloOidcCompletion<TProfile>>(
                    HelloOidcErrors.PendingIdentityInvalid());
            }

            var signedIn = await externalIdentity.SignInAsync(
                new HelloExternalSignInCommand(
                    ticket.Login,
                    sessionMetadata),
                cancellationToken);
            if (signedIn.IsSuccess)
            {
                await HelloOidcTicketService.DeleteExternalAsync(httpContext);
                return OperationResultFactory.Success(
                    new HelloOidcCompletion<TProfile>(
                        HelloOidcCompletionKind.SignedIn,
                        signedIn.Value,
                        null,
                        publicProvider,
                        ticket.ReturnUrl));
            }

            if (!IsOnlyError(
                    signedIn.Errors,
                    IdentityErrorCodes.ExternalLoginNotFound))
            {
                await HelloOidcTicketService.DeleteExternalAsync(httpContext);
                return Fail<HelloOidcCompletion<TProfile>>(
                    signedIn.Errors);
            }

            if (!helloOptions.SelfRegistrationEnabled)
            {
                await HelloOidcTicketService.DeleteExternalAsync(
                    httpContext);
                return Fail<HelloOidcCompletion<TProfile>>(
                    HelloRegistrationErrors.Disabled());
            }

            if (!await tickets.PromoteToPendingAsync(httpContext, ticket))
            {
                await HelloOidcTicketService.DeleteExternalAsync(
                    httpContext);
                return Fail<HelloOidcCompletion<TProfile>>(
                    HelloOidcErrors.PendingIdentityInvalid());
            }

            return OperationResultFactory.Success(
                new HelloOidcCompletion<TProfile>(
                    HelloOidcCompletionKind.RegistrationRequired,
                    null,
                    CreateRegistrationHints(ticket, publicProvider),
                    publicProvider,
                    ticket.ReturnUrl));
        }

        if (ticket.Intent != HelloOidcProperties.LinkIntent
            || localSession is null)
        {
            await HelloOidcTicketService.DeleteExternalAsync(httpContext);
            return Fail<HelloOidcCompletion<TProfile>>(
                HelloOidcErrors.PendingIdentityInvalid());
        }

        var local = await ValidateLocalSessionAsync(
            ticket,
            localSession,
            cancellationToken);
        if (!local.IsSuccess)
        {
            await HelloOidcTicketService.DeleteExternalAsync(httpContext);
            return Fail<HelloOidcCompletion<TProfile>>(local.Errors);
        }

        if (HasProvider(local.Value, provider.Id))
        {
            await HelloOidcTicketService.DeleteExternalAsync(httpContext);
            return Fail<HelloOidcCompletion<TProfile>>(
                HelloOidcErrors.ProviderAlreadyLinked());
        }

        if (!await tickets.PromoteToPendingAsync(httpContext, ticket))
        {
            await HelloOidcTicketService.DeleteExternalAsync(httpContext);
            return Fail<HelloOidcCompletion<TProfile>>(
                HelloOidcErrors.PendingIdentityInvalid());
        }

        return OperationResultFactory.Success(
            new HelloOidcCompletion<TProfile>(
                HelloOidcCompletionKind.LinkPending,
                null,
                null,
                publicProvider,
                uiRoutes.ExternalLoginsPath));
    }

    public async Task<OperationResult<HelloOidcRegistrationHints>>
        GetRegistrationHintsAsync(
            HttpContext httpContext,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        cancellationToken.ThrowIfCancellationRequested();
        if (!helloOptions.SelfRegistrationEnabled)
        {
            await HelloOidcTicketService.DeletePendingAsync(httpContext);
            return Fail<HelloOidcRegistrationHints>(
                HelloRegistrationErrors.Disabled());
        }

        var read = await tickets.ReadPendingAsync(httpContext);
        if (!read.IsSuccess)
        {
            return Fail<HelloOidcRegistrationHints>(read.Errors);
        }

        var ticket = read.Value;
        if (ticket.Intent != HelloOidcProperties.SignInIntent
            || !providers.TryGet(
                ticket.Login.Provider,
                out var provider))
        {
            return Fail<HelloOidcRegistrationHints>(
                HelloOidcErrors.PendingIdentityInvalid());
        }

        return OperationResultFactory.Success(
            CreateRegistrationHints(ticket, ToProvider(provider)));
    }

    public async Task<OperationResult<HelloSignIn<TProfile>>> RegisterAsync(
        HelloOidcRegisterCommand<TProfile> command,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(httpContext);

        if (!helloOptions.SelfRegistrationEnabled)
        {
            await HelloOidcTicketService.DeletePendingAsync(httpContext);
            return Fail<HelloSignIn<TProfile>>(
                HelloRegistrationErrors.Disabled());
        }

        var read = await tickets.ReadPendingAsync(httpContext);
        if (!read.IsSuccess
            || read.Value.Intent != HelloOidcProperties.SignInIntent
            || !providers.TryGet(
                read.IsSuccess ? read.Value.Login.Provider : null,
                out var provider))
        {
            return Fail<HelloSignIn<TProfile>>(
                read.IsSuccess
                    ? [HelloOidcErrors.PendingIdentityInvalid()]
                    : read.Errors);
        }

        if (!await flows.TryConsumeAsync(
                read.Value.FlowId,
                read.Value.ExpiresAt,
                cancellationToken))
        {
            await HelloOidcTicketService.DeletePendingAsync(httpContext);
            return Fail<HelloSignIn<TProfile>>(
                HelloOidcErrors.PendingIdentityInvalid());
        }

        var ticket = read.Value with
        {
            Login = new ExternalLoginKey(
                provider.Id,
                read.Value.Login.Subject),
        };
        var result = await externalIdentity.RegisterAsync(
            new HelloExternalRegistrationCommand<TProfile>(
                command.UserName,
                command.Email,
                command.Phone,
                command.Profile,
                ticket.Login,
                command.SessionMetadata),
            cancellationToken);
        if (!result.IsSuccess
            && IsOnlyError(
                result.Errors,
                IdentityErrorCodes.DuplicateExternalLogin))
        {
            result = await externalIdentity.SignInAsync(
                new HelloExternalSignInCommand(
                    ticket.Login,
                    command.SessionMetadata),
                cancellationToken);
        }

        if (result.IsSuccess)
        {
            await HelloOidcTicketService.DeletePendingAsync(httpContext);
            return result;
        }

        if (result.Errors.Any(
                error => error.Code == IdentityErrorCodes.DuplicateEmail))
        {
            await HelloOidcTicketService.DeletePendingAsync(httpContext);
            return Fail<HelloSignIn<TProfile>>(
                HelloOidcErrors.AccountRequiresLink());
        }

        if (!await HelloOidcTicketService.RotatePendingAsync(
                httpContext,
                ticket))
        {
            await HelloOidcTicketService.DeletePendingAsync(httpContext);
            return Fail<HelloSignIn<TProfile>>(
                HelloOidcErrors.PendingIdentityInvalid());
        }

        return Fail<HelloSignIn<TProfile>>(result.Errors);
    }

    public async Task<OperationResult<IReadOnlyList<HelloOidcLinkedProvider>>>
        ListLinkedProvidersAsync(
            string accessToken,
            CancellationToken cancellationToken)
    {
        var snapshot = await externalIdentity.GetSignInMethodsAsync(
            accessToken,
            cancellationToken);
        return snapshot.IsSuccess
            ? OperationResultFactory.Success<
                IReadOnlyList<HelloOidcLinkedProvider>>(
                    ToLinkedProviders(snapshot.Value))
            : Fail<IReadOnlyList<HelloOidcLinkedProvider>>(
                snapshot.Errors);
    }

    public async Task<OperationResult<HelloOidcHeadlessLinkStart>>
        PrepareHeadlessLinkAsync(
            string providerId,
            string returnUrl,
            HelloOidcLocalSession localSession,
            HttpContext httpContext,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(localSession);
        ArgumentNullException.ThrowIfNull(httpContext);

        if (!providers.TryGet(providerId, out var provider))
        {
            return Fail<HelloOidcHeadlessLinkStart>(
                HelloOidcErrors.ProviderUnavailable());
        }

        if (!HelloOidcReturnUrl.TryNormalizeHeadless(
                returnUrl,
                out var normalizedReturnUrl))
        {
            return Fail<HelloOidcHeadlessLinkStart>(
                HelloOidcErrors.ReturnUrlInvalid());
        }

        var snapshot = await externalIdentity.GetSignInMethodsAsync(
            localSession.AccessToken,
            cancellationToken);
        if (!snapshot.IsSuccess
            || snapshot.Value.UserId != localSession.UserId)
        {
            return Fail<HelloOidcHeadlessLinkStart>(
                snapshot.IsSuccess
                    ? [HelloOidcErrors.PendingIdentityInvalid()]
                    : snapshot.Errors);
        }

        if (HasProvider(snapshot.Value, provider.Id))
        {
            return Fail<HelloOidcHeadlessLinkStart>(
                HelloOidcErrors.ProviderAlreadyLinked());
        }

        var expiresAt = DateTimeOffset.UtcNow.Add(
            options.ExternalCookieLifetime);
        if (!await HelloOidcTicketService.WriteLinkRequestAsync(
                httpContext,
                new HelloOidcLinkRequest(
                    HelloOidcFlowId.Create(),
                    provider.Id,
                    normalizedReturnUrl,
                    localSession.UserId,
                    localSession.SessionId,
                    expiresAt)))
        {
            await HelloOidcTicketService.DeleteLinkRequestAsync(
                httpContext);
            return Fail<HelloOidcHeadlessLinkStart>(
                HelloOidcErrors.PendingIdentityInvalid());
        }

        return OperationResultFactory.Success(
            new HelloOidcHeadlessLinkStart(
                HelloOidcDefaults.ApiPathPrefix
                    + provider.Id
                    + "/link-challenge"));
    }

    public async Task<OperationResult<HelloOidcChallenge>>
        BeginHeadlessLinkAsync(
            string providerId,
            HttpContext httpContext,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var read = await tickets.ReadLinkRequestAsync(httpContext);
        if (!read.IsSuccess)
        {
            await HelloOidcTicketService.DeleteLinkRequestAsync(
                httpContext);
            return Fail<HelloOidcChallenge>(read.Errors);
        }

        try
        {
            if (!await flows.TryConsumeAsync(
                    read.Value.FlowId,
                    read.Value.ExpiresAt,
                    cancellationToken)
                || !string.Equals(
                    providerId,
                    read.Value.ProviderId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return Fail<HelloOidcChallenge>(
                    HelloOidcErrors.PendingIdentityInvalid());
            }

            return challenges.CreateHeadlessLink(
                read.Value.ProviderId,
                read.Value.ReturnUrl,
                read.Value.UserId,
                read.Value.SessionId);
        }
        finally
        {
            await HelloOidcTicketService.DeleteLinkRequestAsync(
                httpContext);
        }
    }

    public async Task<OperationResult<HelloOidcProvider>>
        GetPendingLinkAsync(
            HttpContext httpContext,
            HelloOidcLocalSession localSession,
            CancellationToken cancellationToken)
    {
        var read = await tickets.ReadPendingAsync(httpContext);
        if (!read.IsSuccess
            || read.Value.Intent != HelloOidcProperties.LinkIntent
            || !providers.TryGet(
                read.IsSuccess ? read.Value.Login.Provider : null,
                out var provider))
        {
            return Fail<HelloOidcProvider>(
                read.IsSuccess
                    ? [HelloOidcErrors.PendingIdentityInvalid()]
                    : read.Errors);
        }

        var local = await ValidateLocalSessionAsync(
            read.Value,
            localSession,
            cancellationToken);
        return local.IsSuccess
            ? OperationResultFactory.Success(ToProvider(provider))
            : Fail<HelloOidcProvider>(local.Errors);
    }

    public async Task<OperationResult<HelloStepUpChallenge>> BeginLinkAsync(
        HttpContext httpContext,
        HelloOidcLocalSession localSession,
        string? clientKey,
        CancellationToken cancellationToken)
    {
        var read = await tickets.ReadPendingAsync(httpContext);
        if (!read.IsSuccess
            || read.Value.Intent != HelloOidcProperties.LinkIntent
            || !providers.TryGet(
                read.IsSuccess ? read.Value.Login.Provider : null,
                out var provider))
        {
            return Fail<HelloStepUpChallenge>(
                read.IsSuccess
                    ? [HelloOidcErrors.PendingIdentityInvalid()]
                    : read.Errors);
        }

        var ticket = read.Value with
        {
            Login = new ExternalLoginKey(
                provider.Id,
                read.Value.Login.Subject),
        };
        var local = await ValidateLocalSessionAsync(
            ticket,
            localSession,
            cancellationToken);
        if (!local.IsSuccess)
        {
            return Fail<HelloStepUpChallenge>(local.Errors);
        }

        if (HasProvider(local.Value, provider.Id))
        {
            return Fail<HelloStepUpChallenge>(
                HelloOidcErrors.ProviderAlreadyLinked());
        }

        var begun = await externalIdentity.BeginLinkAsync(
            new HelloBeginExternalLoginMutationCommand(
                localSession.AccessToken,
                ticket.Login,
                clientKey),
            cancellationToken);
        if (!begun.IsSuccess)
        {
            return Fail<HelloStepUpChallenge>(begun.Errors);
        }

        if (!await HelloOidcTicketService.WritePendingAsync(
            httpContext,
            ticket with
            {
                ChallengeId = begun.Value.ChallengeId,
            }))
        {
            await HelloOidcTicketService.DeletePendingAsync(httpContext);
            return Fail<HelloStepUpChallenge>(
                HelloOidcErrors.PendingIdentityInvalid());
        }

        return begun;
    }

    public Task<OperationResult<HelloSignIn<TProfile>>> CompleteLinkAsync(
        string verificationCode,
        HttpContext httpContext,
        HelloOidcLocalSession localSession,
        Skopka.Identity.Sessions.IdentitySessionMetadata sessionMetadata,
        CancellationToken cancellationToken)
        => CompleteMutationAsync(
            verificationCode,
            httpContext,
            localSession,
            sessionMetadata,
            HelloOidcProperties.LinkIntent,
            link: true,
            cancellationToken);

    public async Task<OperationResult<HelloStepUpChallenge>> BeginUnlinkAsync(
        string providerId,
        HttpContext httpContext,
        HelloOidcLocalSession localSession,
        string? clientKey,
        CancellationToken cancellationToken)
    {
        var snapshot = await externalIdentity.GetSignInMethodsAsync(
            localSession.AccessToken,
            cancellationToken);
        if (!snapshot.IsSuccess
            || snapshot.Value.UserId != localSession.UserId)
        {
            return Fail<HelloStepUpChallenge>(
                snapshot.IsSuccess
                    ? [HelloOidcErrors.PendingIdentityInvalid()]
                    : snapshot.Errors);
        }

        var target = ResolveTarget(snapshot.Value, providerId);
        if (!target.IsSuccess)
        {
            return Fail<HelloStepUpChallenge>(target.Errors);
        }

        if (!HasAlternateMethod(snapshot.Value, target.Value))
        {
            return Fail<HelloStepUpChallenge>(
                HelloOidcErrors.LastSignInMethod());
        }

        var begun = await externalIdentity.BeginUnlinkAsync(
            new HelloBeginExternalLoginMutationCommand(
                localSession.AccessToken,
                target.Value,
                clientKey),
            cancellationToken);
        if (!begun.IsSuccess)
        {
            return Fail<HelloStepUpChallenge>(begun.Errors);
        }

        if (!await HelloOidcTicketService.WritePendingAsync(
            httpContext,
            new HelloOidcTicket(
                HelloOidcFlowId.Create(),
                HelloOidcProperties.UnlinkIntent,
                target.Value,
                uiRoutes.ExternalLoginsPath,
                null,
                null,
                null,
                localSession.UserId,
                localSession.SessionId,
                begun.Value.ChallengeId,
                DateTimeOffset.UtcNow.Add(
                    options.PendingCookieLifetime))))
        {
            await HelloOidcTicketService.DeletePendingAsync(httpContext);
            return Fail<HelloStepUpChallenge>(
                HelloOidcErrors.PendingIdentityInvalid());
        }

        return begun;
    }

    public Task<OperationResult<HelloSignIn<TProfile>>> CompleteUnlinkAsync(
        string verificationCode,
        HttpContext httpContext,
        HelloOidcLocalSession localSession,
        Skopka.Identity.Sessions.IdentitySessionMetadata sessionMetadata,
        CancellationToken cancellationToken)
        => CompleteMutationAsync(
            verificationCode,
            httpContext,
            localSession,
            sessionMetadata,
            HelloOidcProperties.UnlinkIntent,
            link: false,
            cancellationToken);

    public async Task ClearBrowserFlowAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var external = await tickets.ReadExternalAsync(httpContext);
            var pending = await tickets.ReadPendingAsync(httpContext);
            var linkRequest = await tickets.ReadLinkRequestAsync(
                httpContext);
            if (external.IsSuccess)
            {
                await flows.TryConsumeAsync(
                    external.Value.FlowId,
                    external.Value.ExpiresAt,
                    cancellationToken);
            }

            if (pending.IsSuccess)
            {
                await flows.TryConsumeAsync(
                    pending.Value.FlowId,
                    pending.Value.ExpiresAt,
                    cancellationToken);
            }

            if (linkRequest.IsSuccess)
            {
                await flows.TryConsumeAsync(
                    linkRequest.Value.FlowId,
                    linkRequest.Value.ExpiresAt,
                    cancellationToken);
            }
        }
        finally
        {
            try
            {
                await HelloOidcTicketService.DeleteExternalAsync(
                    httpContext);
            }
            finally
            {
                try
                {
                    await HelloOidcTicketService.DeletePendingAsync(
                        httpContext);
                }
                finally
                {
                    await HelloOidcTicketService
                        .DeleteLinkRequestAsync(httpContext);
                }
            }
        }
    }

    private async Task<OperationResult<HelloSignIn<TProfile>>>
        CompleteMutationAsync(
            string verificationCode,
            HttpContext httpContext,
            HelloOidcLocalSession localSession,
            Skopka.Identity.Sessions.IdentitySessionMetadata sessionMetadata,
            string expectedIntent,
            bool link,
            CancellationToken cancellationToken)
    {
        var read = await tickets.ReadPendingAsync(httpContext);
        if (!read.IsSuccess
            || read.Value.Intent != expectedIntent
            || read.Value.ChallengeId is null)
        {
            return Fail<HelloSignIn<TProfile>>(
                read.IsSuccess
                    ? [HelloOidcErrors.PendingIdentityInvalid()]
                    : read.Errors);
        }

        var ticket = read.Value;
        if (!await flows.TryConsumeAsync(
                ticket.FlowId,
                ticket.ExpiresAt,
                cancellationToken))
        {
            await HelloOidcTicketService.DeletePendingAsync(httpContext);
            return Fail<HelloSignIn<TProfile>>(
                HelloOidcErrors.PendingIdentityInvalid());
        }

        var local = await ValidateLocalSessionAsync(
            ticket,
            localSession,
            cancellationToken);
        if (!local.IsSuccess)
        {
            await HelloOidcTicketService.DeletePendingAsync(httpContext);
            return Fail<HelloSignIn<TProfile>>(local.Errors);
        }

        if (link)
        {
            if (!providers.IsEnabled(ticket.Login.Provider)
                || HasProvider(local.Value, ticket.Login.Provider))
            {
                await HelloOidcTicketService.DeletePendingAsync(httpContext);
                return Fail<HelloSignIn<TProfile>>(
                    HelloOidcErrors.ProviderAlreadyLinked());
            }
        }
        else
        {
            var target = ResolveTarget(
                local.Value,
                ticket.Login.Provider);
            if (!target.IsSuccess
                || target.Value != ticket.Login
                || !HasAlternateMethod(local.Value, ticket.Login))
            {
                await HelloOidcTicketService.DeletePendingAsync(httpContext);
                return Fail<HelloSignIn<TProfile>>(
                    target.IsSuccess
                        ? HelloOidcErrors.LastSignInMethod()
                        : target.Errors.First());
            }
        }

        var command = new HelloCompleteExternalLoginMutationCommand(
            localSession.AccessToken,
            ticket.Login,
            local.Value.Version,
            ticket.ChallengeId.Value,
            verificationCode,
            sessionMetadata,
            requestContext?.CreateClientKey(httpContext));
        var completed = link
            ? await externalIdentity.CompleteLinkAsync(
                command,
                cancellationToken)
            : await externalIdentity.CompleteUnlinkAsync(
                command,
                cancellationToken);
        if (completed.IsSuccess
            || completed.Errors.Any(error => error.Code is
                HelloExternalIdentityErrorCodes.ChallengeRestartRequired
                or HelloExternalIdentityErrorCodes.RestartRequired))
        {
            await HelloOidcTicketService.DeletePendingAsync(httpContext);
        }
        else
        {
            if (!await HelloOidcTicketService.RotatePendingAsync(
                    httpContext,
                    ticket))
            {
                await HelloOidcTicketService.DeletePendingAsync(httpContext);
                return Fail<HelloSignIn<TProfile>>(
                    HelloOidcErrors.PendingIdentityInvalid());
            }
        }

        return completed;
    }

    private async Task<OperationResult<SignInMethodSnapshot>>
        ValidateLocalSessionAsync(
            HelloOidcTicket ticket,
            HelloOidcLocalSession localSession,
            CancellationToken cancellationToken)
    {
        if (ticket.UserId != localSession.UserId
            || ticket.SessionId != localSession.SessionId)
        {
            return Fail<SignInMethodSnapshot>(
                HelloOidcErrors.PendingIdentityInvalid());
        }

        var snapshot = await externalIdentity.GetSignInMethodsAsync(
            localSession.AccessToken,
            cancellationToken);
        if (!snapshot.IsSuccess)
        {
            return Fail<SignInMethodSnapshot>(snapshot.Errors);
        }

        return snapshot.Value.UserId == localSession.UserId
            ? snapshot
            : Fail<SignInMethodSnapshot>(
                HelloOidcErrors.PendingIdentityInvalid());
    }

    private static OperationResult<ExternalLoginKey> ResolveTarget(
        SignInMethodSnapshot snapshot,
        string providerId)
    {
        var matches = snapshot.ExternalLogins
            .Where(login => string.Equals(
                login.Login.Provider,
                providerId,
                StringComparison.OrdinalIgnoreCase))
            .Select(login => login.Login)
            .ToArray();
        return matches.Length switch
        {
            1 => OperationResultFactory.Success(matches[0]),
            0 => OperationResultFactory.Fail<ExternalLoginKey>(
                HelloOidcErrors.ProviderUnavailable()),
            _ => OperationResultFactory.Fail<ExternalLoginKey>(
                HelloOidcErrors.AmbiguousProvider()),
        };
    }

    private bool HasAlternateMethod(
        SignInMethodSnapshot snapshot,
        ExternalLoginKey target)
        => (options.PasswordSignInEnabled && snapshot.HasPassword)
            || snapshot.ExternalLogins.Any(login =>
                login.Login != target
                && providers.IsEnabled(login.Login.Provider));

    private static bool HasProvider(
        SignInMethodSnapshot snapshot,
        string providerId)
        => snapshot.ExternalLogins.Any(login => string.Equals(
            login.Login.Provider,
            providerId,
            StringComparison.OrdinalIgnoreCase));

    private HelloOidcLinkedProvider[] ToLinkedProviders(
        SignInMethodSnapshot snapshot)
        => snapshot.ExternalLogins
            .GroupBy(
                login => login.Login.Provider,
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var configured = providers.TryGet(
                    group.Key,
                    out var provider);
                var logins = group.ToArray();
                return new HelloOidcLinkedProvider(
                    configured ? provider.Id : group.Key,
                    configured ? provider.DisplayName : group.Key,
                    configured,
                    logins.Length == 1
                        && HasAlternateMethod(
                            snapshot,
                            logins[0].Login),
                    logins.Min(login => login.CreatedAt));
            })
            .OrderBy(
                provider => provider.DisplayName,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static HelloOidcRegistrationHints CreateRegistrationHints(
        HelloOidcTicket ticket,
        HelloOidcProvider provider)
        => new(
            provider,
            ticket.Name,
            ticket.VerifiedEmail,
            ticket.Locale,
            ticket.ReturnUrl);

    private static HelloOidcProvider ToProvider(
        HelloOidcProviderRegistration provider)
        => new(provider.Id, provider.DisplayName);

    private static bool IsOnlyError(
        IReadOnlyCollection<Error> errors,
        string code)
        => errors.Count == 1
            && string.Equals(
                errors.First().Code,
                code,
                StringComparison.Ordinal);

    private static OperationResult<T> Fail<T>(
        Error error)
        => OperationResultFactory.Fail<T>(error);

    private static OperationResult<T> Fail<T>(
        IReadOnlyCollection<Error> errors)
        => OperationResultFactory.Fail<T>(errors);
}

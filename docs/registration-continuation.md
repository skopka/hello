# Host-owned registration continuation

A host that requires additional onboarding implements `IHelloRegistrationContinuation<TProfile>`. `PrepareProfileAsync` must put the durable pending marker in the profile before Identity registration; `IsPendingAsync` reads current durable state for the validated account. Registration stays atomic. Do not mark the account complete from client input alone.

Install `UseHelloRegistrationContinuation<TProfile>(options)` after routing and authentication and before protected host, Hello, Admin and authorization-server endpoints. Configure the local host-owned `ContinuationPath` and `DefaultReturnPath`. The `AuthenticationSchemes` list defaults to the Hello UI cookie; add every other host authentication scheme. A successful scheme must carry the online-validated access token as `access_token`; bearer headers are handled separately and take precedence. Unauthenticated requests continue to normal authorization. Hosts must not allow other pipelines or offline bearer validation to bypass this middleware.

Mark only exact necessary endpoints with `[HelloContinuationExempt]` or endpoint metadata: the host continuation endpoint, necessary sign-in/second-factor/registration, refresh and logout endpoints. A Razor page's metadata applies to the entire page, not an individual handler: do not exempt an account page merely to allow its logout handler. Host continuation mutations must require authentication and antiforgery. Keep required static resources accessible without granting access to protected endpoints. A pending HTML request is redirected to the host continuation page; bearer/JSON requests receive a forbidden ProblemDetails response. A failed status lookup fails closed.

The first local GET destination is stored in a protected, short-lived, Secure HttpOnly cookie bound to the validated account. After the host atomically marks onboarding done, its authenticated POST calls `HelloContinuationCompletion.CompleteAsync` and locally redirects to the returned destination. The helper rechecks current status and consumes the return flow once with `IHelloWebAuthnFlowStore`. A deployment with multiple instances must configure its shared atomic replay guard; the process-local fallback only protects a single instance. If no valid saved destination exists, the configured local fallback is returned. State and status are not encoded as client-controlled completion flags.

For example, a host can map a separate logout endpoint (partial composition example; `Profile` is the host profile type). Its form must include the host antiforgery token. This endpoint is explicitly exempt; do not exempt the account page itself:

```csharp
app.MapPost("/onboarding/logout", async (HttpContext http,
    IAntiforgery antiforgery, IHelloSessionCookieManager cookies,
    IHelloIdentityApplication<Profile> identity, CancellationToken ct) =>
{
    await antiforgery.ValidateRequestAsync(http);
    var refresh = cookies.ReadRefreshToken(http);
    if (refresh is not null)
        await identity.LogoutAsync(refresh, ct);
    cookies.DeleteSessionCookies(http);
    await http.SignOutAsync("Skopka.Hello.UI");
    return Results.LocalRedirect("/login");
}).RequireAuthorization().WithMetadata(new HelloContinuationExemptAttribute());
```

Use the host's real local login path and authentication scheme. Clearing local cookies does not guarantee server-side revocation if the logout operation fails; a host requiring confirmation should handle that result explicitly. Hosts with saved-account switching must also clear the corresponding saved-account entry.

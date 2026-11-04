# Native app sign-in

Native app sign-in lets an embedded WebView obtain its own normal Hello session
through an explicit approval in the system browser. It is disabled by default.
This is a Hello browser flow, not an OAuth grant. The system browser's existing
session remains unchanged; logging out of the WebView affects its own session.
The integration described here has not been verified on a specific native platform.

## Configure the ready Server

Set `SkopkaHello:NativeAppSignIn:Enabled` to `true` and configure
`SkopkaHello:NativeAppSignIn:AllowedCallbacks` as a mapping from each public
client id to an array of exact callback URIs. Each callback must also be a
registered redirect URI of that public client. The current client revision is
checked throughout the flow. Configure clients under the existing
`SkopkaHello:AuthorizationServer` client catalog, even if its `Enabled` flag is
false. Enabling native sign-in alone does not expose OAuth endpoints or require
OAuth signing certificates. Use HTTPS for `SkopkaHello:PublicOrigin` and enable
secure UI cookies.

Run the one-shot `--migrate` command before starting replicas. It migrates the
authorization storage and synchronizes configuration-owned clients when either
feature is enabled. All replicas need the same Data Protection key ring and the
same versioned persistent rate-limit HMAC keys. The server prunes expired flow
rows in bounded batches. Protect those keys as described in [deployment](deployment.md).

## Custom host

Register Identity and Hello, persistent rate limiting, UI, and authorization
client storage before mapping the UI. For example, with an existing EF context
that uses OpenIddict:

```csharp
// In AppAuthorizationDbContext.OnModelCreating:
modelBuilder.UseOpenIddict();
modelBuilder.AddHelloNativeAppFlows();

// connectionString, jwtKeys and rateLimitKeys come from secret configuration.
var identity = services.AddSkopkaHello<AppProfile>(o =>
    o.PublicOrigin = new Uri("https://login.example.com"));
identity.UsePostgreSql(connectionString);
identity.UseJwtSessions("v1", jwtKeys, o =>
{
    o.Issuer = "https://login.example.com";
    o.Audience = "example-app";
});
identity.UseHmacRateLimiting("v1", rateLimitKeys);
services.AddDbContext<AppAuthorizationDbContext>(o =>
{
    o.UseNpgsql(connectionString);
    o.UseOpenIddict();
});
services.AddOpenIddict().AddCore(o => o.UseEntityFrameworkCore()
    .UseDbContext<AppAuthorizationDbContext>());
services.AddSkopkaHelloAuthorizationClients(o =>
{
    o.Issuer = new Uri("https://login.example.com");
    o.Clients.Add(new HelloAuthorizationClientOptions
    {
        ClientId = "native-app",
        DisplayName = "Example app",
        Type = HelloAuthorizationClientType.Public,
        RedirectUris = ["com.example.iqzone:/signin", "https://app.example.com/signin"],
        Scopes = ["openid"],
    });
});
services.AddHelloNativeAppSignIn<AppProfile, AppAuthorizationDbContext>(o =>
{
    o.Enabled = true;
    o.AllowedCallbacks["native-app"] =
        ["com.example.iqzone:/signin", "https://app.example.com/signin"];
});
services.AddSkopkaHelloUi<AppProfile, AppProfileUiFactory>();

// For a host serving both Bearer APIs and cookie-authenticated Blazor:
services.AddAuthentication(o => o.DefaultScheme = "host-auth")
    .AddPolicyScheme("host-auth", null, o => o.ForwardDefaultSelector = context =>
        context.Request.Headers.Authorization.ToString()
            .StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? "Bearer" : HelloUiDefaults.AuthenticationScheme);
services.AddCascadingAuthenticationState();
services.AddRazorComponents().AddInteractiveServerComponents();
```

Call `UseAuthentication`, `UseAuthorization`, and `UseAntiforgery` in that order,
then map Hello and the host’s Razor components with interactive server render mode.
The full reload after completion starts a new circuit under the UI cookie.

The host must register `IIdentityRateLimiter<AppProfile>`, configure shared Data
Protection keys and HTTPS secure cookies, generate and apply its own EF migration
for the native flow model, synchronize configured authorization clients during
its migration process, and schedule bounded calls to
`IHelloNativeAppFlowStore.PruneAsync`. Configure each allowed callback as a
registered redirect URI. This clients-only setup does not register the full
OpenIddict authorization server.

## WebView and native shell

The shell generates a cryptographically random verifier and state in memory and
computes the S256 challenge. It loads `GET {prefix}/native-sign-in` in the
original WebView, obtains the antiforgery token, and submits a same-origin POST
to `?handler=Start` with `clientId`, `callback`, `state`, `codeChallenge`, and an
optional local `returnUrl`. It opens the returned `launchUrl` at the fixed Hello
origin in the system browser. The fragment contains a short-lived launch
secret: fragments are not sent in HTTP requests, but native URL handlers and
logs must redact the entire launch URL and request bodies. The browser clears the
fragment before attaching, signs in through normal Hello UI if necessary, and
asks the user to confirm the account.

After approval, the callback fragment carries the original state and a short-lived,
one-use completion receipt. The shell compares the returned state with its own
in-memory value, then uses the original WebView to POST `?handler=Complete`
with `codeVerifier`, `completionReceipt` and an antiforgery token. Status and
cancellation use `?handler=Status` and `?handler=Cancel` with the same WebView
binder and antiforgery protection. Status never returns the receipt. A party that
only initiated the flow cannot redeem another person's approval without also
receiving the callback receipt. Approval is short-lived and consumption is
single-use. Prefer verified app links or universal links with platform domain
association; custom schemes can be intercepted by another app. Treat the callback
fragment as secret and never log callback URLs.

The WebView receives its own normal encrypted UI ticket and refresh cookie;
there is no native HTTP cookie jar or token transfer. Reload the full page after
completion so server-rendered Blazor creates a fresh authenticated circuit.

Use a typed native bridge restricted to the fixed trusted origin and the expected
callback. Never take the verifier or expected state from a URL or allow arbitrary
web content to invoke the bridge. Do not persist them or log cookies, tokens,
launch or callback URLs, receipts, OTPs, or request bodies. OAuth scope permissions do not restrict the
normal cookie's access to the site; authorize every site operation independently.

The default overall lifetime is ten minutes; the launch secret and approved
result each expire after one minute. A flow moves from pending to browser
attached, approved, consuming and completed, or to cancelled/expired. Durable
state survives a server restart, but a lost WebView binder or in-memory verifier
requires a new flow. A failure after consumption has begun can leave the outcome
uncertain; start a new flow rather than replaying completion.

The Hello security event sink receives `hello.native_app.begun`, `attached`,
`approved`, `denied`, `completed`, `cancelled`, `rejected`, and `rate_limited`
events. Delivery is best effort after the relevant Hello state change; do not
assume it is atomic with Identity session creation.

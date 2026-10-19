# Phone-only SQLite Blazor sample

This .NET 10 Blazor Web App uses static server rendering, SQLite Identity
persistence, Hello Razor Pages and a Development-only SMS logger. It turns off
password sign-in and registration. The root page uses `AuthorizeView`; `/private`
uses `[Authorize]` and reads `AuthenticationState`.
Phone-only accounts have no user-name claim; read
`HelloUiDefaults.DisplayNameClaimType` from the authenticated principal to show
the profile display name.

After Skopka.Identity 0.15.0 is available to NuGet restore, run:

```powershell
dotnet restore .\Skopka.Hello.slnx --configfile .\NuGet.Config
dotnet run --project .\samples\Skopka.Hello.PhoneSqliteBlazor -- --environment Development
```

Open `/hello/login`, enter a `+7` phone number, and read the code from the
Development SMS log. The first successful verification asks for a display name;
later verifications sign in directly. Open `/` and `/private` to see the same
authenticated principal in Blazor. The sample migrates its SQLite database in
Development; for production, apply migrations as a separate deployment step.

`Program.cs` demonstrates the required service order: configure Hello and
Identity with `.UseSqlite()`, HMAC one-time codes, persistent HMAC rate limits
and JWT sessions; register Hello UI with `LayoutPath`; select the UI cookie for
browser requests and Bearer for API requests; call
`AddCascadingAuthenticationState()`, `AddRazorComponents()`, `UseAuthentication()`,
`UseAuthorization()`, `UseAntiforgery()`, `MapSkopkaHelloUi()` and
`MapRazorComponents<App>()`. The host layout lives at
`/Pages/Shared/_HostLayout.cshtml`.

The sample generates throwaway keys on each Development start. Set three
different Base64 values of at least 32 random bytes in
`PhoneSample:VerificationKey`, `PhoneSample:RateLimitKey`, and
`PhoneSample:JwtKey` for a stable host. Production also needs a real SMS
provider, persisted Data Protection key ring, HTTPS, durable rate-limit storage
and an appropriate public origin. The development logger rejects use outside
Development.

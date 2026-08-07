# Same-origin SPA reference client

The sample host serves a small framework-free client at `/app`. It demonstrates
the browser contract of `Skopka.Hello.Endpoints` without adding OAuth/OIDC
protocol behavior to JavaScript:

- provider discovery and external sign-in;
- atomic external registration with the sample's exact `SampleProfile` shape;
- password sign-in for an existing account;
- external-login linking and unlinking through Identity-owned OTP step-up;
- authenticated antiforgery renewal before account mutations.

The access token stays only in the main tab's JavaScript memory. Provider
navigation runs in a popup opened with `noopener`. The callback URL contains a
random channel identifier, not a token, and signals the original tab through a
same-origin `BroadcastChannel`. Refresh, OIDC and antiforgery cookies retain
their server-configured protection.

The host exposes `/app/config` so the client reads the configured antiforgery
request-cookie and header names instead of duplicating server options. Host
applications can copy the `wwwroot/app` assets and map an equivalent safe
configuration response.

Configure PostgreSQL, versioned JWT/rate-limit/OTP keys and at least one OIDC
provider as described in the repository
[getting-started guide](../../docs/getting-started.md), then start the sample
host and open its `/app` path. Provider callback origins must match
`SkopkaHello:PublicOrigin` exactly.

## Browser tests

Build the solution before installing the browser version pinned by Playwright:

```powershell
dotnet build .\Skopka.Hello.slnx -c Release
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Skopka.Hello.BrowserTests\bin\Release\net10.0\playwright.ps1 install chromium
dotnet test .\tests\Skopka.Hello.BrowserTests -c Release --no-build
```

On hosts with PowerShell 7, replace `powershell.exe -NoProfile
-ExecutionPolicy Bypass -File` with `pwsh`. CI installs Chromium and its Linux
dependencies automatically.

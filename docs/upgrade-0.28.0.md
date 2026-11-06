# Upgrading an in-process host from 0.25.1 to 0.28.0

This is the upgrade plan for the local 0.28.0 candidate, including IqZone.
It does not authorize package publication or application deployment.

## Dependencies and database

Update all installed `Skopka.Hello.*` references together to `0.28.0`, and all
direct `Skopka.Identity.*` references to `0.17.0`. The Hello family still contains
six packages; `Skopka.Hello.Server` is the executable/container, not a seventh
NuGet package. An in-process host does not need to switch to the Server image.

Run the selected Identity provider's migrations before starting the updated
host. PostgreSQL and SQLite add `identity_sessions.assurance`, with `0` for
existing sessions. The migration does not delete accounts, credentials or
sessions. Existing sessions do not acquire an MFA assertion retroactively.
When the host requires MFA, those sessions must authenticate again.

Retain the existing Data Protection key ring, token signing keys, OTP keys and
rate-limit keys. All replicas must share the relevant keys and persistent
replay/rate-limit storage. Do not replace keys just to perform this upgrade.

Custom Identity stores must implement the new guarded TOTP enrollment/reset
operations and atomic verified-phone binding. Session stores must preserve and
return the logical session assurance; unsupported custom implementations fail
closed. See Identity's `docs/building-web-hosts.md` and `docs/security.md`.

## Required authenticator sign-in

Register the host's live second-factor policy and configure TOTP, its secret
protector and persistent rate limiting. IqZone should require the factor for
its administrator, manager, author and teacher roles: `iq-admin`, `iq-manager`,
`iq-author`, `iq-teacher` in the inspected IqZone source. The policy must use current
server-side membership, not roles
submitted by the client or a stale browser ticket.

The policy is optional: without it, existing sign-in behavior is retained.
With it, password, phone, passkey and external login stop after the first factor
until the authenticator or a recovery code is verified. An account without an
authenticator enrolls using that freshly verified first factor, saves recovery
codes, then completes sign-in. No access token, refresh token or Hello UI session
is issued by the intermediate steps.

CLI/API clients must handle the required-second-factor response and complete
the challenge; treating every non-success response as a bad password is no
longer sufficient. See [mandatory second factor](mandatory-second-factor.md)
for the exact public contract and requests.

The live policy also applies to online session validation and refresh. External
resource servers that validate self-contained JWTs offline can observe policy
or revocation changes only when the short-lived token expires.

An IqZone-specific policy can use its existing Identity role service (partial
host example; keep the existing `IqZoneProfile` and `IqZoneRoles` types):

```csharp
public sealed class IqZoneSecondFactorPolicy(
    IIdentityRoleService<IqZoneProfile> roles)
    : IHelloSignInSecondFactorPolicy<IqZoneProfile>
{
    public async Task<bool> RequiresAsync(
        IdentityUser<IqZoneProfile> user, CancellationToken cancellationToken)
    {
        var assigned = await roles.GetUserRolesAsync(user.Id, cancellationToken);
        // A failed role lookup must not silently turn off the second factor.
        return !assigned.IsSuccess || assigned.Value.Any(role => role.Name is
            IqZoneRoles.Admin or IqZoneRoles.Manager or
            IqZoneRoles.Author or IqZoneRoles.Teacher);
    }
}

builder.Services.AddScoped<
    IHelloSignInSecondFactorPolicy<IqZoneProfile>, IqZoneSecondFactorPolicy>();
```

Namespaces: `Skopka.Hello`, `Skopka.Identity.Users`, `Skopka.Identity.Roles`,
`Microsoft.Extensions.DependencyInjection`, and the host's profile namespace.
Do not reuse `StaffAccess.SeesEverythingAsync` for this rule: the current IqZone
implementation covers author/teacher access, not all four staff roles.

## Phone registration and binding

Add `IHelloUiRegistrationFields` to `IqZoneProfileUiFactory`. Describe the
existing `grade` field as optional, with the hint
“Не школьник — оставьте пустым”, validate an empty value or an integer from 1 to
11, and pass it into `IqZoneProfile.Grade` from `HelloUiRegistrationProfile.Values`
inside `Create`. Keep the existing consent enrichment and profile editor.
The runnable PhoneSqliteBlazor sample demonstrates this contract.

To request binding immediately after sign-in, use the safe local return URL
`<UI prefix>/account#phone-binding`. The API operations are
`POST /account/phone/binding` and `POST /account/phone/binding/complete`.
They use the same `PhoneSignIn` purpose as phone login, including a host's
Development-only fixed-code provider. A fixed code must remain disabled in
production. A number owned by another account is reported only after valid proof.
See [phone binding and registration fields](phone-binding.md).

## Existing authenticator enrollment clients

Since 0.25.2, account enrollment requires a fresh primary-factor proof before
revealing the QR code or secret. Clients written against 0.25.1 must supply the
password or supported passwordless proof, then submit the returned enrollment
ticket when confirming. Refreshing the access token requires restarting that
account-enrollment flow. The new mandatory sign-in enrollment has a separate,
restricted continuation; it does not create a temporary authenticated session.

Administrative reset is version-checked and bound to the exact factor. It
removes recovery codes and invalidates old sessions. Under the host policy,
the employee must enroll a new authenticator at the next sign-in.

## Other changes since 0.25.1

- Native app sign-in remains opt-in, client-specific, and separate from an
  OAuth grant. See [native app sign-in](native-app-sign-in.md).
- Hosts may replace only email delivery while retaining Hello's rendering and
  queue. Existing SMTP setup remains valid. See [email transport](email-transport.md).
- Admin client details and actions use dialogs. Review host CSS that targets
  the former expanded forms. Successful action submissions now redirect to GET;
  generated secrets are retrieved once from server-side state.
- JSON UI dictionaries must put translations inside `texts`; malformed root
  properties are rejected. Keep both English and Russian host overrides current.

## Local validation and later release

Until Identity 0.17.0 is published, restore the candidate from locally packed
packages without committing a private source into `NuGet.Config`:

```powershell
dotnet restore .\Skopka.Hello.slnx --configfile .\NuGet.Config `
  -p:RestoreAdditionalProjectSources=H:/Rep/skopka/identity/artifacts/packages-0.17.0
```

Publication must later proceed Identity first, then all six Hello packages and
the Server image. Only after package availability is verified should an
application upgrade be prepared. No tag or application deployment is part of
this candidate's local verification.

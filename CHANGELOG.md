# Changelog

## 0.26.0

- Add optional `HelloUiProfileField.Hint`. The account page renders it under the profile input as `.hello-field-hint` and links it with `aria-describedby`, so screen readers announce it as the field description. Fields without a hint render unchanged markup.
- The hint is host-owned text like `Label`; Hello does not localize it. It is an init-only property, so existing positional `HelloUiProfileField` construction keeps compiling and binding.
- The phone-only SQLite Blazor sample now implements `IHelloUiProfileEditor<TProfile>` and shows a display-name hint.

## 0.25.0

- Add OTP-protected OAuth client administration through Admin API and Razor. Client IDs are immutable; scopes and audiences are chosen from the configured server catalog. Confidential client secrets are returned once on creation, rotation, or conversion from public; conversion to public removes the secret.
- Synchronization preserves administrator-owned and unmatched legacy registrations, adopts legacy IDs matching configuration, and rejects collisions with administrator-owned clients before modifying registrations. Removing a configuration-owned client still removes its grants.
- Persist client scopes, resource and reauthentication policy in OpenIddict so administrator-created clients work after restart. Authorization code with mandatory PKCE and the existing refresh flow remain the only supported grants.
- Protect changes with live administrative policies, antiforgery on Razor forms, action-bound step-up, optimistic concurrency and audit events without secrets. Configuration-owned and unmatched legacy clients are read-only in Admin.
- Existing authorization codes and refresh grants issued before client revision tracking lack the required revision and will be rejected after upgrade; affected users must sign in again. Client changes invalidate existing grants.
- Publish all six Hello packages and the Server image together as 0.25.0. See `docs/skopiclub-auth-integration.md` for the verified host upgrade steps; package publication does not deploy production.

## 0.24.1

- Apply the shared phone policy to phone changes and registration as well as OTP requests; distinguish invalid numbers from disallowed country codes in localized UI and API errors.
- Correct and localize consent labels and client validation.
- Existing incorrectly stored phone numbers are not migrated automatically; operators must correct them separately.

## 0.24.0

- Phone registration respects the existing Locale field mode, derives hidden locale from the request culture, and uses the shared legal-consent markup and field errors. The locale field now defaults to hidden; select Optional or Required to retain it.
- Opt-in `DefaultPhoneRegion = "RU"` normalizes Russian phone OTP input before policy and Identity checks; other phone operations and the default strict E.164 behavior are unchanged.
- Localized phone validation and client-side required messages. Hosts may remove custom locale-hiding, consent-layout and OTP phone-normalization CSS/JS workarounds.

## 0.23.0

- Added passwordless phone OTP sign-in and atomic verified-phone registration through Skopka.Identity 0.15.0.
- Added phone request, verification and registration APIs and a localized Razor login flow.
- Added host SMS templates, Development-only logging provider, SMS budget and pre-send policy extension.
- Added a SQLite static-rendered Blazor Web App sample and password-disabled host mode.

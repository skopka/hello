# Changelog

## 0.28.1

- Add optional `SkopkaHelloUiOptions.PostLogoutRedirectPath` and ready Server configuration for a local destination after explicit sign-out, sign-out everywhere, or current-session revocation. Default Login/account-chooser redirects remain unchanged.
- Prepare required second-factor verification or enrollment during password and phone first-factor submissions, so the redirected page immediately shows the code input or enrollment QR and key. Reloading within the pending expiry preserves the step.

## 0.28.0

- Redirect successful client administration submissions to a GET and show generated secrets once from server-side state.
- Add host-required authenticator verification before issuing a session, with recovery-code support and first-factor-authorized enrollment for accounts without an authenticator.
- Add host-defined phone registration fields and verified phone binding through the existing phone sign-in code flow.
- Bind administrative authenticator reset to the user version and exact enrollment, remove recovery codes and invalidate existing sessions.
- Require Skopka.Identity 0.17.0 and its session-assurance migration. Existing sessions have no asserted second factor; accounts subject to the host rule must sign in again.

## 0.27.1

- Shorten the Admin client revision label and separate read-only URI labels from input hints in English and Russian.
- Display read-only client addresses one per line, show an em dash for empty address lists, and keep client details in one continuous list.

## 0.27.0

- Add a replaceable email transport for the built-in account-message renderer and delivery pipeline, including scoped transports and retryable/permanent delivery outcomes.
- Add optional native app sign-in through a system browser, with a separate WebView session, durable single-use flow and explicit account approval.
- Make authorization-client administration compact, with accessible creation and management dialogs and catalog-backed scope selection.

## 0.25.2

- Require fresh primary authentication before revealing an authenticator enrollment secret or QR code: password accounts re-enter their password; passwordless accounts use a verified phone, passkey or linked external provider.
- Bind protected, single-use enrollment proofs and confirmation tickets to the exact access token, with independent rate limits and a maximum five-minute ticket lifetime. Refreshing the access token requires restarting enrollment.
- HTTP clients must send `password` or a fresh `proof` when starting enrollment and the returned `enrollmentTicket` when confirming it. Previously started enrollments must be restarted.

## 0.25.1

- Fix missing English and Russian Admin client translations and localize the client ID, client type and yes/no labels.
- Hide the empty validation summary on the Admin clients page when there are no errors.
- Reject unknown and duplicate root properties in UI localization dictionaries. Move any translation keys at the root into `texts`.

## 0.25.0

- Publish NuGet packages through Trusted Publishing using short-lived credentials.

- Add optional `HelloUiProfileField.Hint`. The account page renders it under the profile input as `.hello-field-hint` and links it with `aria-describedby`, so screen readers announce it as the field description. Fields without a hint render unchanged markup.
- The hint is host-owned text like `Label`; Hello does not localize it. It is an init-only property, so existing positional `HelloUiProfileField` construction keeps compiling and binding.
- The phone-only SQLite Blazor sample now implements `IHelloUiProfileEditor<TProfile>` and shows a display-name hint.

- Show Admin Users and Roles as compact, left-aligned rows; details and actions open in dialogs. Hosts with custom Admin CSS should review selectors that targeted the former cards, grid or collapsed panels.
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

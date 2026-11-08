# Changelog

## 0.32.0

- Require Skopka.Identity 0.19.1 and apply the `PhoneScopes` migration of 0.19.0 (the Hello server image applies pending Identity migrations at startup). The migration cannot be reverted while two accounts share a number.
- Add `HelloSiteOptions.PhoneScope`: one phone number may belong to one account per site scope, for example a personal account on the main site and a staff account on the staff site. Code sign-in, password sign-in by number (`PasswordLoginHandle.Phone` and `Automatic`), registration (by code, password or external sign-in), phone binding by code, delegated phone registration and verification and queued phone confirmation find and create accounts in the current site's scope. `null` (the default) means the default scope `""`, which Hello passes explicitly to password sign-in so that `Automatic` does not search every scope; on binding, `null` keeps the account's current scope. Hosts without scopes are unchanged. See `docs/public-sites.md`.
- Answer Admin role member lists and role badge filters with Identity's role-membership query and count instead of paging through accounts: no "Continue search" there, and the Roles page shows the member count and the badge filter the number of matches. Host-decided badges keep the bounded scan (`MembershipScanLimit`).
- Make the Roles page "add an account" search an ordinary paged search: accounts already in the role are marked and cannot be added again.
- Add `HelloAdminUserBadge.AlsoFromHost`. A badge with roles now ignores the badge source's keys unless it sets this flag; such a badge is filtered by the scan.
- Add `HelloAdminQueryUsersCommand.RoleIds`, `IHelloAdminApplication.CountUsersAsync`, the repeated `roleId` parameter of `GET /admin/users` and `HelloAdminUser.PhoneScope`, shown read-only in the Admin Manage dialog when it is not the default.
- Fix the Roles page "role already removed" outcome, which could claim a removal when the member was on a later page.

## 0.31.0

- Add `IHelloDelegatedPhoneVerification<TProfile>`: a signed-in actor proves possession of a phone number that may already belong to another account. After the correct code the host receives a protected one-use `HelloPhoneOwnershipProof` with the holder of the confirmed number (`AccountId`) and either registers a new account (`RegisterAsync`) or redeems it for linking (`RedeemAsync` → `HelloPhoneOwnership`). Occupancy is still disclosed only after the correct code; codes, budgets, limits and `IHelloDelegatedPhoneRegistrationPolicy<TProfile>` are shared with delegated registration. The proof is bound to the actor and access token, expires with the Identity proof (`VerificationOptions.ProofLifetime`, two minutes by default) and is spent atomically by Skopka.Identity. `IHelloDelegatedPhoneRegistration<TProfile>` is unchanged. See `docs/host-sms.md`.

## 0.30.0

- End every Hello.UI and Hello.Admin form submission with `303 See Other` to a GET, on success, error and intermediate steps. Refresh and Back no longer resubmit; errors, typed non-secret values and the current step survive refresh, while passwords and codes are never kept. Outcomes are process-local and bound to the browser and account; multi-instance hosts need session affinity. Native app sign-in approval keeps its one-time hand-off response. See `docs/form-submissions.md`.
- Make a repeated Admin step-up confirmation (double click, Enter and a click) end like the first: the action runs once and every duplicate gets the same success. A confirmation used elsewhere is explained from the current state ("the role is already assigned") instead of only "invalid or expired"; the restart message now asks to check the state.
- Return to the context after an Admin confirmation: the same user's Manage dialog, role dialog or client card, keeping search, filters and page.
- Add host-declared Admin user badges (`SkopkaHelloAdminOptions.UserBadges`, `HelloAdminUserBadge`, optional `IHelloAdminUserBadgeSource`) shown in the user list and offered as a `Badge` filter.
- Show role members on the Roles page with search and pages, and add or remove members there with the same step-up and per-role assignment rules as on the Users page. Badge filters and member lists examine at most `SkopkaHelloAdminOptions.MembershipScanLimit` accounts per request (default 500) and offer to continue.

## 0.29.1

- Redirect access-link form submissions to GET, preserve an active authenticator setup across refresh, and show recovery codes only once while keeping the completion message visible.
- Use the standard Hello form styles, localize access-link failures, distinguish an expired authenticator setup from an invalid link, and let users restart setup without resetting the attempt limit.

## 0.29.0

- Add host-issued one-use access links with independent lifetime and site-bound password and authenticator setup, plus optional revocation of previous credentials and sessions.
- Support multiple configured public origins with per-site feature and sign-in controls, trusted session scope, host account admission and account-specific session lifetimes. Existing sessions without a site context require a fresh sign-in when explicit sites are enabled.
- Add per-account self-service authorization and a host-owned mandatory registration-continuation gate for Razor and API requests.
- Add bounded host SMS through the configured delivery channel and shared budgets, plus authenticated delegated registration after phone proof.
- Require Skopka.Identity 0.18.0 and apply its session-context, access-link and device-flow migrations before enabling these features.

## 0.28.4

- Give the account phone-binding code button its own localization key, `PhoneBinding.RequestCode` ("Send code" / "Получить код"), so hosts can override it independently of the sign-in button.

## 0.28.3

- Let users cancel the phone-binding code step and enter another number without changing their account. After successful binding, return to the phone section and show a one-time localized success message.

## 0.28.2

- Require SMS verification of the new number for account phone additions and replacements. Remove `PUT /account/phone` and the direct Razor edit; the retained application method fails with `hello.phone_binding.required`.
- Separate account phone visibility from verified binding through `Account.PhoneBindingEnabled` (default `true`), including hosts using `Account.Phone = ReadOnly`. Show Add phone or Change phone based on the current account.

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

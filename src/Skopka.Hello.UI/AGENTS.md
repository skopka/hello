# UI Module Instructions

Read `../../AGENTS.md` first.

The implemented UI contains Razor pages for registration, login, account
summary, active-session revocation and OTP-protected password change. Pages
call the shared application service, contain no identity business logic and
protect every form mutation with antiforgery.

Recovery and confirmation token pages are no-store/no-referrer. A GET may
render a token form but must never confirm an address or reset a password;
mutation happens only through an antiforgery-protected POST.

The protected UI cookie contains an encrypted authentication ticket, never a
plain refresh token. Online access-token validation and refresh rotation happen
in the cookie event handler. Keep the UI authorization policy on all account
pages.

The optional custom CSS endpoint serves one explicitly configured file and
must not expose or browse the mounted directory. CSS custom properties are the
theming contract, built-in styles can be disabled, and custom CSS is linked
after built-in styles.

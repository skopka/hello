# UI Module Instructions

Read `../../AGENTS.md` first.

The implemented UI contains Razor pages for registration, login, account
summary and active-session revocation. Pages call the shared application
service, contain no identity business logic and protect every form mutation
with antiforgery.

The protected UI cookie contains an encrypted authentication ticket, never a
plain refresh token. Online access-token validation and refresh rotation happen
in the cookie event handler. Keep the UI authorization policy on all account
pages.

The optional custom CSS endpoint serves one explicitly configured file and
must not expose or browse the mounted directory. CSS custom properties are the
theming contract, built-in styles can be disabled, and custom CSS is linked
after built-in styles.

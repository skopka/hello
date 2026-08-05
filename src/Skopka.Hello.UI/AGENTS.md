# UI Module Instructions

Read `../../AGENTS.md` first.

The implemented UI contains Razor pages for password and external registration
and login, account summary, active-session revocation, external-login management
and OTP-protected password change. Pages call shared application services,
contain no identity business logic and protect every form mutation with
antiforgery.

Recovery and confirmation token pages are no-store/no-referrer. A GET may
render a token form but must never confirm an address or reset a password;
mutation happens only through an antiforgery-protected POST.

The protected UI cookie contains an encrypted authentication ticket, never a
plain refresh token. Online access-token validation and refresh rotation happen
in the cookie event handler. Keep the UI authorization policy on all account
pages.

OIDC completion GET requests only render the same-origin continuation form;
they never consume provider state or mutate identity. Link/unlink UI must not
render provider subjects or tokens. Successful link/unlink replaces the local
ticket and transport cookies with the fresh session returned by the shared
operation.

The optional custom CSS endpoint serves one explicitly configured file and
must not expose or browse the mounted directory. CSS custom properties are the
theming contract, built-in styles can be disabled, and custom CSS is linked
after built-in styles.

Hello pages live under the collision-resistant `/SkopkaHello` Razor page-name
namespace. Their selectors are replaced by the DI-configured UI prefix through
the exact RCL page-route convention. Do not reintroduce absolute `@page
"/hello"` templates or raw route constants in links/redirects; use `asp-page`
and `RedirectToPage`. Password and external registration selectors and links
must be absent when the shared self-registration policy is disabled.
`SkopkaHelloUiOptions.EnabledPages` additionally controls which page groups
receive selectors. Disabled pages remain packaged but must have no HTTP route,
link or reachable handler through an enabled page. Preserve the declared Login
and Account dependencies and use the configured local authenticated redirect
when Account is not available.

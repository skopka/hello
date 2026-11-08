# Form submissions, refresh and repeated confirmation

Since 0.30.0 every form submission on a Hello Razor page — Hello.UI and
Hello.Admin — ends with a redirect to a GET. This covers success, validation
errors, domain errors and intermediate steps such as "code sent, enter the
code". Refresh and Back never show the browser's "Resubmit the form?" dialog
and never repeat the submission. Hosts configure nothing.

## What the person sees

- After an error the page shows the same errors next to the fields and in the
  summary, in the page language. Fields the person typed stay filled.
  Passwords and codes (`Password`, `ConfirmPassword`, `CurrentPassword`,
  `NewPassword`, `Code`, `VerificationCode`, `SecondFactorCode`,
  `BindingCode`, `TotpCode`, `RecoveryCode`, `UserCode` and the passkey
  assertion) are never kept; the person types them again.
- A multi-step flow (sign-in code, second factor, phone binding, authenticator
  setup, cross-device sign-in, Admin confirmation) stays on its step across
  refresh until the step expires, is cancelled or is restarted.
- A success message is shown once.
- The redirect target is the same page without its `handler`, plus the values
  the page accepts on GET (such as `ReturnUrl` or the device request being
  waited for) and an opaque `hello-prg` reference. Secrets and tokens are
  never added to the address.

## How it works

A Hello.UI or Hello.Admin POST handler that renders its page is rendered once,
with typed secrets removed first, and the rendered response (status, headers,
body) is kept server-side. The response becomes `303 See Other`. The GET it
points to returns the kept outcome before the page handler runs; authorization
and per-site page availability are still checked first. A later submission on
the same page from the same browser replaces the kept outcome, so Back after a
successful submission renders the page afresh.

The outcome is bound to the browser by an `HttpOnly`, `SameSite=Lax` cookie
named `__Host-Skopka.Hello.UI.Prg` (`Skopka.Hello.UI.Prg` when
`SkopkaHelloUiOptions.SecureCookies` is `false`) and to the signed-in account.
Another browser, or another account on the same browser, gets the page
afresh. Kept outcomes expire after ten minutes and are bounded to 64 MiB per
process; when the bound is reached, the GET renders the page afresh instead.
The replay response is always `no-store`.

Admin pages redirect explicitly: their errors and the non-secret values typed
into the failed form are kept for the GET, which reloads the list from current
data. They stay visible on refresh until the next submission on that page.

## Repeated confirmation in Admin

Submitting one step-up confirmation twice — Enter and a click, a double click,
a password manager submitting the form — ends like a single submission. The
action runs once; the duplicate waits for it and receives the same success.
A duplicate arriving later, within ten minutes, also receives the remembered
success. The pending "Confirm: …" button disappears and the confirmation
dialog does not reopen. The Admin script additionally ignores a second submit
of a form that is already being sent.

When a confirmation's challenge was already used or has expired and the
outcome is not remembered (another server instance, a restart), the page
compares the intended result with the current state. "The role … is already
assigned", "… already done" and similar messages tell the person that nothing
needs repeating; otherwise the page says the action was not completed and asks
to check the current state and start again.

## Returning to the context

After a confirmation the person returns to where the action started, not to the
general list:

- Users: the "Manage" dialog of the same user, with the updated data and the
  success message, keeping search, status, badge filter and page
  (`Manage={userId}`).
- Roles: the dialog of the same role with its members, keeping the role search
  and page and the member search and page (`Open={roleId}`). After deleting a
  role the list is shown.
- Clients: the card of the changed client (`Edit={clientId}`), except after
  deletion. A newly generated secret is shown first, once.

When the user no longer matches the list filter (for example after blocking
with `Status=Active`), the list shows the success message without a dialog.

## Hosting requirements

Kept outcomes, pending Admin confirmations and remembered confirmation results
are process-local, like the [access-link](access-links.md) state since 0.29.1.
A multi-instance host needs session affinity for Hello pages. Without affinity a
redirected GET that reaches another instance renders the page afresh: errors
and the current step are not shown, and the person submits again. A duplicate
confirmation that reaches another instance gets the explanation from current
state described above. TempData uses the host's TempData provider (the cookie
provider by default) and Data Protection; shared Data Protection keys are
required as before.

A host page that posts its own forms is not affected; only pages under the
Hello UI and Admin prefixes use this behavior.

## Exceptions

- Script requests that answer with JSON, such as the passkey challenge, are not
  form submissions; they neither redirect nor replace a kept outcome.
- Native app sign-in approval (`NativeSignIn/Browser`) answers its confirmation
  with a one-time hand-off page that immediately opens the app callback, and a
  refused request with ProblemDetails. Its flow is consumed by the first
  submission, so a repeated submission is refused and changes nothing; the
  hand-off is never kept server-side because it carries a one-time receipt.

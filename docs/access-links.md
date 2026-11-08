# Host-issued account access links

Access links let a trusted host deliver a one-use account setup or recovery URL itself. Identity owns the link, its separate lifetime (seven days by default, at most 30 days), password change, authenticator enrollment, recovery codes and atomic consumption. Issuing a new link invalidates an earlier link. `InvalidateExisting` also immediately revokes the previous password, authenticator and sessions for recovery; leave it false for an invitation. `identity.access_link.used` is a post-commit security event, not a durable audit record.

Enable Identity's `AddAccessLinks()` after configuring relational persistence, a password hasher and TOTP; call `services.AddHelloAccessLinks<TProfile>()` **before** `AddSkopkaHelloUi`. Configure a trusted HTTPS public origin (or an allowlisted site with its own origin), Data Protection shared across replicas, the Hello UI and a persistent rate limiter. The following is an excerpt from an existing configured host:

```csharp
identity.AddAccessLinks(options => options.Lifetime = TimeSpan.FromDays(7));
services.AddHelloAccessLinks<MyProfile>();
services.AddSkopkaHelloUi<MyProfile, MyProfileFactory>();
```

A trusted host obtains `IHelloAccessLinks<MyProfile>` and calls `IssueAsync(userId, expectedVersion, siteId, invalidateExisting, cancellationToken)`. For the single-origin configuration, the site id is `default`. The result contains the secret `Url` and expiry; deliver it privately. Never expose issuance as an anonymous endpoint. The host must authorize each issuance and obtain the current optimistic user version itself. Site ids and origins come from trusted configuration, not an arbitrary request host.

Successful completion returns `HelloCompletedAccessLink<TProfile>` with the safe
Hello account projection and one-time recovery codes, without Identity's security stamp.

The secret is delivered in the URL fragment. JavaScript removes it from the address bar and submits an antiforgery-protected, nonmutating inspection; JavaScript is required. A subsequent antiforgery-protected POST starts authenticator setup; completion requires the new password and a current authenticator code. Recovery codes are displayed once, then the user can sign in at that same site. Expired, superseded, used and wrong-site links are refused. This flow does not change the separate password-reset lifetime or automatically sign in the browser.

The link URL contains a secret fragment, which the browser does not send in an HTTP request. Configure application hosting diagnostics, reverse proxies, tracing and error reporting to suppress or redact request bodies for the access-link route; never record the URL, password, authenticator key, code or recovery codes. The page sets `no-store` and `Referrer-Policy: no-referrer`; those headers do not sanitize upstream logs. Shared Data Protection keys must be protected and retained for the enrollment's lifetime.

## Browser retries and refresh (0.29.1)

Form submissions redirect to GET. Refresh retains a live authenticator setup
without repeating inspection, enrollment or completion POSTs. Passwords and entered
codes are cleared; they are not stored for the redirect. Recovery codes are shown
on the first successful GET only. A later refresh keeps the completion message
and sign-in link, without displaying the codes again.

The authenticator setup expires independently of the access link. An expired setup
returns to the start step; a failed code or setup replaced in another tab offers
localized retry guidance and a restart action. Restarting creates a new QR/key and
does not reset Identity's attempt limit. Scan the new QR before trying again.
Host dictionaries can override `AccessLink.Retry`, `AccessLink.EnrollmentExpired`,
`AccessLink.Restart` and `AccessLink.Success` independently.
The generic Identity error alone does not distinguish a wrong code,
expired/replaced setup, concurrent submission or an unavailable protection key.

The browser flow uses the existing process-local `HelloUiPrgStateStore`, with an
opaque protected TempData reference. Active UI state is bounded to thirty minutes
(completion and one-time recovery display to ten minutes); Identity still checks
the link and enrollment expiry. Multi-instance hosts need affinity for this UI
state, in addition to shared Data Protection. After a process restart or lost UI
state, reopen the original link while it is still valid.

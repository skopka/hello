# Host policy example

This is a compiled policy library, not a runnable web application. It shows site
admission, mandatory staff TOTP, account restrictions, durable onboarding markers,
delegated child creation (the same policy also gates delegated phone verification
for linking an existing child account) and account-specific session expiry. Wire it into an
existing Hello host with `services.AddExamplePolicies()` and configure the
`personal` and `staff` site ids as in [public sites](../../docs/public-sites.md).

Restore and build with the repository NuGet configuration and the released Identity 0.19.0 packages:

```powershell
dotnet restore Skopka.Hello.slnx --configfile NuGet.Config
dotnet build samples/Skopka.Hello.HostPolicies -c Release --no-restore
```

The host must provision staff through a separately authorized server operation,
install the [continuation gate](../../docs/registration-continuation.md), and own
its questionnaire and completion transaction. For example, the questionnaire
first asks “Кто будет учиться?”, asks an age band for “Я сам”, and keeps the pending
marker until the applicable parent-consent or child-creation step has succeeded.
Do not merely clear the marker after receiving a client checkbox. Preserve the
original purchase destination with the continuation completion helper.

The sample treats children with and without a phone as one kind; a production
host can split their sign-in policy further. It intentionally prevents public
registration/profile JSON from assigning staff status or clearing onboarding.
Protect custom parent/manager APIs separately; the Hello self-service policy
does not authorize those host operations.

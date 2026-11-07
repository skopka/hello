# Phone registration and binding

Enable phone OTP and configure an SMS sender, HMAC OTP and persistent rate limiter as described in [security.md](security.md). The account page includes a phone binding section at the configured account route followed by `#phone-binding`; hosts can use that local URL as their authenticated redirect. It shows **Add phone** when the account has no number and **Change phone** otherwise. Both actions send a code to the new number and leave the current number unchanged until verification succeeds.

`SkopkaHelloUiOptions.Account.Phone` controls number visibility. `ReadOnly` shows the current number while allowing verified binding through the independent `Account.PhoneBindingEnabled` option (default `true`). `Hidden` hides the current number; set `PhoneBindingEnabled = false` as well to hide and disable the binding forms. Binding also requires the phone OTP services. There is no unverified phone-edit form, including with `Phone = Editable`. This UI option does not disable the authenticated binding API.

On the code step, **Cancel** returns to an empty phone-input form without changing the account. A successful binding redirects to the account page's `#phone-binding` section and shows **Phone number linked.** once. The cancellation link and success redirect respect the configured UI prefix. These interaction improvements are available from 0.28.3; the binding API is unchanged.

For an IqZone-style account page, this configuration fragment enables the built-in replacement for a custom `/me/phone` page once phone OTP and SMS delivery are configured:

```csharp
services.AddSkopkaHelloUi<MyProfile, MyProfileUiFactory>(options =>
{
    options.Account.Phone = HelloUiAccountFieldMode.ReadOnly;
    options.Account.PhoneBindingEnabled = true;
});
```

Migration: `PUT /account/phone` is no longer mapped. The retained `IHelloIdentityApplication.ChangePhoneAsync` method returns `hello.phone_binding.required` without mutation, including requests to clear a number. Replace direct changes with the two binding calls below. Hosts without phone OTP cannot change account phone numbers through Hello. No database migration is required for this Hello change.

An authenticated bearer client can POST `{ "phone": "+79991234567" }` to `/account/phone/binding` and then POST `{ "ticket": "...", "code": "..." }` to `/account/phone/binding/complete`. Both calls validate the session online; the protected short-lived ticket binds the user, access token, phone, challenge and user version. A refreshed access token or account change requires starting again. The first call has the same behavior for occupied and unoccupied numbers. A conflict may be reported only after a valid code; a successful completion atomically confirms the phone and consumes its proof. Browser forms are protected by antiforgery, and the bearer endpoints do not return the OTP.

For phone-only Razor registration, a profile factory can also implement `IHelloUiRegistrationFields`. `GetFields()` supplies host labels, optional hints and bounds; `Validate()` checks submitted values and returns the validated dictionary. Hello rejects undeclared fields, missing required values and oversized values before invoking the host validator. `HelloUiRegistrationProfile.Values` passes validated values to the existing factory, defaulting to an empty dictionary for existing hosts. Field names are restricted to ASCII letters, digits and underscores. Hints and labels are host-owned and should match the host's language policy. The PhoneSqliteBlazor sample demonstrates an optional school-class field.

# Changelog

## 0.24.0

- Phone registration respects the existing Locale field mode, derives hidden locale from the request culture, and uses the shared legal-consent markup and field errors. The locale field now defaults to hidden; select Optional or Required to retain it.
- Opt-in `DefaultPhoneRegion = "RU"` normalizes Russian phone OTP input before policy and Identity checks; other phone operations and the default strict E.164 behavior are unchanged.
- Localized phone validation and client-side required messages. Hosts may remove custom locale-hiding, consent-layout and OTP phone-normalization CSS/JS workarounds.

## 0.23.0

- Added passwordless phone OTP sign-in and atomic verified-phone registration through Skopka.Identity 0.15.0.
- Added phone request, verification and registration APIs and a localized Razor login flow.
- Added host SMS templates, Development-only logging provider, SMS budget and pre-send policy extension.
- Added a SQLite static-rendered Blazor Web App sample and password-disabled host mode.

using Microsoft.AspNetCore.Http;
using Skopka.Identity.Verification;

namespace Skopka.Hello;

public sealed class HelloCrossDeviceSignInOptions
{
    public bool Enabled { get; set; }

    public TimeSpan RequestLifetime { get; set; } = TimeSpan.FromMinutes(2);

    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(2);

    public int UserCodeLength { get; set; } = 8;

    public int UserCodeGroupSize { get; set; } = 4;

    public string UserCodeAlphabet { get; set; } =
        "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public bool RequireStepUp { get; set; } = true;

    public string StepUpMethod { get; set; } =
        VerificationMethods.TimeBasedOneTimePassword;

    public TimeSpan StepUpMaximumAge { get; set; } =
        TimeSpan.FromMinutes(2);

    public int CreateClientPermitLimit { get; set; } = 5;

    public TimeSpan CreateClientWindow { get; set; } =
        TimeSpan.FromMinutes(5);

    public int StatusClientPermitLimit { get; set; } = 120;

    public TimeSpan StatusClientWindow { get; set; } =
        TimeSpan.FromMinutes(2);

    public TimeSpan RetentionAfterExpiration { get; set; } =
        TimeSpan.FromDays(1);

    public int CleanupBatchSize { get; set; } = 500;

    public string? SessionClientName { get; set; }

    public string VerifierCookieName { get; set; } =
        "__Host-Skopka.Hello.CrossDevice";

    public SameSiteMode CookieSameSite { get; set; } = SameSiteMode.Strict;

    internal void Validate(SkopkaHelloOptions helloOptions)
    {
        ArgumentNullException.ThrowIfNull(helloOptions);
        if (!Enabled)
        {
            return;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(VerifierCookieName);
        ArgumentException.ThrowIfNullOrWhiteSpace(UserCodeAlphabet);
        ArgumentException.ThrowIfNullOrWhiteSpace(StepUpMethod);

        if (!VerifierCookieName.StartsWith(
                "__Host-",
                StringComparison.Ordinal)
            || CookieSameSite != SameSiteMode.Strict)
        {
            throw new InvalidOperationException(
                "The cross-device verifier cookie must use a __Host- name and SameSite=Strict.");
        }

        if (RequestLifetime <= TimeSpan.Zero
            || RequestLifetime > TimeSpan.FromMinutes(15)
            || PollingInterval < TimeSpan.FromSeconds(1)
            || PollingInterval > TimeSpan.FromSeconds(30)
            || UserCodeLength < 4
            || UserCodeLength > 32
            || UserCodeGroupSize < 0
            || UserCodeGroupSize > UserCodeLength
            || UserCodeAlphabet.Length < 16
            || UserCodeAlphabet.Length > 64
            || UserCodeAlphabet.Distinct().Count()
                != UserCodeAlphabet.Length
            || UserCodeAlphabet.Any(character =>
                char.IsWhiteSpace(character)
                || char.IsControl(character)
                || character == '-')
            || !RequireStepUp
            || !string.Equals(
                StepUpMethod,
                VerificationMethods.TimeBasedOneTimePassword,
                StringComparison.Ordinal)
            || StepUpMaximumAge <= TimeSpan.Zero
            || StepUpMaximumAge > RequestLifetime
            || CreateClientPermitLimit <= 0
            || CreateClientWindow <= TimeSpan.Zero
            || StatusClientPermitLimit <= 0
            || StatusClientWindow <= TimeSpan.Zero
            || RetentionAfterExpiration < TimeSpan.Zero
            || CleanupBatchSize <= 0)
        {
            throw new InvalidOperationException(
                "Cross-device sign-in options are invalid. This version requires fresh TOTP step-up.");
        }

        if (SessionClientName is not null
            && (string.IsNullOrWhiteSpace(SessionClientName)
                || SessionClientName.Trim().Length > 128
                || SessionClientName.Any(char.IsControl)))
        {
            throw new InvalidOperationException(
                "SessionClientName must contain at most 128 non-control characters.");
        }

        SessionClientName = SessionClientName?.Trim();
        if (!helloOptions.SecureCookies)
        {
            throw new InvalidOperationException(
                "Cross-device sign-in requires Secure cookies and HTTPS.");
        }

        if (!helloOptions.Totp.Enabled)
        {
            throw new InvalidOperationException(
                "Cross-device sign-in requires TOTP to be enabled.");
        }

        if (helloOptions.PublicOrigin is not { Scheme: "https" })
        {
            throw new InvalidOperationException(
                "Cross-device sign-in requires an HTTPS PublicOrigin so approval QR links do not depend on the request Host header.");
        }

    }
}

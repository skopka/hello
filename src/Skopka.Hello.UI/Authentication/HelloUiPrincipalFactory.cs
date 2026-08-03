using System.Globalization;
using System.Security.Claims;

namespace Skopka.Hello.UI;

internal static class HelloUiPrincipalFactory
{
    public const string SessionIdClaim = "sid";
    public const string DisplayNameClaim =
        "skopka_hello_display_name";
    public const string EmailConfirmedClaim =
        "email_confirmed";
    public const string PhoneConfirmedClaim =
        "phone_number_verified";

    public static ClaimsPrincipal Create<TProfile>(
        HelloAccount<TProfile> account,
        Guid sessionId,
        IHelloUiProfileFactory<TProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(profiles);

        var displayName = profiles.GetDisplayName(account.Profile);
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = account.UserName
                ?? account.Email
                ?? account.Phone
                ?? "Account";
        }

        List<Claim> claims =
        [
            new("sub", account.Id.ToString("D")),
            new(
                ClaimTypes.NameIdentifier,
                account.Id.ToString("D")),
            new(
                SessionIdClaim,
                sessionId.ToString("D")),
            new(DisplayNameClaim, displayName),
            new(
                EmailConfirmedClaim,
                account.EmailConfirmed.ToString(
                    CultureInfo.InvariantCulture)),
            new(
                PhoneConfirmedClaim,
                account.PhoneConfirmed.ToString(
                    CultureInfo.InvariantCulture)),
        ];
        if (!string.IsNullOrWhiteSpace(account.UserName))
        {
            claims.Add(new Claim(
                ClaimTypes.Name,
                account.UserName));
        }

        if (!string.IsNullOrWhiteSpace(account.Email))
        {
            claims.Add(new Claim(
                ClaimTypes.Email,
                account.Email));
        }

        if (!string.IsNullOrWhiteSpace(account.Phone))
        {
            claims.Add(new Claim(
                ClaimTypes.MobilePhone,
                account.Phone));
        }

        return new ClaimsPrincipal(
            new ClaimsIdentity(
                claims,
                HelloUiDefaults.AuthenticationScheme,
                ClaimTypes.Name,
                ClaimTypes.Role));
    }

    public static bool TryGetUserId(
        ClaimsPrincipal principal,
        out Guid userId)
        => Guid.TryParse(
            principal.FindFirstValue("sub")
                ?? principal.FindFirstValue(
                    ClaimTypes.NameIdentifier),
            out userId);

    public static bool TryGetSessionId(
        ClaimsPrincipal principal,
        out Guid sessionId)
        => Guid.TryParse(
            principal.FindFirstValue(SessionIdClaim),
            out sessionId);
}

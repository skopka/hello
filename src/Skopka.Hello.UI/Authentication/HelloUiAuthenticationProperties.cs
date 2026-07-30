using Microsoft.AspNetCore.Authentication;

namespace Skopka.Hello.UI;

internal static class HelloUiAuthenticationProperties
{
    public static AuthenticationProperties Create(
        HelloSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var properties = new AuthenticationProperties
        {
            AllowRefresh = true,
            IsPersistent = true,
            IssuedUtc = DateTimeOffset.UtcNow,
            ExpiresUtc = session.RefreshTokenExpiresAt,
        };
        properties.StoreTokens(
        [
            new AuthenticationToken
            {
                Name = HelloUiDefaults.AccessTokenName,
                Value = session.AccessToken,
            },
        ]);
        return properties;
    }
}

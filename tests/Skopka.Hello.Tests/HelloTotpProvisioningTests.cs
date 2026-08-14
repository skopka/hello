namespace Skopka.Hello.Tests;

public sealed class HelloTotpProvisioningTests
{
    [Fact]
    public void ProvisioningUriUsesStandardTotpDefaultsAndEscaping()
    {
        const string secret =
            "JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP";

        var uri = HelloIdentityApplication<object>.CreateProvisioningUri(
            "IqZone XYZ",
            "ученик+1@example.test",
            secret);

        Assert.Equal(
            "otpauth://totp/IqZone%20XYZ:"
            + "%D1%83%D1%87%D0%B5%D0%BD%D0%B8%D0%BA%2B1%40example.test"
            + $"?secret={secret}&issuer=IqZone%20XYZ",
            uri);
        Assert.DoesNotContain("algorithm=", uri, StringComparison.Ordinal);
        Assert.DoesNotContain("digits=", uri, StringComparison.Ordinal);
        Assert.DoesNotContain("period=", uri, StringComparison.Ordinal);
    }
}

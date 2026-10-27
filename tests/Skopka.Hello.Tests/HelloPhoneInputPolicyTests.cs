using Skopka.Identity;
using Skopka.Identity.Errors;
using Skopka.Identity.PhoneOtp;

namespace Skopka.Hello.Tests;

public sealed class HelloPhoneInputPolicyTests
{
    [Theory]
    [InlineData("8 999 000-24-13")]
    [InlineData("79990002413")]
    [InlineData("9990002413")]
    [InlineData("+7 (999) 000-24-13")]
    public void RussianFormatsProduceSameHandle(string input)
    {
        var result = Create("RU", true).Normalize(input);
        Assert.True(result.IsSuccess);
        Assert.Equal("+79990002413", result.Value);
    }

    [Fact]
    public void ForeignCountryIsAllowedWhenNoAllowlistIsConfigured()
    {
        var result = Create("RU", true).Normalize("+1 (212) 555-1234");
        Assert.True(result.IsSuccess);
        Assert.Equal("+12125551234", result.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("RU")]
    public void AllowlistIsIndependentFromRussianParsing(string? region)
    {
        var result = Create(region, false, "+7").Normalize("+12125551234");
        Assert.Equal(IdentityErrorCodes.PhoneCountryNotAllowed, Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void LegacySaveKeepsInputUnchanged()
    {
        var result = Create(null, false).Normalize("+1 202 555 0123");
        Assert.True(result.IsSuccess);
        Assert.Equal("+1 202 555 0123", result.Value);
    }

    [Fact]
    public void OversizedOriginalIsRejectedBeforeCompaction()
    {
        var input = "+7" + new string(' ', 600) + "9990002413";
        var result = Create("RU", true).Normalize(input);
        Assert.Equal(IdentityErrorCodes.PhoneInvalidFormat, Assert.Single(result.Errors).Code);
    }

    private static HelloPhoneInputPolicy Create(string? region, bool enabled, params string[] codes)
    {
        var options = new SkopkaHelloOptions { PhoneOtpEnabled = enabled, DefaultPhoneRegion = region };
        var otp = new PhoneOtpOptions { AllowedCountryCodes = codes };
        return new HelloPhoneInputPolicy(options, otp, new IdentityPhoneNumberPolicy(new DefaultIdentityNormalizer(), otp));
    }
}

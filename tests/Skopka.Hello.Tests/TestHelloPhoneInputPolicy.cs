using Skopka.Identity;
using Skopka.Identity.PhoneOtp;

namespace Skopka.Hello.Tests;

internal static class TestHelloPhoneInputPolicy
{
    public static HelloPhoneInputPolicy Create()
    {
        var otp = new PhoneOtpOptions();
        return new HelloPhoneInputPolicy(new SkopkaHelloOptions(), otp,
            new IdentityPhoneNumberPolicy(new DefaultIdentityNormalizer(), otp));
    }
}

using Skopka.Abstraction.OperationResult;
using Skopka.Identity.PhoneOtp;

namespace Skopka.Hello;

internal sealed class HelloPhoneInputPolicy(
    SkopkaHelloOptions options, PhoneOtpOptions otpOptions,
    IIdentityPhoneNumberPolicy identityPolicy)
{
    public OperationResult<string> Normalize(string phone, bool otpRequest = false)
    {
        if (!otpRequest && !options.PhoneOtpEnabled && options.DefaultPhoneRegion is null
            && otpOptions.AllowedCountryCodes.Count == 0)
        {
            return OperationResultFactory.Success(phone);
        }

        return identityPolicy.Normalize(HelloPhoneNormalizer.Normalize(phone, options.DefaultPhoneRegion));
    }
}

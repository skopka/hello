using Skopka.Abstraction.OperationResult;
using Skopka.Identity.Errors;
using Skopka.Identity.PhoneOtp;
using Skopka.Identity.RateLimiting;

namespace Skopka.Hello;

/// <summary>Trusted server-side custom SMS delivery. Do not expose this service directly to untrusted clients.</summary>
public interface IHelloSmsSender
{
    /// <summary>Sends bounded host text through the configured SMS channel and shared limits.</summary>
    Task<OperationResult> SendAsync(string phone, string text, string? clientKey, CancellationToken cancellationToken);
}

internal sealed class HelloSmsSender<TProfile>(
    IHelloAccountMessageSender sender, HelloPhoneInputPolicy phonePolicy,
    PhoneOtpOptions phoneOptions, SkopkaHelloOptions options,
    IEnumerable<IIdentityRateLimiter<TProfile>> rateLimiters, TimeProvider clock) : IHelloSmsSender
{
    public async Task<OperationResult> SendAsync(string phone, string text, string? clientKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 480)
            return OperationResultFactory.Fail(new Error(HelloDeliveryErrorCodes.InvalidMessage, "The SMS text is invalid.", ErrorType.Validation));
        var normalized = phonePolicy.Normalize(phone, otpRequest: true);
        if (!normalized.IsSuccess) return OperationResultFactory.Fail(normalized.Errors);
        var available = sender.CheckAvailability(HelloDeliveryChannel.Sms);
        if (!available.IsSuccess) return available;
        var limiter = rateLimiters.FirstOrDefault() ?? throw new InvalidOperationException("SMS delivery requires an Identity rate limiter.");
        var requests = new List<RateLimitRequest>
        {
            new("hello.phone_otp.daily_sms", "host", options.DailySmsBudget, TimeSpan.FromDays(1)),
            new("phone_otp.daily_phone", normalized.Value, phoneOptions.DailyPhonePermitLimit, phoneOptions.DailyWindow),
        };
        if (!string.IsNullOrWhiteSpace(clientKey))
            requests.Add(new("phone_otp.daily_client", clientKey.Trim(), phoneOptions.DailyClientPermitLimit, phoneOptions.DailyWindow));
        foreach (var request in requests)
        {
            var decision = await limiter.HitAsync(request, cancellationToken);
            if (!decision.IsAllowed)
                return OperationResultFactory.Fail(new Error(IdentityErrorCodes.RateLimitExceeded, "Too many requests.", ErrorType.Forbidden, new RateLimitDetails(decision.RetryAfter)));
        }
        return await sender.SendAsync(new HelloAccountMessage(Guid.NewGuid(), HelloAccountMessageKind.HostSms,
            HelloDeliveryChannel.Sms, normalized.Value, null, clock.GetUtcNow().AddMinutes(10), Text: text), cancellationToken);
    }
}

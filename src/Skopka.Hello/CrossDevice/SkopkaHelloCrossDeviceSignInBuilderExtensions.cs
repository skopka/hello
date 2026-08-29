using Microsoft.Extensions.DependencyInjection.Extensions;
using Skopka.Identity;
using Skopka.Identity.DeviceAuthorization;

namespace Microsoft.Extensions.DependencyInjection;

public static class SkopkaHelloCrossDeviceSignInBuilderExtensions
{
    /// <summary>
    /// Enables short-lived cross-device sign-in with explicit approval and
    /// fresh TOTP step-up on an existing authenticated device.
    /// </summary>
    public static IdentityBuilder<TProfile> AddCrossDeviceSignIn<TProfile>(
        this IdentityBuilder<TProfile> builder,
        Action<Skopka.Hello.HelloCrossDeviceSignInOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var helloOptions = builder.Services
            .LastOrDefault(descriptor => descriptor.ServiceType
                == typeof(Skopka.Hello.SkopkaHelloOptions))
            ?.ImplementationInstance as Skopka.Hello.SkopkaHelloOptions
            ?? throw new InvalidOperationException(
                "AddSkopkaHello must be called before AddCrossDeviceSignIn.");
        var options = new Skopka.Hello.HelloCrossDeviceSignInOptions
        {
            Enabled = true,
        };
        configure?.Invoke(options);
        options.Validate(helloOptions);

        builder.Services.RemoveAll<
            Skopka.Hello.HelloCrossDeviceSignInOptions>();
        builder.Services.AddSingleton(options);
        if (!options.Enabled)
        {
            return builder;
        }

        builder.AddDeviceAuthorization(identity =>
        {
            identity.RequestLifetime = options.RequestLifetime;
            identity.UserCodeLength = options.UserCodeLength;
            identity.UserCodeGroupSize = options.UserCodeGroupSize;
            identity.UserCodeAlphabet = options.UserCodeAlphabet;
            identity.RequiredStepUpMethod = options.StepUpMethod;
            identity.StepUpMaximumAge = options.StepUpMaximumAge;
            identity.CreateClientPermitLimit =
                options.CreateClientPermitLimit;
            identity.CreateClientWindow = options.CreateClientWindow;
            identity.StatusClientPermitLimit =
                options.StatusClientPermitLimit;
            identity.StatusClientWindow = options.StatusClientWindow;
            identity.RetentionAfterExpiration =
                options.RetentionAfterExpiration;
            identity.CleanupBatchSize = options.CleanupBatchSize;
        });
        builder.Services.TryAddScoped<
            Skopka.Hello.IHelloCrossDeviceSignInApplication<TProfile>,
            Skopka.Hello.HelloCrossDeviceSignInApplication<TProfile>>();
        builder.Services.TryAddScoped<
            Skopka.Hello.IHelloCrossDeviceCookieManager,
            Skopka.Hello.HelloCrossDeviceCookieManager>();
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                Skopka.Hello.IHelloStepUpRequirementProvider<TProfile>,
                Skopka.Hello
                    .HelloCrossDeviceStepUpRequirementProvider<TProfile>>());
        return builder;
    }
}

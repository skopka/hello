using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Skopka.Hello;

namespace Skopka.Hello.AuthorizationServer;

public static class HelloNativeAppServiceCollectionExtensions
{
    /// <summary>Registers the optional native app flow and the host's shared EF store.</summary>
    public static IServiceCollection AddHelloNativeAppSignIn<TProfile, TContext>(
        this IServiceCollection services,
        Action<HelloNativeAppSignInOptions> configure) where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        var options = new HelloNativeAppSignInOptions();
        configure(options);
        if (options.Enabled && (options.Lifetime <= TimeSpan.Zero || options.Lifetime > TimeSpan.FromMinutes(10)
            || options.LaunchLifetime <= TimeSpan.Zero || options.LaunchLifetime > TimeSpan.FromMinutes(1)
            || options.ApprovalLifetime <= TimeSpan.Zero || options.ApprovalLifetime > TimeSpan.FromMinutes(1)
            || options.RetentionAfterExpiration < TimeSpan.Zero || options.CleanupBatchSize is < 1 or > 10000
            || options.BeginClientPermitLimit < 1 || options.BeginClientWindow <= TimeSpan.Zero
            || options.BeginApplicationPermitLimit < 1 || options.BeginApplicationWindow <= TimeSpan.Zero
            || options.StatusMinimumInterval < TimeSpan.FromSeconds(2)
            || options.AllowedCallbacks is null || options.AllowedCallbacks.Count == 0
            || options.AllowedCallbacks.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Length > 200
                || pair.Value is null || pair.Value.Length == 0 || pair.Value.Distinct(StringComparer.Ordinal).Count() != pair.Value.Length
                || pair.Value.Any(uri => !IsValidCallback(uri)))))
        {
            throw new InvalidOperationException("Invalid native app sign-in configuration.");
        }
        services.AddSingleton(options);
        if (options.Enabled)
        {
            services.TryAddScoped<IHelloNativeAppFlowStore, EfHelloNativeAppFlowStore<TContext>>();
            services.TryAddScoped<IHelloNativeAppSignInApplication<TProfile>, HelloNativeAppSignInApplication<TProfile>>();
        }
        return services;
    }
    private static bool IsValidCallback(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 2048
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment)
            || Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query)
                .Keys.Any(key => string.Equals(key, "state", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (uri.Scheme == Uri.UriSchemeHttps)
        {
            return !string.IsNullOrEmpty(uri.Host);
        }

        return uri.Scheme.Contains('.')
            && uri.Scheme.Split('.').All(segment => segment.Length > 0
                && char.IsAsciiLetter(segment[0])
                && segment.All(character => char.IsAsciiLetterOrDigit(character) || character == '-'));
    }
}

using Microsoft.AspNetCore.Http;

namespace Skopka.Hello.UI;

public static class HelloUiCrossDeviceApprovalAvailability
{
    private static readonly object CacheKey = new();

    public static Task<bool> IsAvailableAsync(
        HttpContext httpContext,
        IHelloUiApplication application,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(application);

        if (httpContext.RequestServices.GetService(
                typeof(IHelloUiCrossDeviceApplication)) is null)
        {
            return Task.FromResult(false);
        }

        if (httpContext.Items.TryGetValue(CacheKey, out var cached)
            && cached is Task<bool> result)
        {
            return result;
        }

        var availability = CheckAsync(
            httpContext,
            application,
            cancellationToken);
        httpContext.Items[CacheKey] = availability;
        return availability;
    }

    private static async Task<bool> CheckAsync(
        HttpContext httpContext,
        IHelloUiApplication application,
        CancellationToken cancellationToken)
    {
        if (httpContext.User.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var state = await application.GetTotpStateAsync(
            httpContext,
            cancellationToken);
        return state.IsSuccess && state.Value.IsEnabled;
    }
}

using System.Globalization;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;

namespace Skopka.Hello.UI;

/// <summary>
/// Ends every Hello form submission with a redirect to a GET.
/// </summary>
/// <remarks>
/// A POST handler that renders its page (a validation error, a domain error or
/// an intermediate step such as "enter the code") is rendered once, with typed
/// secrets removed, and kept server-side for its browser. The response becomes
/// a <c>303 See Other</c> to the same page, and that GET returns the kept
/// outcome. Refresh and Back therefore never repeat the submission, and the
/// step the person was on stays on screen until the next submission to that
/// page or until the outcome expires; then the page renders afresh.
/// </remarks>
internal sealed class HelloUiPostRedirectFilter(
    HelloUiPostRedirectStore store,
    SkopkaHelloUiOptions uiOptions,
    ITempDataDictionaryFactory tempDataFactory)
    : IAsyncPageFilter, IAsyncResultFilter
{
    /// <summary>The query parameter that references a kept submission outcome.</summary>
    public const string QueryParameter = "hello-prg";

    private const string BindingCookieName = "Skopka.Hello.UI.Prg";
    private const string BindingItemKey = "Skopka.Hello.UI.Prg.Binding";
    private const string GetStateItemKey = "Skopka.Hello.UI.Prg.GetState";

    // Names of values a person types to prove something. They are never kept
    // in a retained outcome; after an error the person types them again.
    private static readonly HashSet<string> SecretNames = new(
        [
            "Password",
            "ConfirmPassword",
            "CurrentPassword",
            "NewPassword",
            "ConfirmNewPassword",
            "Code",
            "VerificationCode",
            "SecondFactorCode",
            "BindingCode",
            "TotpCode",
            "RecoveryCode",
            "UserCode",
        ],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> SecretPrefixes = new(
        ["Passkey"],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> ExcludedHeaders = new(
        [
            "Set-Cookie",
            "Content-Length",
            "Transfer-Encoding",
            "Location",
            "Date",
            "Server",
        ],
        StringComparer.OrdinalIgnoreCase);

    public Task OnPageHandlerSelectionAsync(
        PageHandlerSelectedContext context)
        => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(
        PageHandlerExecutingContext context,
        PageHandlerExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        var http = context.HttpContext;
        var request = http.Request;
        if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
        {
            var token = request.Query[QueryParameter].ToString();
            if (token.Length > 0
                && ReadBinding(http) is { } binding
                && store.TryGet(token, binding, PageKey(request), out var snapshot)
                && snapshot is not null)
            {
                // Replaying an outcome must not spend state that the page keeps
                // in TempData for its next real render (pending steps, tokens).
                tempDataFactory.GetTempData(http).Keep();
                context.Result = new ReplayResult(snapshot);
                return;
            }

            await next();
            return;
        }

        if (HttpMethods.IsPost(request.Method)
            && ReadBinding(http) is { } current)
        {
            store.Invalidate(current, PageKey(request));
        }

        var executed = await next();
        if (executed.Exception is null
            && executed.Result is PageResult
            && HttpMethods.IsPost(request.Method))
        {
            RemoveSecrets(context.ModelState, executed.HandlerInstance);
            http.Items[GetStateItemKey] = GetState(executed.HandlerInstance);
        }
    }

    public async Task OnResultExecutionAsync(
        ResultExecutingContext context,
        ResultExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        var http = context.HttpContext;
        if (!HttpMethods.IsPost(http.Request.Method)
            || context.Result is not PageResult)
        {
            await next();
            return;
        }

        var response = http.Response;
        var original = response.Body;
        using var buffer = new MemoryStream();
        response.Body = buffer;
        try
        {
            await next();
        }
        finally
        {
            response.Body = original;
        }

        if (response.HasStarted)
        {
            buffer.Position = 0;
            await buffer.CopyToAsync(original, http.RequestAborted);
            return;
        }

        var headers = response.Headers
            .Where(header => !ExcludedHeaders.Contains(header.Key))
            .Select(header => new KeyValuePair<string, string[]>(
                header.Key,
                header.Value.Where(value => value is not null).Select(value => value!).ToArray()))
            .ToArray();
        var binding = EnsureBinding(http);
        var token = store.Store(new HelloUiPostRedirectSnapshot(
            binding,
            PageKey(http.Request),
            response.StatusCode,
            headers,
            buffer.ToArray()));

        var cookies = response.Headers.SetCookie;
        response.Clear();
        if (cookies.Count > 0)
        {
            response.Headers.SetCookie = cookies;
        }

        response.StatusCode = StatusCodes.Status303SeeOther;
        response.Headers.CacheControl = "no-store, max-age=0";
        response.Headers.Pragma = "no-cache";
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers.Location = RedirectTarget(
            http.Request,
            token,
            http.Items.TryGetValue(GetStateItemKey, out var state)
                ? state as IReadOnlyList<KeyValuePair<string, string?>>
                : null);
    }

    internal static string PageKey(HttpRequest request)
        => (request.PathBase + request.Path).Value ?? "/";

    internal static string RedirectTarget(
        HttpRequest request,
        string? token,
        IReadOnlyList<KeyValuePair<string, string?>>? getState = null)
    {
        var query = QueryHelpers.ParseQuery(request.QueryString.Value)
            .Where(pair => !string.Equals(pair.Key, "handler", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(pair.Key, QueryParameter, StringComparison.OrdinalIgnoreCase))
            .SelectMany(pair => pair.Value.Select(value =>
                new KeyValuePair<string, string?>(pair.Key, value)))
            .ToList();
        foreach (var pair in getState ?? [])
        {
            // A value the page also accepts on GET (a return URL, a request
            // being waited for) keeps the page meaningful once the retained
            // outcome expires. The query string of the POST wins.
            if (!query.Any(existing => string.Equals(existing.Key, pair.Key, StringComparison.OrdinalIgnoreCase)))
            {
                query.Add(pair);
            }
        }

        if (token is not null)
        {
            query.Add(new(QueryParameter, token));
        }

        return request.PathBase + request.Path + QueryString.Create(query);
    }

    /// <summary>The page URL without a retained-outcome reference.</summary>
    public static string CleanUrl(HttpRequest request)
        => RedirectTarget(request, token: null);

    private static List<KeyValuePair<string, string?>> GetState(object? handler)
    {
        if (handler is null)
        {
            return [];
        }

        List<KeyValuePair<string, string?>> state = [];
        foreach (var property in handler.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.GetCustomAttribute<BindPropertyAttribute>() is not { SupportsGet: true } bind
                || property.GetIndexParameters().Length != 0
                || IsSecret(property.Name)
                || property.Name.EndsWith("Token", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = property.GetValue(handler);
            var text = value switch
            {
                null => null,
                string s => s,
                bool b => b ? "true" : null,
                DateTimeOffset d => d.ToString("O", CultureInfo.InvariantCulture),
                DateTime d => d.ToString("O", CultureInfo.InvariantCulture),
                Enum e => Convert.ToInt64(e, CultureInfo.InvariantCulture) == 0 ? null : e.ToString(),
                Guid g => g == Guid.Empty ? null : g.ToString("D"),
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                _ => null,
            };
            if (!string.IsNullOrEmpty(text) && text.Length <= 2048)
            {
                state.Add(new(bind.Name ?? property.Name, text));
            }
        }

        return state;
    }

    private static void RemoveSecrets(
        ModelStateDictionary modelState,
        object? handler)
    {
        foreach (var key in modelState.Keys.ToArray())
        {
            if (!IsSecret(key))
            {
                continue;
            }

            modelState.SetModelValue(key, rawValue: null, attemptedValue: null);
            if (handler is not null)
            {
                ClearProperty(handler, key);
            }
        }

        if (handler is null)
        {
            return;
        }

        // Bound values that failed no validation have no ModelState entry
        // with errors but still render through asp-for; clear them as well.
        foreach (var property in handler.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.GetCustomAttribute<BindPropertyAttribute>() is null
                || property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            ClearSecrets(property.GetValue(handler), property.Name, handler, property, depth: 0);
        }
    }

    private static void ClearSecrets(
        object? value,
        string path,
        object owner,
        PropertyInfo property,
        int depth)
    {
        if (IsSecret(path))
        {
            Clear(owner, property);
            return;
        }

        if (value is null || depth > 2 || value is string || property.PropertyType.IsValueType
            || value is System.Collections.IEnumerable)
        {
            return;
        }

        foreach (var nested in value.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (nested.GetIndexParameters().Length != 0)
            {
                continue;
            }

            ClearSecrets(nested.GetValue(value), path + "." + nested.Name, value, nested, depth + 1);
        }
    }

    private static void ClearProperty(object handler, string key)
    {
        object? owner = handler;
        var segments = key.Split('.');
        for (var i = 0; i < segments.Length && owner is not null; i++)
        {
            var property = owner.GetType().GetProperty(
                segments[i],
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            if (property is null || property.GetIndexParameters().Length != 0)
            {
                return;
            }

            if (i == segments.Length - 1)
            {
                Clear(owner, property);
                return;
            }

            owner = property.GetValue(owner);
        }
    }

    private static void Clear(object owner, PropertyInfo property)
    {
        if (!property.CanWrite || property.SetMethod?.IsPublic != true)
        {
            return;
        }

        if (property.PropertyType == typeof(string))
        {
            var nullable = new NullabilityInfoContext().Create(property).WriteState
                == NullabilityState.Nullable;
            property.SetValue(owner, nullable ? null : string.Empty);
        }
    }

    internal static bool IsSecret(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        var segments = key.Split('.');
        return SecretNames.Contains(segments[^1])
            || segments.Any(SecretPrefixes.Contains);
    }

    private string? ReadBinding(HttpContext http)
    {
        if (http.Items.TryGetValue(BindingItemKey, out var cached) && cached is string value)
        {
            return value;
        }

        return http.Request.Cookies.TryGetValue(CookieName, out var cookie)
            && cookie.Length is > 20 and < 100
                ? Bind(http, cookie)
                : null;
    }

    private string EnsureBinding(HttpContext http)
    {
        if (ReadBinding(http) is { } existing)
        {
            return existing;
        }

        var cookie = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        http.Response.Cookies.Append(CookieName, cookie, new CookieOptions
        {
            HttpOnly = true,
            Secure = uiOptions.SecureCookies,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            IsEssential = true,
        });
        return Bind(http, cookie);
    }

    private static string Bind(HttpContext http, string cookie)
    {
        // The outcome belongs to this browser and to the account that saw it.
        // Another account signed in on the same browser gets a fresh page.
        var subject = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? http.User.FindFirst("sub")?.Value
            ?? string.Empty;
        var binding = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(cookie + "\n" + subject)));
        http.Items[BindingItemKey] = binding;
        return binding;
    }

    private string CookieName => uiOptions.SecureCookies
        ? "__Host-" + BindingCookieName
        : BindingCookieName;

    private sealed class ReplayResult(HelloUiPostRedirectSnapshot snapshot)
        : IActionResult
    {
        public async Task ExecuteResultAsync(ActionContext context)
        {
            var response = context.HttpContext.Response;
            response.StatusCode = snapshot.StatusCode;
            foreach (var (name, values) in snapshot.Headers)
            {
                response.Headers[name] = new StringValues(values);
            }

            response.Headers.CacheControl = "no-store, max-age=0";
            response.Headers.Pragma = "no-cache";
            response.ContentLength = snapshot.Body.Length;
            if (HttpMethods.IsHead(context.HttpContext.Request.Method))
            {
                return;
            }

            await response.Body.WriteAsync(snapshot.Body, context.HttpContext.RequestAborted);
        }
    }
}

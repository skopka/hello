using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Skopka.Hello.Admin;
using Skopka.Hello.UI;

namespace Skopka.Hello.Tests;

public sealed class HelloUiLocalizationTests
{
    [Fact]
    public void DefaultsPreserveEnglishSingleLanguageBehavior()
    {
        var options = new SkopkaHelloUiOptions();

        options.Validate();

        Assert.False(options.Localization.Enabled);
        Assert.Equal("en", options.Localization.DefaultCulture);
        Assert.Equal(
            ["en", "ru"],
            options.Localization.SupportedCultures
                .Select(culture => culture.Name));
    }

    [Fact]
    public void DefaultCultureMustBeSupported()
    {
        var options = new SkopkaHelloUiOptions();
        options.Localization.DefaultCulture = "de";

        var exception = Assert.Throws<InvalidOperationException>(
            options.Validate);

        Assert.Contains(
            "supported",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddCultureRejectsUnknownCulture()
    {
        var options = new SkopkaHelloUiOptions();

        Assert.Throws<InvalidOperationException>(
            () => options.Localization.AddCulture(
                "not_a_real_culture",
                "Unknown"));
    }

    [Fact]
    public async Task CustomDictionaryOverridesBuiltInText()
    {
        var temporaryFile = Path.Combine(
            Path.GetTempPath(),
            $"skopka-hello-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(
            temporaryFile,
            """
            {
              "culture": "ru",
              "texts": {
                "Layout.SignIn": "Войти в тестовый стенд"
              }
            }
            """,
            Encoding.UTF8);

        try
        {
            var services = new ServiceCollection();
            services.AddSkopkaHelloUi(options =>
            {
                options.Localization.Enabled = true;
                options.Localization.DefaultCulture = "ru";
                options.Localization.AddDictionaryFile(
                    "ru",
                    temporaryFile);
            });
            await using var provider = services.BuildServiceProvider();
            var localizer = provider.GetRequiredService<
                IHelloUiLocalizer>();

            using var culture = new CultureScope("ru");

            Assert.Equal(
                "Войти в тестовый стенд",
                localizer["Layout.SignIn"].Value);
            Assert.Equal(
                "Аккаунт",
                localizer["Layout.Account"].Value);
        }
        finally
        {
            File.Delete(temporaryFile);
        }
    }

    [Fact]
    public void BuiltInDictionariesHaveMatchingKeySets()
    {
        AssertMatchingKeySets(
            typeof(HelloUiModule).Assembly,
            "Skopka.Hello.UI.Localization.en.json",
            "Skopka.Hello.UI.Localization.ru.json");
        AssertMatchingKeySets(
            typeof(HelloAdminDefaults).Assembly,
            "Skopka.Hello.Admin.Localization.en.json",
            "Skopka.Hello.Admin.Localization.ru.json");
    }

    [Fact]
    public async Task CultureResolutionPrefersCookieThenHeaderThenDefault()
    {
        var options = new SkopkaHelloUiOptions();
        options.Localization.Enabled = true;
        options.Localization.DefaultCulture = "en";
        options.Validate();
        var filter = new HelloUiRequestCultureFilter(options);
        var context = new DefaultHttpContext();
        context.Request.Headers.AcceptLanguage = "ru-RU, en;q=0.8";

        var fromHeader = await filter.ResolveCultureAsync(context);

        Assert.Equal("ru", fromHeader.Name);

        context.Request.Headers.Cookie =
            $"{options.Localization.CultureCookieName}=en";
        var fromCookie = await filter.ResolveCultureAsync(context);

        Assert.Equal("en", fromCookie.Name);

        context.Request.Headers.Cookie =
            $"{options.Localization.CultureCookieName}=unsupported";
        context.Request.Headers.Remove("Accept-Language");
        var fallback = await filter.ResolveCultureAsync(context);

        Assert.Equal("en", fallback.Name);
    }

    [Fact]
    public async Task CultureEndpointRequiresAntiforgeryAndUsesLocalReturnUrl()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services
            .AddDataProtection()
            .UseEphemeralDataProtectionProvider();
        builder.Services.AddSingleton(new HelloUiRoutePaths("/portal"));
        builder.Services.AddRazorPages();
        builder.Services.AddSkopkaHelloUi(options =>
            options.Localization.Enabled = true);
        await using var application = builder.Build();
        application.MapSkopkaHelloUi();

        var endpoint = ((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate => String.Equals(
                candidate.RoutePattern.RawText,
                "/portal/culture",
                StringComparison.Ordinal));

        var rejected = CreatePostContext(application.Services, "culture=ru");
        await endpoint.RequestDelegate!(rejected);
        Assert.Equal(
            StatusCodes.Status400BadRequest,
            rejected.Response.StatusCode);

        var antiforgery = application.Services.GetRequiredService<
            IAntiforgery>();
        var tokenContext = new DefaultHttpContext
        {
            RequestServices = application.Services,
        };
        tokenContext.Request.Scheme = "https";
        var tokens = antiforgery.GetAndStoreTokens(tokenContext);
        var antiforgeryCookie = tokenContext.Response.Headers.SetCookie
            .Select(value => value!.Split(';', 2)[0])
            .Single();
        var form = String.Join(
            "&",
            $"culture={Uri.EscapeDataString("ru")}",
            $"returnUrl={Uri.EscapeDataString("/portal/login?source=language")}",
            $"{Uri.EscapeDataString(tokens.FormFieldName)}={Uri.EscapeDataString(tokens.RequestToken!)}");
        var accepted = CreatePostContext(application.Services, form);
        accepted.Request.Headers.Cookie = antiforgeryCookie;

        await endpoint.RequestDelegate!(accepted);

        Assert.Equal(
            StatusCodes.Status302Found,
            accepted.Response.StatusCode);
        Assert.Equal(
            "/portal/login?source=language",
            accepted.Response.Headers.Location);
        Assert.Contains(
            accepted.Response.Headers.SetCookie,
            value => value!.StartsWith(
                "Skopka.Hello.Culture=ru",
                StringComparison.Ordinal));
    }

    private static DefaultHttpContext CreatePostContext(
        IServiceProvider services,
        string form)
    {
        var content = Encoding.UTF8.GetBytes(form);
        var context = new DefaultHttpContext
        {
            RequestServices = services,
        };
        context.Request.Method = HttpMethods.Post;
        context.Request.Scheme = "https";
        context.Request.ContentType =
            "application/x-www-form-urlencoded";
        context.Request.ContentLength = content.Length;
        context.Request.Body = new MemoryStream(content);
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static void AssertMatchingKeySets(
        Assembly assembly,
        string englishResource,
        string russianResource)
    {
        var english = ReadKeys(assembly, englishResource);
        var russian = ReadKeys(assembly, russianResource);

        Assert.Equal(english, russian);
    }

    private static string[] ReadKeys(
        Assembly assembly,
        string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Resource '{resourceName}' was not found.");
        using var document = JsonDocument.Parse(stream);
        return document.RootElement
            .GetProperty("texts")
            .EnumerateObject()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo previousCulture =
            CultureInfo.CurrentCulture;
        private readonly CultureInfo previousUiCulture =
            CultureInfo.CurrentUICulture;

        public CultureScope(string culture)
        {
            var selected = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentCulture = selected;
            CultureInfo.CurrentUICulture = selected;
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }
}

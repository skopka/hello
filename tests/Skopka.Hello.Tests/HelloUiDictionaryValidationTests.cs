using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Skopka.Hello.UI;

namespace Skopka.Hello.Tests;

public sealed class HelloUiDictionaryValidationTests
{
    [Theory]
    [InlineData("\"Admin.Clients.Title\": \"secret-value\",", "Admin.Clients.Title")]
    [InlineData("\"text\": {},", "text")]
    public void UnknownRootPropertyIsRejected(string property, string name)
    {
        WithDictionary($$"""{ "culture": "en", {{property}} "texts": {} }""", path =>
        {
            using var provider = CreateProvider(path);
            var exception = Assert.Throws<InvalidOperationException>(
                () => provider.GetRequiredService<IHelloUiLocalizer>());
            Assert.Contains(path, exception.Message, StringComparison.Ordinal);
            Assert.Contains(name, exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("secret-value", exception.Message, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData("\"culture\": \"en\", \"culture\": \"en\", \"texts\": {}", "culture")]
    [InlineData("\"culture\": \"en\", \"texts\": {}, \"texts\": {}", "texts")]
    public void DuplicateRootPropertyIsRejected(string properties, string name)
    {
        WithDictionary($"{{ {properties} }}", path =>
        {
            using var provider = CreateProvider(path);
            var exception = Assert.Throws<InvalidOperationException>(
                () => provider.GetRequiredService<IHelloUiLocalizer>());
            Assert.Contains(path, exception.Message, StringComparison.Ordinal);
            Assert.Contains($"duplicate root property '{name}'", exception.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ValidDictionaryOverridesAndFallsBackToPackagedText()
    {
        WithDictionary("""{ "texts": { "Layout.SignIn": "Custom sign in" }, "culture": "en" }""", path =>
        {
            var previous = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
                using var provider = CreateProvider(path);
                var localizer = provider.GetRequiredService<IHelloUiLocalizer>();
                Assert.Equal("Custom sign in", localizer["Layout.SignIn"].Value);
                Assert.NotEqual("Layout.Account", localizer["Layout.Account"].Value);
            }
            finally
            {
                CultureInfo.CurrentUICulture = previous;
            }
        });
    }

    [Fact]
    public async Task UnknownRootPropertyFailsHostStartup()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hello-dictionary-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, """{ "culture": "en", "texts": {}, "Admin.Clients.Title": "ignored" }""");
        try
        {
            using var host = Host.CreateDefaultBuilder()
                .ConfigureServices(services => services.AddSkopkaHelloUi(
                    options => options.Localization.AddDictionaryFile("en", path)))
                .Build();

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => host.StartAsync());
            Assert.Contains(path, exception.Message, StringComparison.Ordinal);
            Assert.Contains("Admin.Clients.Title", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static ServiceProvider CreateProvider(string path)
    {
        var services = new ServiceCollection();
        services.AddSkopkaHelloUi(options => options.Localization.AddDictionaryFile("en", path));
        return services.BuildServiceProvider();
    }

    private static void WithDictionary(string json, Action<string> action)
    {
        var path = Path.Combine(Path.GetTempPath(), $"hello-dictionary-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        try
        {
            action(path);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

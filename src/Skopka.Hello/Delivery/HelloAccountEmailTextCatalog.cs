using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Hosting;

namespace Skopka.Hello;

internal sealed class HelloAccountEmailTextCatalog
{
    private static readonly string[] EmbeddedResources =
    [
        "Skopka.Hello.Delivery.Localization.en.json",
        "Skopka.Hello.Delivery.Localization.ru.json",
    ];

    private readonly Dictionary<string, Dictionary<string, string>>
        dictionaries = new(StringComparer.OrdinalIgnoreCase);

    private readonly HelloAccountMessageLocalizationOptions options;

    public HelloAccountEmailTextCatalog(
        HelloSmtpOptions smtpOptions,
        IHostEnvironment? environment = null)
    {
        ArgumentNullException.ThrowIfNull(smtpOptions);
        options = smtpOptions.Localization;

        var assembly = typeof(HelloAccountEmailTextCatalog).Assembly;
        foreach (var resourceName in EmbeddedResources)
        {
            using var stream = assembly.GetManifestResourceStream(
                    resourceName)
                ?? throw new InvalidOperationException(
                    $"The embedded account-message dictionary '{resourceName}' was not found.");
            LoadDictionary(
                stream,
                resourceName,
                expectedCulture: null,
                overwrite: false);
        }

        var contentRoot = environment?.ContentRootPath
            ?? Directory.GetCurrentDirectory();
        foreach (var registration in options.DictionaryFiles)
        {
            foreach (var configuredPath in registration.Value)
            {
                var fullPath = Path.IsPathRooted(configuredPath)
                    ? Path.GetFullPath(configuredPath)
                    : Path.GetFullPath(configuredPath, contentRoot);
                using var stream = new FileStream(
                    fullPath,
                    new FileStreamOptions
                    {
                        Mode = FileMode.Open,
                        Access = FileAccess.Read,
                        Share = FileShare.Read,
                        Options = FileOptions.SequentialScan,
                    });
                LoadDictionary(
                    stream,
                    fullPath,
                    registration.Key,
                    overwrite: true);
            }
        }

        ValidateRequiredKeys();
    }

    public CultureInfo Culture =>
        CultureInfo.GetCultureInfo(options.DefaultCulture);

    public string GetRequiredString(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        foreach (var culture in GetFallbackCultures())
        {
            if (dictionaries.TryGetValue(culture, out var dictionary)
                && dictionary.TryGetValue(name, out var value))
            {
                return value;
            }
        }

        throw new InvalidOperationException(
            $"The account-message text '{name}' is not configured.");
    }

    private IEnumerable<string> GetFallbackCultures()
    {
        var yielded = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var culture in GetCultureAndParents(
                     options.DefaultCulture).Concat(["en"]))
        {
            if (yielded.Add(culture))
            {
                yield return culture;
            }
        }
    }

    private static IEnumerable<string> GetCultureAndParents(
        string culture)
    {
        var current = CultureInfo.GetCultureInfo(culture);
        while (!String.IsNullOrEmpty(current.Name))
        {
            yield return current.Name;
            current = current.Parent;
        }
    }

    private void LoadDictionary(
        Stream stream,
        string sourceName,
        string? expectedCulture,
        bool overwrite)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(stream);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"The account-message dictionary '{sourceName}' is not valid JSON.",
                exception);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty(
                    "culture",
                    out var cultureElement)
                || cultureElement.ValueKind != JsonValueKind.String
                || !document.RootElement.TryGetProperty(
                    "texts",
                    out var textsElement)
                || textsElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException(
                    $"The account-message dictionary '{sourceName}' must contain a culture string and a texts object.");
            }

            var culture = NormalizeCulture(
                cultureElement.GetString()!,
                sourceName);
            if (expectedCulture is not null
                && !String.Equals(
                    culture,
                    expectedCulture,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"The account-message dictionary '{sourceName}' declares culture '{culture}', but it was registered for '{expectedCulture}'.");
            }

            if (!dictionaries.TryGetValue(culture, out var dictionary))
            {
                dictionary = new Dictionary<string, string>(
                    StringComparer.Ordinal);
                dictionaries.Add(culture, dictionary);
            }

            var fileKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in textsElement.EnumerateObject())
            {
                if (!fileKeys.Add(property.Name))
                {
                    throw new InvalidOperationException(
                        $"The account-message dictionary '{sourceName}' contains duplicate key '{property.Name}'.");
                }

                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    throw new InvalidOperationException(
                        $"The account-message dictionary value '{property.Name}' in '{sourceName}' must be a string.");
                }

                if (!overwrite && dictionary.ContainsKey(property.Name))
                {
                    throw new InvalidOperationException(
                        $"The embedded account-message dictionaries contain duplicate key '{property.Name}' for culture '{culture}'.");
                }

                dictionary[property.Name] = property.Value.GetString()!;
            }
        }
    }

    private void ValidateRequiredKeys()
    {
        foreach (var kind in Enum.GetValues<HelloAccountMessageKind>())
        {
            var keys = HelloAccountEmailTemplateRenderer.GetKeys(kind);
            _ = GetRequiredString(keys.Subject);
            _ = GetRequiredString(keys.Introduction);
            if (keys.Action is not null)
            {
                _ = GetRequiredString(keys.Action);
            }
        }

        foreach (var variant in HelloAccountEmailTemplateVariants
                     .AccountSecurityVariants)
        {
            var keys = HelloAccountEmailTemplateRenderer.GetKeys(
                HelloAccountMessageKind.AccountSecurityVerification,
                variant);
            _ = GetRequiredString(keys.Subject);
            _ = GetRequiredString(keys.Introduction);
        }

        _ = GetRequiredString(HelloAccountEmailTextKeys.LinkExpires);
        _ = GetRequiredString(HelloAccountEmailTextKeys.CodeExpires);
        _ = GetRequiredString(
            HelloAccountEmailTextKeys.IgnoreUnrequested);
    }

    private static string NormalizeCulture(
        string culture,
        string sourceName)
    {
        try
        {
            var normalized = CultureInfo.GetCultureInfo(culture).Name;
            if (String.IsNullOrEmpty(normalized))
            {
                throw new CultureNotFoundException(
                    nameof(culture),
                    culture,
                    "The invariant culture is not supported.");
            }

            return normalized;
        }
        catch (CultureNotFoundException exception)
        {
            throw new InvalidOperationException(
                $"The account-message dictionary '{sourceName}' declares an invalid culture.",
                exception);
        }
    }
}

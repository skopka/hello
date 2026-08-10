using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Hosting;

namespace Skopka.Hello.UI;

internal sealed class HelloUiTextCatalog
{
    private readonly Dictionary<
        string,
        Dictionary<string, string>> dictionaries =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly SkopkaHelloUiLocalizationOptions options;

    public HelloUiTextCatalog(
        SkopkaHelloUiOptions uiOptions,
        IEnumerable<HelloUiDictionarySource> sources,
        IHostEnvironment? environment = null)
    {
        ArgumentNullException.ThrowIfNull(uiOptions);
        ArgumentNullException.ThrowIfNull(sources);

        options = uiOptions.Localization;
        foreach (var source in sources)
        {
            foreach (var resourceName in source.ResourceNames)
            {
                using var stream = source.Assembly
                        .GetManifestResourceStream(resourceName)
                    ?? throw new InvalidOperationException(
                        $"The embedded UI dictionary '{resourceName}' was not found.");
                LoadDictionary(
                    stream,
                    resourceName,
                    expectedCulture: null,
                    overwrite: false);
            }
        }

        var contentRoot = environment?.ContentRootPath
            ?? Directory.GetCurrentDirectory();
        foreach (var culture in options.SupportedCultures)
        {
            foreach (var configuredPath in
                     options.GetDictionaryFiles(culture.Name))
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
                    culture.Name,
                    overwrite: true);
            }
        }
    }

    public bool TryGetString(
        string culture,
        string name,
        out string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        foreach (var candidate in GetFallbackCultures(culture))
        {
            if (dictionaries.TryGetValue(
                    candidate,
                    out var dictionary)
                && dictionary.TryGetValue(name, out value!))
            {
                return true;
            }
        }

        value = name;
        return false;
    }

    public IReadOnlyDictionary<string, string> GetAllStrings(
        string culture,
        bool includeParentCultures)
    {
        var result = new Dictionary<string, string>(
            StringComparer.Ordinal);
        var cultures = GetFallbackCultures(culture).ToArray();
        if (!includeParentCultures && cultures.Length > 0)
        {
            cultures = [cultures[0]];
        }

        for (var index = cultures.Length - 1; index >= 0; index--)
        {
            if (!dictionaries.TryGetValue(
                    cultures[index],
                    out var dictionary))
            {
                continue;
            }

            foreach (var pair in dictionary)
            {
                result[pair.Key] = pair.Value;
            }
        }

        return result;
    }

    private IEnumerable<string> GetFallbackCultures(string culture)
    {
        var yielded = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in GetCultureAndParents(culture)
                     .Concat(GetCultureAndParents(
                         options.DefaultCulture))
                     .Concat(["en"]))
        {
            if (yielded.Add(candidate))
            {
                yield return candidate;
            }
        }
    }

    private static IEnumerable<string> GetCultureAndParents(
        string culture)
    {
        CultureInfo current;
        try
        {
            current = CultureInfo.GetCultureInfo(culture);
        }
        catch (CultureNotFoundException)
        {
            yield break;
        }

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
            document = JsonDocument.Parse(
                stream,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                });
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"The UI dictionary '{sourceName}' is not valid JSON.",
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
                    $"The UI dictionary '{sourceName}' must contain a culture string and a texts object.");
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
                    $"The UI dictionary '{sourceName}' declares culture '{culture}', but it was registered for '{expectedCulture}'.");
            }

            if (!dictionaries.TryGetValue(
                    culture,
                    out var dictionary))
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
                        $"The UI dictionary '{sourceName}' contains duplicate key '{property.Name}'.");
                }

                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    throw new InvalidOperationException(
                        $"The UI dictionary value '{property.Name}' in '{sourceName}' must be a string.");
                }

                var value = property.Value.GetString()!;
                if (!overwrite && dictionary.ContainsKey(property.Name))
                {
                    throw new InvalidOperationException(
                        $"The embedded UI dictionaries contain duplicate key '{property.Name}' for culture '{culture}'.");
                }

                dictionary[property.Name] = value;
            }
        }
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
                $"The UI dictionary '{sourceName}' declares an invalid culture.",
                exception);
        }
    }
}

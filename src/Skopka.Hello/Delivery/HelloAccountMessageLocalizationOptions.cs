using System.Globalization;

namespace Skopka.Hello;

public sealed class HelloAccountMessageLocalizationOptions
{
    private readonly Dictionary<string, List<string>> dictionaryFiles =
        new(StringComparer.OrdinalIgnoreCase);

    public string DefaultCulture { get; set; } = "en";

    public void AddDictionaryFile(
        string culture,
        string filePath)
    {
        var normalizedCulture = NormalizeCulture(culture);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!dictionaryFiles.TryGetValue(
                normalizedCulture,
                out var files))
        {
            files = [];
            dictionaryFiles.Add(normalizedCulture, files);
        }

        files.Add(filePath.Trim());
    }

    internal IReadOnlyDictionary<string, List<string>> DictionaryFiles =>
        dictionaryFiles;

    internal void Validate()
    {
        DefaultCulture = NormalizeCulture(DefaultCulture);
        foreach (var files in dictionaryFiles.Values)
        {
            foreach (var filePath in files)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            }
        }
    }

    private static string NormalizeCulture(string culture)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(culture);
        try
        {
            var normalized = CultureInfo.GetCultureInfo(
                culture.Trim()).Name;
            if (String.IsNullOrEmpty(normalized))
            {
                throw new InvalidOperationException(
                    "The invariant culture cannot be used for account messages.");
            }

            return normalized;
        }
        catch (CultureNotFoundException exception)
        {
            throw new InvalidOperationException(
                $"'{culture}' is not a valid account-message culture.",
                exception);
        }
    }
}

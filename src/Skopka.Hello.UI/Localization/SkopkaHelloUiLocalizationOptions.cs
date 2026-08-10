using System.Globalization;

namespace Skopka.Hello.UI;

public sealed class SkopkaHelloUiLocalizationOptions
{
    public const string DefaultCultureCookieName =
        "Skopka.Hello.Culture";

    private readonly Dictionary<string, CultureRegistration> cultures =
        new(StringComparer.OrdinalIgnoreCase);

    public SkopkaHelloUiLocalizationOptions()
    {
        AddCulture("en", "English");
        AddCulture("ru", "Русский");
    }

    public bool Enabled { get; set; }

    public string DefaultCulture { get; set; } = "en";

    public string CultureCookieName { get; set; } =
        DefaultCultureCookieName;

    public IReadOnlyList<HelloUiCulture> SupportedCultures =>
        cultures.Values
            .Select(registration => registration.Culture)
            .ToArray();

    public void AddCulture(string culture, string displayName)
    {
        var normalizedCulture = NormalizeCulture(culture);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        if (cultures.TryGetValue(
                normalizedCulture,
                out var registration))
        {
            registration.Culture = new HelloUiCulture(
                normalizedCulture,
                displayName.Trim());
            return;
        }

        cultures.Add(
            normalizedCulture,
            new CultureRegistration(
                new HelloUiCulture(
                    normalizedCulture,
                    displayName.Trim())));
    }

    public void AddDictionaryFile(
        string culture,
        string filePath,
        string? displayName = null)
    {
        var normalizedCulture = NormalizeCulture(culture);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!cultures.TryGetValue(
                normalizedCulture,
                out var registration))
        {
            AddCulture(
                normalizedCulture,
                displayName
                    ?? CultureInfo.GetCultureInfo(normalizedCulture)
                        .NativeName);
            registration = cultures[normalizedCulture];
        }
        else if (displayName is not null)
        {
            AddCulture(normalizedCulture, displayName);
        }

        registration.DictionaryFiles.Add(filePath.Trim());
    }

    internal IReadOnlyList<string> GetDictionaryFiles(string culture)
        => cultures.TryGetValue(culture, out var registration)
            ? registration.DictionaryFiles
            : [];

    internal bool TryGetSupportedCulture(
        string? requestedCulture,
        out HelloUiCulture culture)
    {
        culture = default!;
        if (String.IsNullOrWhiteSpace(requestedCulture))
        {
            return false;
        }

        CultureInfo requested;
        try
        {
            requested = CultureInfo.GetCultureInfo(requestedCulture);
        }
        catch (CultureNotFoundException)
        {
            return false;
        }

        while (!String.IsNullOrEmpty(requested.Name))
        {
            if (cultures.TryGetValue(
                    requested.Name,
                    out var registration))
            {
                culture = registration.Culture;
                return true;
            }

            requested = requested.Parent;
        }

        return false;
    }

    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(CultureCookieName);
        if (CultureCookieName.Length > 256
            || CultureCookieName.Any(character =>
                Char.IsWhiteSpace(character)
                || Char.IsControl(character)
                || character is ';' or ',' or '='))
        {
            throw new InvalidOperationException(
                "The localization culture cookie name is invalid.");
        }

        DefaultCulture = NormalizeCulture(DefaultCulture);
        if (!cultures.ContainsKey(DefaultCulture))
        {
            throw new InvalidOperationException(
                "The default UI culture must be one of the supported cultures.");
        }

        if (cultures.Count == 0)
        {
            throw new InvalidOperationException(
                "At least one UI culture must be configured.");
        }

        foreach (var registration in cultures.Values)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(
                registration.Culture.DisplayName);
            foreach (var filePath in registration.DictionaryFiles)
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
            var normalized = CultureInfo.GetCultureInfo(culture.Trim()).Name;
            if (String.IsNullOrEmpty(normalized))
            {
                throw new InvalidOperationException(
                    "The invariant culture cannot be used for UI localization.");
            }

            return normalized;
        }
        catch (CultureNotFoundException exception)
        {
            throw new InvalidOperationException(
                $"'{culture}' is not a valid UI culture.",
                exception);
        }
    }

    private sealed class CultureRegistration(HelloUiCulture culture)
    {
        public HelloUiCulture Culture { get; set; } = culture;

        public List<string> DictionaryFiles { get; } = [];
    }
}

using System.Globalization;
using Microsoft.Extensions.Localization;

namespace Skopka.Hello.UI;

internal sealed class HelloUiLocalizer(
    HelloUiTextCatalog catalog,
    SkopkaHelloUiOptions options)
    : IHelloUiLocalizer
{
    public LocalizedString this[string name]
    {
        get
        {
            var found = TryGetString(name, out var value);
            return new LocalizedString(
                name,
                value,
                resourceNotFound: !found);
        }
    }

    public LocalizedString this[
        string name,
        params object[] arguments]
    {
        get
        {
            var found = TryGetString(name, out var format);
            var value = String.Format(
                CultureInfo.CurrentCulture,
                format,
                arguments);
            return new LocalizedString(
                name,
                value,
                resourceNotFound: !found);
        }
    }

    public bool TryGetString(string name, out string value)
        => catalog.TryGetString(
            GetCultureName(),
            name,
            out value);

    public IEnumerable<LocalizedString> GetAllStrings(
        bool includeParentCultures)
        => catalog.GetAllStrings(
                GetCultureName(),
                includeParentCultures)
            .Select(pair => new LocalizedString(
                pair.Key,
                pair.Value,
                resourceNotFound: false));

    private string GetCultureName()
        => options.Localization.Enabled
            ? CultureInfo.CurrentUICulture.Name
            : options.Localization.DefaultCulture;
}

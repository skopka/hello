using Microsoft.Extensions.Localization;
using Skopka.Hello.UI;

namespace Skopka.Hello.Tests;

internal sealed class TestHelloUiLocalizer : IHelloUiLocalizer
{
    public static TestHelloUiLocalizer Instance { get; } = new();

    public LocalizedString this[string name] =>
        new(name, name, resourceNotFound: true);

    public LocalizedString this[
        string name,
        params object[] arguments] =>
        new(name, name, resourceNotFound: true);

    public bool TryGetString(string name, out string value)
    {
        value = name;
        return false;
    }

    public IEnumerable<LocalizedString> GetAllStrings(
        bool includeParentCultures)
        => [];
}

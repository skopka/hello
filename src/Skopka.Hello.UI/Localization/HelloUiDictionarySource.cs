using System.Reflection;

namespace Skopka.Hello.UI;

internal sealed record HelloUiDictionarySource(
    Assembly Assembly,
    IReadOnlyList<string> ResourceNames);

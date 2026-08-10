using Microsoft.Extensions.Localization;

namespace Skopka.Hello.UI;

public interface IHelloUiLocalizer : IStringLocalizer
{
    bool TryGetString(string name, out string value);
}

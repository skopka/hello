using System.Globalization;

namespace Skopka.Hello;

public sealed class HelloPhoneSmsTemplates(SkopkaHelloOptions options)
{
    public string Render(string code, string? culture = null)
    {
        var language = culture ?? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var template = language.Equals("ru", StringComparison.OrdinalIgnoreCase)
            ? options.RussianPhoneCodeTemplate
            : options.EnglishPhoneCodeTemplate;
        return template
            .Replace("{application}", options.SmsApplicationName,
                StringComparison.Ordinal)
            .Replace("{code}", code, StringComparison.Ordinal);
    }
}

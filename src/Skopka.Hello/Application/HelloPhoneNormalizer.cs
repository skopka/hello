using System.Globalization;

namespace Skopka.Hello;

internal static class HelloPhoneNormalizer
{
    internal static string Normalize(string? phone, string? region)
    {
        if (phone is null) return string.Empty;
        if (region is null) return phone;
        var compact = new string(phone.Where(c => char.GetUnicodeCategory(c) != UnicodeCategory.SpaceSeparator
            && c is not '(' and not ')' and not '-').ToArray());
        var digits = compact.StartsWith('+') ? compact[1..] : compact;
        if (digits.Length == 0 || digits.Any(c => c < '0' || c > '9')) return phone;
        if (compact.StartsWith('+')) return compact;
        if (digits.Length == 10) return "+7" + digits;
        if (digits.Length == 11 && (digits[0] == '7' || digits[0] == '8'))
            return "+7" + digits[1..];
        return phone;
    }
}

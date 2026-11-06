namespace Skopka.Hello.PhoneSqliteBlazor;

public sealed record SampleProfile(string DisplayName, string? Locale)
{
    public string? SchoolClass { get; init; }
}

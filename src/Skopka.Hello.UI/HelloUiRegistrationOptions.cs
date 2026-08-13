namespace Skopka.Hello.UI;

public enum HelloUiRegistrationField
{
    DisplayName,
    Email,
    UserName,
    Phone,
    Locale,
}

public enum HelloUiRegistrationFieldMode
{
    Hidden,
    Optional,
    Required,
}

public sealed class HelloUiRegistrationOptions
{
    public HelloUiRegistrationFieldMode DisplayName { get; set; } =
        HelloUiRegistrationFieldMode.Required;

    public HelloUiRegistrationFieldMode Email { get; set; } =
        HelloUiRegistrationFieldMode.Optional;

    public HelloUiRegistrationFieldMode UserName { get; set; } =
        HelloUiRegistrationFieldMode.Optional;

    public HelloUiRegistrationFieldMode Phone { get; set; } =
        HelloUiRegistrationFieldMode.Optional;

    public HelloUiRegistrationFieldMode Locale { get; set; } =
        HelloUiRegistrationFieldMode.Hidden;

    public HelloUiRegistrationFieldMode GetMode(
        HelloUiRegistrationField field)
        => field switch
        {
            HelloUiRegistrationField.DisplayName => DisplayName,
            HelloUiRegistrationField.Email => Email,
            HelloUiRegistrationField.UserName => UserName,
            HelloUiRegistrationField.Phone => Phone,
            HelloUiRegistrationField.Locale => Locale,
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };

    public bool IsVisible(HelloUiRegistrationField field)
        => GetMode(field) != HelloUiRegistrationFieldMode.Hidden;

    public bool IsRequired(HelloUiRegistrationField field)
        => GetMode(field) == HelloUiRegistrationFieldMode.Required;

    internal void Validate()
    {
        foreach (var field in Enum.GetValues<HelloUiRegistrationField>())
        {
            var mode = GetMode(field);
            if (!Enum.IsDefined(mode))
            {
                throw new InvalidOperationException(
                    $"Registration.{field} contains an unsupported field mode.");
            }
        }

        if (!IsVisible(HelloUiRegistrationField.Email)
            && !IsVisible(HelloUiRegistrationField.UserName)
            && !IsVisible(HelloUiRegistrationField.Phone))
        {
            throw new InvalidOperationException(
                "Registration must show at least one of Email, UserName or Phone so password registration can create a usable login handle.");
        }
    }
}

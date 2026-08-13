using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Skopka.Hello.UI.Pages;

internal sealed record HelloUiRegistrationFormValues(
    string DisplayName,
    string? Email,
    string? UserName,
    string? Phone,
    string? Locale);

internal static class HelloUiRegistrationFormValidator
{
    public static HelloUiRegistrationFormValues Validate(
        HelloUiRegistrationOptions options,
        ModelStateDictionary modelState,
        IHelloUiLocalizer localizer,
        string displayName,
        string? email,
        string? userName,
        string? phone,
        string? locale,
        bool requireLoginIdentifier)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(modelState);
        ArgumentNullException.ThrowIfNull(localizer);

        displayName = Prepare(
            options,
            HelloUiRegistrationField.DisplayName,
            modelState,
            nameof(displayName),
            displayName) ?? string.Empty;
        email = Prepare(
            options,
            HelloUiRegistrationField.Email,
            modelState,
            nameof(email),
            email);
        userName = Prepare(
            options,
            HelloUiRegistrationField.UserName,
            modelState,
            nameof(userName),
            userName);
        phone = Prepare(
            options,
            HelloUiRegistrationField.Phone,
            modelState,
            nameof(phone),
            phone);
        locale = Prepare(
            options,
            HelloUiRegistrationField.Locale,
            modelState,
            nameof(locale),
            locale);

        AddRequiredError(
            options,
            HelloUiRegistrationField.DisplayName,
            modelState,
            nameof(displayName),
            displayName,
            localizer);
        AddRequiredError(
            options,
            HelloUiRegistrationField.Email,
            modelState,
            nameof(email),
            email,
            localizer);
        AddRequiredError(
            options,
            HelloUiRegistrationField.UserName,
            modelState,
            nameof(userName),
            userName,
            localizer);
        AddRequiredError(
            options,
            HelloUiRegistrationField.Phone,
            modelState,
            nameof(phone),
            phone,
            localizer);
        AddRequiredError(
            options,
            HelloUiRegistrationField.Locale,
            modelState,
            nameof(locale),
            locale,
            localizer);

        if (requireLoginIdentifier
            && string.IsNullOrWhiteSpace(email)
            && string.IsNullOrWhiteSpace(userName)
            && string.IsNullOrWhiteSpace(phone))
        {
            modelState.AddModelError(
                string.Empty,
                localizer["Validation.LoginIdentifierRequired"].Value);
        }

        return new HelloUiRegistrationFormValues(
            displayName,
            email,
            userName,
            phone,
            locale);
    }

    private static string? Prepare(
        HelloUiRegistrationOptions options,
        HelloUiRegistrationField field,
        ModelStateDictionary modelState,
        string propertyName,
        string? value)
    {
        if (options.IsVisible(field))
        {
            return value;
        }

        modelState.Remove(ToInputKey(propertyName));
        return null;
    }

    private static void AddRequiredError(
        HelloUiRegistrationOptions options,
        HelloUiRegistrationField field,
        ModelStateDictionary modelState,
        string propertyName,
        string? value,
        IHelloUiLocalizer localizer)
    {
        if (options.IsRequired(field)
            && string.IsNullOrWhiteSpace(value))
        {
            modelState.AddModelError(
                ToInputKey(propertyName),
                localizer[
                    "Validation.Required",
                    localizer[$"Field.{field}"].Value].Value);
        }
    }

    private static string ToInputKey(string propertyName)
        => $"Input.{char.ToUpperInvariant(propertyName[0])}{propertyName[1..]}";
}

using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Localization;
using Skopka.Abstraction.OperationResult;
using Skopka.Hello.UI;
using Skopka.Hello.UI.Pages;
using Skopka.Identity.Errors;

namespace Skopka.Hello.Tests;

public sealed class HelloUiModelStateTests
{
    [Theory]
    [InlineData(
        "Password must contain at least 15 characters.",
        "Пароль должен содержать не менее 15 символов.")]
    [InlineData(
        "Password must not exceed 128 characters.",
        "Пароль должен содержать не более 128 символов.")]
    public void PasswordLengthDetailsAreLocalizedAndAttachedToField(
        string detail,
        string expected)
    {
        var modelState = new ModelStateDictionary();
        modelState.SetModelValue("Input.Password", "", "");

        HelloUiModelState.AddErrors(
            modelState,
            [CreatePasswordRejected(detail)],
            new DictionaryLocalizer(),
            _ => "Input.Password");

        var error = Assert.Single(
            modelState["Input.Password"]!.Errors);
        Assert.Equal(expected, error.ErrorMessage);
        Assert.False(modelState.ContainsKey(string.Empty));
    }

    [Fact]
    public void CustomPasswordValidatorDetailIsNotHiddenByGenericMessage()
    {
        const string detail =
            "Use a password that is not present in the breach list.";
        var modelState = new ModelStateDictionary();
        modelState.SetModelValue("Input.Password", "", "");

        HelloUiModelState.AddErrors(
            modelState,
            [CreatePasswordRejected(detail)],
            new DictionaryLocalizer(),
            _ => "Input.Password");

        var error = Assert.Single(
            modelState["Input.Password"]!.Errors);
        Assert.Equal(detail, error.ErrorMessage);
    }

    private static Error CreatePasswordRejected(string detail)
        => new(
            IdentityErrorCodes.PasswordRejected,
            "The password does not satisfy the configured policy.",
            ErrorType.Validation,
            new ValidationDetails(
                new Dictionary<string, string[]>
                {
                    ["newPassword"] = [detail],
                }));

    private sealed class DictionaryLocalizer : IHelloUiLocalizer
    {
        private static readonly Dictionary<string, string> Texts =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Errors.identity.password.rejected"] =
                    "Пароль не соответствует настроенной политике.",
                ["Errors.identity.password.rejected.minimum_length"] =
                    "Пароль должен содержать не менее {0} символов.",
                ["Errors.identity.password.rejected.maximum_length"] =
                    "Пароль должен содержать не более {0} символов.",
            };

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
                return new LocalizedString(
                    name,
                    String.Format(
                        CultureInfo.CurrentCulture,
                        format,
                        arguments),
                    resourceNotFound: !found);
            }
        }

        public bool TryGetString(string name, out string value)
        {
            if (Texts.TryGetValue(name, out var found))
            {
                value = found;
                return true;
            }

            value = name;
            return false;
        }

        public IEnumerable<LocalizedString> GetAllStrings(
            bool includeParentCultures)
            => Texts.Select(pair => new LocalizedString(
                pair.Key,
                pair.Value,
                resourceNotFound: false));
    }
}

using System.ComponentModel.DataAnnotations;
using Skopka.Hello.UI.Pages;

namespace Skopka.Hello.Tests;

public sealed class RegisterPageValidationTests
{
    [Fact]
    public void PhoneOnlyRegistrationIsValid()
    {
        var input = new RegisterModel.InputModel
        {
            DisplayName = "Alice",
            Phone = "+1 555 010 4242",
            Password = "correct horse battery staple",
            ConfirmPassword = "correct horse battery staple",
        };
        List<ValidationResult> errors = [];

        var valid = Validator.TryValidateObject(
            input,
            new ValidationContext(input),
            errors,
            validateAllProperties: true);

        Assert.True(valid);
        Assert.Empty(errors);
    }

    [Fact]
    public void RegistrationRequiresAtLeastOneLoginHandle()
    {
        var input = new RegisterModel.InputModel
        {
            DisplayName = "Alice",
            Password = "correct horse battery staple",
            ConfirmPassword = "correct horse battery staple",
        };
        List<ValidationResult> errors = [];

        var valid = Validator.TryValidateObject(
            input,
            new ValidationContext(input),
            errors,
            validateAllProperties: true);

        Assert.False(valid);
        Assert.Contains(
            errors,
            error => error.ErrorMessage
                == "Enter a user name, email address or phone number.");
    }
}

using Microsoft.AspNetCore.Mvc.ModelBinding;
using Skopka.Hello.UI;
using Skopka.Hello.UI.Pages;

namespace Skopka.Hello.Tests;

public sealed class RegisterPageValidationTests
{
    [Theory]
    [InlineData(HelloUiRegistrationField.Email)]
    [InlineData(HelloUiRegistrationField.UserName)]
    [InlineData(HelloUiRegistrationField.Phone)]
    public void SingleIdentifierConfigurationAcceptsItsValue(
        HelloUiRegistrationField identifier)
    {
        var options = CreateSingleIdentifierOptions(identifier);
        var modelState = new ModelStateDictionary();

        var values = HelloUiRegistrationFormValidator.Validate(
            options,
            modelState,
            TestHelloUiLocalizer.Instance,
            displayName: "Alice",
            email: identifier == HelloUiRegistrationField.Email
                ? "alice@example.test"
                : null,
            userName: identifier == HelloUiRegistrationField.UserName
                ? "alice"
                : null,
            phone: identifier == HelloUiRegistrationField.Phone
                ? "+1 555 010 4242"
                : null,
            locale: null,
            requireLoginIdentifier: true);

        Assert.True(modelState.IsValid);
        Assert.NotNull(identifier switch
        {
            HelloUiRegistrationField.Email => values.Email,
            HelloUiRegistrationField.UserName => values.UserName,
            HelloUiRegistrationField.Phone => values.Phone,
            _ => null,
        });
    }

    [Fact]
    public void RequiredConfiguredFieldProducesFieldError()
    {
        var options = CreateSingleIdentifierOptions(
            HelloUiRegistrationField.Phone);
        var modelState = new ModelStateDictionary();

        HelloUiRegistrationFormValidator.Validate(
            options,
            modelState,
            TestHelloUiLocalizer.Instance,
            displayName: "Alice",
            email: null,
            userName: null,
            phone: null,
            locale: null,
            requireLoginIdentifier: true);

        Assert.False(modelState.IsValid);
        Assert.NotEmpty(modelState["Input.Phone"]!.Errors);
        Assert.NotEmpty(modelState[string.Empty]!.Errors);
    }

    [Fact]
    public void HiddenPostedFieldsAreDiscarded()
    {
        var options = CreateSingleIdentifierOptions(
            HelloUiRegistrationField.Phone);
        var modelState = new ModelStateDictionary();
        modelState.SetModelValue(
            "Input.Email",
            "injected@example.test",
            "injected@example.test");
        modelState.SetModelValue(
            "Input.UserName",
            "injected-user",
            "injected-user");
        modelState.SetModelValue(
            "Input.Locale",
            "ru",
            "ru");

        var values = HelloUiRegistrationFormValidator.Validate(
            options,
            modelState,
            TestHelloUiLocalizer.Instance,
            displayName: "Alice",
            email: "injected@example.test",
            userName: "injected-user",
            phone: "+1 555 010 4242",
            locale: "ru",
            requireLoginIdentifier: true);

        Assert.Null(values.Email);
        Assert.Null(values.UserName);
        Assert.Null(values.Locale);
        Assert.False(modelState.ContainsKey("Input.Email"));
        Assert.False(modelState.ContainsKey("Input.UserName"));
        Assert.False(modelState.ContainsKey("Input.Locale"));
    }

    [Fact]
    public void ExternalRegistrationAllowsOptionalIdentifiersToBeEmpty()
    {
        var options = new HelloUiRegistrationOptions();
        var modelState = new ModelStateDictionary();

        HelloUiRegistrationFormValidator.Validate(
            options,
            modelState,
            TestHelloUiLocalizer.Instance,
            displayName: "Alice",
            email: null,
            userName: null,
            phone: null,
            locale: null,
            requireLoginIdentifier: false);

        Assert.True(modelState.IsValid);
    }

    private static HelloUiRegistrationOptions
        CreateSingleIdentifierOptions(
            HelloUiRegistrationField identifier)
    {
        var options = new HelloUiRegistrationOptions
        {
            Email = HelloUiRegistrationFieldMode.Hidden,
            UserName = HelloUiRegistrationFieldMode.Hidden,
            Phone = HelloUiRegistrationFieldMode.Hidden,
        };

        switch (identifier)
        {
            case HelloUiRegistrationField.Email:
                options.Email = HelloUiRegistrationFieldMode.Required;
                break;
            case HelloUiRegistrationField.UserName:
                options.UserName = HelloUiRegistrationFieldMode.Required;
                break;
            case HelloUiRegistrationField.Phone:
                options.Phone = HelloUiRegistrationFieldMode.Required;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(identifier));
        }

        return options;
    }
}

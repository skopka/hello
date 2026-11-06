using Skopka.Hello.UI;

namespace Skopka.Hello.Tests;

public sealed class HelloUiRegistrationFieldsValidatorTests
{
    private static readonly HelloUiProfileField[] Fields =
        [new("grade", "Grade", null, Required: true, MaximumLength: 4)];

    [Fact]
    public void UndeclaredFieldsAreRejected()
    {
        var result = HelloUiRegistrationFieldsValidator.Validate(Fields,
            new Dictionary<string, string?> { ["grade"] = "7", ["extra"] = "x" });
        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, error => error.Code == "hello.ui.registration_fields_invalid");
    }

    [Theory]
    [InlineData("", "hello.ui.registration_field_required")]
    [InlineData("12345", "hello.ui.registration_field_too_long")]
    public void InvalidValuesReturnFieldDetails(string value, string code)
    {
        var result = HelloUiRegistrationFieldsValidator.Validate(Fields,
            new Dictionary<string, string?> { ["grade"] = value });
        Assert.False(result.IsSuccess);
        var error = Assert.Single(result.Errors);
        Assert.Equal(code, error.Code);
        var details = Assert.IsType<Skopka.Identity.Errors.ValidationDetails>(error.Details);
        Assert.Contains("grade", details.Fields.Keys);
    }
    [Theory]
    [InlineData("", false)]
    [InlineData("12345", false)]
    [InlineData("7", true)]
    public async Task InvalidValuesStopBeforeTheProfileFactory(string value, bool extra)
    {
        var factory = new Factory();
        var application = new HelloUiPhoneApplication<string>(null!, factory, null!, null!,
            new Skopka.Hello.SkopkaHelloOptions(), TimeProvider.System, new FieldProvider());
        var values = new Dictionary<string, string?> { ["grade"] = value };
        if (extra) values["extra"] = "x";
        var result = await application.RegisterWithValuesAsync("ticket", "Name", null, false, false,
            values, new Microsoft.AspNetCore.Http.DefaultHttpContext(), CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.False(factory.Called);
    }

    private sealed class Factory : IHelloUiProfileFactory<string>
    {
        public bool Called { get; private set; }
        public Skopka.Abstraction.OperationResult.OperationResult<string> Create(HelloUiRegistrationProfile profile)
        {
            Called = true;
            return Skopka.Abstraction.OperationResult.OperationResultFactory.Success("value");
        }
        public string GetDisplayName(string profile) => profile;
    }

    private sealed class FieldProvider : IHelloUiRegistrationFields
    {
        public IReadOnlyList<HelloUiProfileField> GetFields() => Fields;
        public Skopka.Abstraction.OperationResult.OperationResult<IReadOnlyDictionary<string, string?>> Validate(
            IReadOnlyDictionary<string, string?> values)
            => throw new InvalidOperationException("Invalid inputs must stop before host validation.");
    }
}

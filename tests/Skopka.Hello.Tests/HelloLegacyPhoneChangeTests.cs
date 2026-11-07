using Skopka.Abstraction.OperationResult;

namespace Skopka.Hello.Tests;

public sealed class HelloLegacyPhoneChangeTests
{
    [Theory]
    [InlineData("+15551234567")]
    [InlineData(null)]
    public async Task DirectPhoneMutationFailsWithoutCallingIdentity(string? phone)
    {
        // Null dependencies make any attempted Identity call fail the test.
        var application = new HelloIdentityApplication<object>(
            registration: null!, authentication: null!, sessions: null!,
            credentials: null!, users: null!, stepUp: null!, verification: null!,
            anonymousMessageRequester: null!, messageSender: null!,
            deliveryOptions: new HelloDeliveryOptions(), options: new SkopkaHelloOptions(),
            phoneInputPolicy: null!);

        var result = await application.ChangePhoneAsync(
            new HelloChangePhoneCommand("access-token", 1, phone), CancellationToken.None);

        Assert.False(result.IsSuccess);
        var error = Assert.Single(result.Errors);
        Assert.Equal("hello.phone_binding.required", error.Code);
        Assert.Equal(ErrorType.Forbidden, error.Type);
    }
}

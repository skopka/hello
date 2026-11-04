using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skopka.Hello.AuthorizationServer;
using Xunit;

namespace Skopka.Hello.IntegrationTests;

public sealed class NativeAppOptionsTests
{
    [Fact]
    public void InvalidEnabledPolicyFailsAtRegistration()
    {
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection()
            .AddHelloNativeAppSignIn<string, Context>(options =>
            {
                options.Enabled = true;
                options.AllowedCallbacks["app"] = ["com.example.sample://callback"];
                options.ApprovalLifetime = TimeSpan.FromMinutes(2);
            }));
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection()
            .AddHelloNativeAppSignIn<string, Context>(options =>
            {
                options.Enabled = true;
                options.AllowedCallbacks["app"] = ["/relative"];
            }));
    }

    [Theory]
    [InlineData("http://client.example/callback")]
    [InlineData("javascript:alert(1)")]
    [InlineData("custom://callback")]
    [InlineData("com.example.app://callback#fragment")]
    [InlineData("https://user@client.example/callback")]
    [InlineData("https://client.example/callback?%73tate=old")]
    public void UnsafeCallbacksFailAtRegistration(string callback)
    {
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection()
            .AddHelloNativeAppSignIn<string, Context>(options =>
            {
                options.Enabled = true;
                options.AllowedCallbacks["app"] = [callback];
            }));
    }
    private sealed class Context(DbContextOptions<Context> options) : DbContext(options) { }
}

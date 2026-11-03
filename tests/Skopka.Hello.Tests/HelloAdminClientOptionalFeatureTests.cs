using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Skopka.Abstraction.OperationResult;
using Skopka.Hello.Admin;
using Skopka.Hello.AuthorizationServer;
using Skopka.Hello.UI;

namespace Skopka.Hello.Tests;

public sealed class HelloAdminClientOptionalFeatureTests
{
    [Fact]
    public async Task AdminWithoutAuthorizationServerHasNoClientRoutesOrRequiredService()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSkopkaHello<TestProfile>();
        builder.Services.AddSkopkaHelloUi<TestProfile, TestProfileFactory>();
        builder.Services.AddSkopkaHelloAdmin<TestProfile, TestProfileProjector>();

        // Validate the optional registration in isolation: the rest of this test
        // host intentionally does not register an Identity implementation.
        var descriptor = Assert.Single(builder.Services,
            item => item.ServiceType == typeof(IHelloAdminClientApplication));
        IServiceCollection optionalServices = new ServiceCollection();
        optionalServices.Add(descriptor);
        using var validated = optionalServices.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        Assert.False(validated.GetRequiredService<IServiceProviderIsService>()
            .IsService(typeof(IHelloAuthorizationClientManagement)));

        await using var application = builder.Build();
        application.MapSkopkaHelloAdmin<TestProfile>();
        application.MapSkopkaHelloUi();
        var routes = ((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText ?? string.Empty)
            .ToArray();
        Assert.DoesNotContain(routes, route => route.Contains("/clients", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(routes, route => route.EndsWith("/users", StringComparison.OrdinalIgnoreCase));
    }

    private sealed record TestProfile(string DisplayName);

    private sealed class TestProfileFactory : IHelloUiProfileFactory<TestProfile>
    {
        public OperationResult<TestProfile> Create(HelloUiRegistrationProfile profile)
            => OperationResultFactory.Success(new TestProfile(profile.DisplayName));

        public string GetDisplayName(TestProfile profile) => profile.DisplayName;
    }

    private sealed class TestProfileProjector : IHelloAdminProfileProjector<TestProfile>
    {
        public Task<OperationResult<IReadOnlyList<HelloAdminProfileField>>> ProjectAsync(
            TestProfile profile,
            HelloAdminProfileProjectionContext context,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<HelloAdminProfileField> fields = [];
            return Task.FromResult(OperationResultFactory.Success(fields));
        }
    }
}

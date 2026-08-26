using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Skopka.Abstraction.OperationResult;
using Skopka.Hello.Admin;
using Skopka.Hello.UI;

namespace Skopka.Hello.Tests;

public sealed class HelloAdminRoutingTests
{
    [Fact]
    public async Task AdminApiAndUiUseConfiguredPrefixes()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSkopkaHello<TestProfile>(options =>
            options.UiPathPrefix = "/identity");
        builder.Services.AddSkopkaHelloUi<
            TestProfile,
            TestProfileFactory>();
        builder.Services.AddSkopkaHelloAdmin<
            TestProfile,
            TestProfileProjector>(options =>
            options.ApiPathPrefix = "/management");

        await using var application = builder.Build();
        application.MapSkopkaHelloAdmin<TestProfile>();
        application.MapSkopkaHelloUi();

        var routes = ((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => "/" + (endpoint.RoutePattern.RawText
                ?? string.Empty).TrimStart('/'))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("/management/users", routes);
        Assert.Contains("/management/roles", routes);
        Assert.Contains("/management/users/{userId:guid}/roles", routes);
        Assert.Contains(
            "/management/roles/actions/{action}/challenge",
            routes);
        Assert.Contains(
            "/management/users/{userId:guid}/actions/{action}/challenge",
            routes);
        Assert.Contains("/identity/management", routes);
        Assert.Contains("/identity/management/users", routes);
        Assert.DoesNotContain("/hello/admin/users", routes);

        var paths = application.Services
            .GetRequiredService<HelloAdminRoutePaths>();
        Assert.Equal("/identity/management", paths.RootPath);
        Assert.Equal("/identity/management/users", paths.UsersPath);
        Assert.Equal("/identity/management/roles", paths.RolesPath);
    }

    [Fact]
    public void DuplicatePolicyNamesAreRejected()
    {
        var options = new SkopkaHelloAdminOptions
        {
            ManagePolicyName = HelloAdminDefaults.ReadPolicy,
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void RoleRemovalSessionRevocationDefaultsToAlways()
    {
        var options = new SkopkaHelloAdminOptions();

        Assert.Equal(
            HelloSessionRevocationScope.Always,
            options.RevokeSessionsOnRoleRemoval);
    }

    [Fact]
    public void InvalidRoleRemovalSessionRevocationScopeIsRejected()
    {
        var options = new SkopkaHelloAdminOptions
        {
            RevokeSessionsOnRoleRemoval =
                (HelloSessionRevocationScope)(-1),
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void AssignmentAllowAndDenyListsCannotBothBeConfigured()
    {
        var options = new SkopkaHelloAdminOptions();
        options.RoleAssignment.RoleName = "iq-manager";
        options.RoleAssignment.Assignable = ["iq-author"];
        options.RoleAssignment.NotAssignable = ["iq-admin"];

        var exception = Assert.Throws<InvalidOperationException>(
            options.Validate);

        Assert.Contains(
            "cannot both be configured",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AssignmentFilterRequiresDelegateRole()
    {
        var options = new SkopkaHelloAdminOptions();
        options.RoleAssignment.Assignable = ["iq-author"];

        var exception = Assert.Throws<InvalidOperationException>(
            options.Validate);

        Assert.Contains(
            "RoleAssignment.RoleName is required",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/account/admin")]
    [InlineData("/swagger/admin")]
    [InlineData("/_content/admin")]
    public void ReservedApiPrefixesAreRejected(string prefix)
    {
        var options = new SkopkaHelloAdminOptions
        {
            ApiPathPrefix = prefix,
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("/admin/")]
    [InlineData("/admin/{tenant}")]
    [InlineData("/admin/../users")]
    [InlineData("/admin users")]
    public void NonLiteralApiPrefixesAreRejected(string prefix)
    {
        var options = new SkopkaHelloAdminOptions
        {
            ApiPathPrefix = prefix,
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void ApiAndUiNamespacesCannotOverlap()
    {
        var options = new SkopkaHelloAdminOptions
        {
            ApiPathPrefix = "/identity/admin",
        };
        options.Validate();

        Assert.Throws<InvalidOperationException>(
            () => new HelloAdminRoutePaths(
                new HelloUiRoutePaths("/identity"),
                options));
    }

    [Fact]
    public void EnabledRazorUiRequiresHelloUiRegistrationFirst()
    {
        var services = new ServiceCollection();
        services.AddSkopkaHello<TestProfile>();

        Assert.Throws<InvalidOperationException>(
            () => services.AddSkopkaHelloAdmin<
                TestProfile,
                TestProfileProjector>());
    }

    [Fact]
    public async Task DisabledRazorUiDoesNotExposeFallbackPageRoute()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSkopkaHello<TestProfile>();
        builder.Services.AddSkopkaHelloUi<
            TestProfile,
            TestProfileFactory>();
        builder.Services.AddSkopkaHelloAdmin<
            TestProfile,
            TestProfileProjector>(options =>
            options.RazorUiEnabled = false);

        await using var application = builder.Build();
        application.MapSkopkaHelloAdmin<TestProfile>();
        application.MapSkopkaHelloUi();

        var routes = ((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => "/" + (endpoint.RoutePattern.RawText
                ?? string.Empty).TrimStart('/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.DoesNotContain("/hello/admin/users", routes);
        Assert.DoesNotContain("/hello/admin/roles", routes);
        Assert.DoesNotContain("/hello/admin", routes);
        Assert.DoesNotContain("/SkopkaHelloAdmin/Users", routes);
        Assert.DoesNotContain("/SkopkaHelloAdmin/Roles", routes);
        Assert.DoesNotContain("/Users", routes);
        Assert.Contains("/admin/users", routes);
        Assert.Contains("/admin/roles", routes);
    }

    [Fact]
    public async Task ExistingHostAdminRouteCollisionIsRejected()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSkopkaHello<TestProfile>();
        builder.Services.AddSkopkaHelloAdmin<
            TestProfile,
            TestProfileProjector>(options =>
            options.RazorUiEnabled = false);

        await using var application = builder.Build();
        application.MapGet("/admin/users", () => "host");

        var exception = Assert.Throws<InvalidOperationException>(
            application.MapSkopkaHelloAdmin<TestProfile>);
        Assert.Contains(
            "collides",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    private sealed record TestProfile(string DisplayName);

    private sealed class TestProfileFactory
        : IHelloUiProfileFactory<TestProfile>
    {
        public OperationResult<TestProfile> Create(
            HelloUiRegistrationProfile profile)
            => OperationResultFactory.Success(
                new TestProfile(profile.DisplayName));

        public string GetDisplayName(TestProfile profile)
            => profile.DisplayName;
    }

    private sealed class TestProfileProjector
        : IHelloAdminProfileProjector<TestProfile>
    {
        public Task<OperationResult<IReadOnlyList<HelloAdminProfileField>>>
            ProjectAsync(
                TestProfile profile,
                HelloAdminProfileProjectionContext context,
                CancellationToken cancellationToken)
        {
            IReadOnlyList<HelloAdminProfileField> fields =
                [new("displayName", "Display name", profile.DisplayName)];
            return Task.FromResult(OperationResultFactory.Success(fields));
        }
    }
}

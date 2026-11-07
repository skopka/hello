using Microsoft.AspNetCore.Http;
using Skopka.Hello;
using Skopka.Identity.Sessions;
using Skopka.Identity.Users;

namespace Skopka.Hello.Tests;

public sealed class HelloSitesTests
{
    private sealed record Profile(string Type);
    private sealed class Policy : IHelloSignInPolicy<Profile>
    {
        public Task<bool> IsAllowedAsync(HelloSignInContext<Profile> context, CancellationToken ct)
            => Task.FromResult(context.Account.Profile.Type == context.Site.Id);
    }
    private static IdentityUser<Profile> User(string type) => new(Guid.NewGuid(), default, "name", null, false,
        null, false, new Profile(type), 1, "stamp", null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
    private static (HelloSiteCatalog Catalog, HelloSiteContext Sites, HttpContext Context) Setup()
    {
        var options = new SkopkaHelloOptions { PhoneOtpEnabled = true };
        options.Sites.Add(new HelloSiteOptions { Id = "main", PublicOrigin = new Uri("https://main.test") });
        options.Sites.Add(new HelloSiteOptions
        {
            Id = "staff",
            PublicOrigin = new Uri("https://staff.test"),
            PhoneOtpEnabled = false,
            SelfRegistrationEnabled = false,
            EnabledFeatures = HelloSiteFeatures.Login | HelloSiteFeatures.Account
        });
        var catalog = new HelloSiteCatalog(options);
        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("staff.test");
        return (catalog, new HelloSiteContext(catalog, new HttpContextAccessor { HttpContext = http }), http);
    }
    private static IdentitySessionAdmissionContext<Profile> Admission(IdentityUser<Profile> user, string site, string method,
        IdentitySessionAdmissionOperation operation) => new(user, default, operation, null, null)
        { Context = new IdentitySessionContext(site, method) };

    [Fact]
    public async Task StaffPasswordRequiresMatchingAccountAndSite()
    {
        var (catalog, sites, _) = Setup();
        var admission = new HelloSiteSessionAdmission<Profile>(sites, catalog, [new Policy()]);
        Assert.True((await admission.CheckAsync(Admission(User("staff"), "staff", HelloAuthenticationMethods.Password,
            IdentitySessionAdmissionOperation.Create), CancellationToken.None)).IsSuccess);
        Assert.False((await admission.CheckAsync(Admission(User("main"), "staff", HelloAuthenticationMethods.Password,
            IdentitySessionAdmissionOperation.Create), CancellationToken.None)).IsSuccess);
        Assert.False((await admission.CheckAsync(Admission(User("staff"), "staff", HelloAuthenticationMethods.Phone,
            IdentitySessionAdmissionOperation.Create), CancellationToken.None)).IsSuccess);
    }

    [Fact]
    public async Task CrossSiteRefreshAndUnknownHostAreDenied()
    {
        var (catalog, sites, http) = Setup();
        var admission = new HelloSiteSessionAdmission<Profile>(sites, catalog, [new Policy()]);
        var stored = Admission(User("staff"), "staff", HelloAuthenticationMethods.Password, IdentitySessionAdmissionOperation.Refresh);
        http.Request.Host = new HostString("main.test");
        Assert.False((await admission.CheckAsync(stored, CancellationToken.None)).IsSuccess);
        http.Request.Host = new HostString("unknown.test");
        Assert.False((await admission.CheckAsync(stored, CancellationToken.None)).IsSuccess);
    }

    [Fact]
    public async Task StoredMethodRemainsEnforcedForRefreshAndValidation()
    {
        var (catalog, sites, _) = Setup();
        var admission = new HelloSiteSessionAdmission<Profile>(sites, catalog, [new Policy()]);
        foreach (var operation in new[] { IdentitySessionAdmissionOperation.Refresh, IdentitySessionAdmissionOperation.Validate })
        {
            Assert.True((await admission.CheckAsync(Admission(User("staff"), "staff", HelloAuthenticationMethods.Password, operation), CancellationToken.None)).IsSuccess);
            Assert.False((await admission.CheckAsync(Admission(User("staff"), "staff", HelloAuthenticationMethods.Phone, operation), CancellationToken.None)).IsSuccess);
        }
    }

    [Fact]
    public async Task DefaultSiteAllowsLegacySessionsAndSiteFeaturesAreBounded()
    {
        var catalog = new HelloSiteCatalog(new SkopkaHelloOptions());
        var sites = new HelloSiteContext(catalog, new HttpContextAccessor());
        var admission = new HelloSiteSessionAdmission<Profile>(sites, catalog, []);
        var legacy = new IdentitySessionAdmissionContext<Profile>(User("legacy"), default,
            IdentitySessionAdmissionOperation.Refresh, null, null);
        Assert.True((await admission.CheckAsync(legacy, CancellationToken.None)).IsSuccess);
        var (_, staff, _) = Setup();
        Assert.False(HelloSiteAccess.Allows(staff, HelloSiteFeatures.Registration));
        Assert.False(HelloSiteAccess.Allows(staff, HelloSiteFeatures.PasswordRecovery));
    }
}

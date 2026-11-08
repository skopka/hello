using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Skopka.Abstraction.OperationResult;
using Skopka.Hello.UI;
using Skopka.Hello.UI.Pages;
using Skopka.Identity.AccessLinks;

namespace Skopka.Hello.Tests;

public sealed class AccessLinkPageTests
{
    [Fact]
    public async Task ExpiredEnrollmentRestoresLinkWithoutQrAndAllowsAnotherBegin()
    {
        using var fixture = new Fixture();
        var begin = fixture.CreateModel();
        begin.Token = Fixture.LinkToken;
        Assert.IsType<RedirectToPageResult>(await begin.OnPostBeginAsync(CancellationToken.None));
        Assert.False(string.IsNullOrWhiteSpace(begin.AccessLinkStateToken));
        var current = fixture.CreateModel(begin.AccessLinkStateToken);
        Assert.IsType<PageResult>(await current.OnGetAsync(CancellationToken.None));
        Assert.True(current.Valid);
        Assert.True(current.Enrolling);
        Assert.NotNull(current.QrCode);

        fixture.Clock.Now = fixture.Clock.Now.AddMinutes(11);
        var expired = fixture.CreateModel(begin.AccessLinkStateToken);
        Assert.IsType<PageResult>(await expired.OnGetAsync(CancellationToken.None));
        Assert.True(expired.Valid);
        Assert.False(expired.Enrolling);
        Assert.Empty(expired.EnrollmentTicket);
        Assert.Null(expired.QrCode);
        Assert.Contains(expired.ModelState.Values.SelectMany(value => value.Errors),
            error => error.ErrorMessage == "AccessLink.EnrollmentExpired");
        var restart = fixture.CreateModel(expired.AccessLinkStateToken);
        restart.Token = expired.Token;
        Assert.IsType<RedirectToPageResult>(await restart.OnPostBeginAsync(CancellationToken.None));
        Assert.Equal(2, fixture.Service.BeginCount);
    }

    [Fact]
    public async Task ExpiredEnrollmentIsNotSubmittedToCompletionService()
    {
        using var fixture = new Fixture();
        var begin = fixture.CreateModel();
        begin.Token = Fixture.LinkToken;
        await begin.OnPostBeginAsync(CancellationToken.None);
        var page = fixture.CreateModel(begin.AccessLinkStateToken);
        await page.OnGetAsync(CancellationToken.None);
        fixture.Clock.Now = fixture.Clock.Now.AddMinutes(11);
        var complete = fixture.CreateModel(begin.AccessLinkStateToken);
        complete.Token = Fixture.LinkToken;
        complete.EnrollmentTicket = page.EnrollmentTicket;
        complete.Code = "123456";
        complete.Password = complete.ConfirmPassword = "a test password";
        Assert.IsType<RedirectToPageResult>(await complete.OnPostCompleteAsync(CancellationToken.None));
        Assert.Equal(0, fixture.Service.CompleteCount);
        var next = fixture.CreateModel(complete.AccessLinkStateToken, complete.AccessLinkErrorsToken);
        await next.OnGetAsync(CancellationToken.None);
        Assert.True(next.Valid);
        Assert.False(next.Enrolling);
        Assert.Contains(next.ModelState.Values.SelectMany(value => value.Errors),
            error => error.ErrorMessage == "AccessLink.EnrollmentExpired");
    }

    [Fact]
    public async Task PasswordMismatchRedirectsWithoutPuttingSubmittedSecretsInTempData()
    {
        using var fixture = new Fixture();
        var begin = fixture.CreateModel();
        begin.Token = Fixture.LinkToken;
        await begin.OnPostBeginAsync(CancellationToken.None);
        var page = fixture.CreateModel(begin.AccessLinkStateToken);
        await page.OnGetAsync(CancellationToken.None);
        var complete = fixture.CreateModel(begin.AccessLinkStateToken);
        complete.Token = Fixture.LinkToken;
        complete.EnrollmentTicket = page.EnrollmentTicket;
        complete.Code = "123456";
        complete.Password = "private-password-one";
        complete.ConfirmPassword = "private-password-two";
        Assert.IsType<RedirectToPageResult>(await complete.OnPostCompleteAsync(CancellationToken.None));
        Assert.Equal(0, fixture.Service.CompleteCount);
        Assert.Empty(complete.Password);
        Assert.Empty(complete.ConfirmPassword);
        Assert.Empty(complete.Code);
        Assert.NotNull(complete.AccessLinkStateToken);
        Assert.NotNull(complete.AccessLinkErrorsToken);
        Assert.DoesNotContain("private-password", complete.AccessLinkStateToken, StringComparison.Ordinal);
        Assert.DoesNotContain("123456", complete.AccessLinkStateToken, StringComparison.Ordinal);
        Assert.DoesNotContain("private-password", complete.AccessLinkErrorsToken, StringComparison.Ordinal);
        Assert.DoesNotContain("123456", complete.AccessLinkErrorsToken, StringComparison.Ordinal);
        Assert.DoesNotContain(complete.TempData.Values, value => value is string text
            && (text.Contains("private-password", StringComparison.Ordinal) || text.Contains("123456", StringComparison.Ordinal)));
        var next = fixture.CreateModel(complete.AccessLinkStateToken, complete.AccessLinkErrorsToken);
        await next.OnGetAsync(CancellationToken.None);
        Assert.True(next.Valid);
        Assert.True(next.Enrolling);
        Assert.Contains(next.ModelState.Values.SelectMany(value => value.Errors),
            error => error.ErrorMessage == "AccessLink.PasswordMismatch");
    }

    private sealed class Fixture : IDisposable
    {
        internal const string LinkToken = "link-token-for-test";
        private readonly MemoryCache cache = new(Options.Create(new MemoryCacheOptions()));
        private readonly EphemeralDataProtectionProvider protection = new();
        private readonly ServiceProvider services;
        private readonly HelloUiPrgStateStore state;
        internal MutableTime Clock { get; } = new();
        internal FakeAccessLinks Service { get; }

        internal Fixture()
        {
            state = new HelloUiPrgStateStore(cache);
            Service = new FakeAccessLinks(Clock);
            services = new ServiceCollection().AddSingleton<IHelloUiAccessLinks>(Service).BuildServiceProvider();
        }

        internal AccessLinkModel CreateModel(string? stateToken = null, string? errorsToken = null)
        {
            var http = new DefaultHttpContext { RequestServices = services };
            return new AccessLinkModel(TestHelloUiLocalizer.Instance, new HelloUiRoutePaths("/hello"),
                protection, new SkopkaHelloOptions(), state, Clock)
            {
                PageContext = new PageContext { HttpContext = http },
                TempData = new TempDataDictionary(http, new EmptyTempDataProvider()),
                AccessLinkStateToken = stateToken,
                AccessLinkErrorsToken = errorsToken,
            };
        }

        public void Dispose() { services.Dispose(); cache.Dispose(); }
    }

    private sealed class MutableTime : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeAccessLinks(MutableTime clock) : IHelloUiAccessLinks
    {
        internal int BeginCount { get; private set; }
        internal int CompleteCount { get; private set; }
        public Task<OperationResult<IdentityAccessLinkInfo>> InspectAsync(string token, CancellationToken cancellationToken)
            => Task.FromResult(OperationResultFactory.Success(new IdentityAccessLinkInfo(clock.Now.AddDays(7))));
        public Task<OperationResult<IdentityAccessLinkEnrollment>> BeginAsync(string token, HttpContext context, CancellationToken cancellationToken)
        {
            BeginCount++;
            return Task.FromResult(OperationResultFactory.Success(new IdentityAccessLinkEnrollment(Guid.NewGuid(),
                "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567", clock.Now.AddMinutes(10))));
        }
        public Task<OperationResult<IReadOnlyList<string>>> CompleteAsync(string token, Guid enrollmentId, string code,
            string password, HttpContext context, CancellationToken cancellationToken)
        {
            CompleteCount++;
            return Task.FromResult(OperationResultFactory.Success<IReadOnlyList<string>>(["recovery-code"]));
        }
    }

    private sealed class EmptyTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}

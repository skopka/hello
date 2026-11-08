using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Skopka.Hello.UI;

namespace Skopka.Hello.Tests;

public sealed class HelloUiPostRedirectTests
{
    private static HelloUiPostRedirectSnapshot Snapshot(string binding, string page, int bodySize = 16)
        => new(binding, page, 200, [new("Cache-Control", ["no-store"])], new byte[bodySize]);

    [Theory]
    [InlineData("/Pages/SkopkaHello/Login.cshtml", true)]
    [InlineData("/Pages/SkopkaHello/Account/Security.cshtml", true)]
    [InlineData("/Pages/SkopkaHelloAdmin/Users.cshtml", true)]
    [InlineData("/Pages/Host/Index.cshtml", false)]
    public void EveryHelloPageRedirectsAfterASubmission(string path, bool hello)
    {
        var model = new PageApplicationModel(
            new PageActionDescriptor { RelativePath = path },
            typeof(PageModel).GetTypeInfo(),
            []);

        new HelloUiPageApplicationModelConvention().Apply(model);

        Assert.Equal(hello, model.Filters.OfType<ServiceFilterAttribute>()
            .Any(filter => filter.ServiceType == typeof(HelloUiPostRedirectFilter)));
    }

    [Fact]
    public void RetainedOutcomeBelongsToItsBrowserAndPage()
    {
        using var store = new HelloUiPostRedirectStore();
        var token = store.Store(Snapshot("browser-a", "/hello/login"));

        Assert.NotNull(token);
        Assert.True(store.TryGet(token, "browser-a", "/hello/login", out var replayed));
        Assert.Equal(200, replayed!.StatusCode);
        // Refresh shows the same outcome again.
        Assert.True(store.TryGet(token, "browser-a", "/HELLO/login", out _));
        Assert.False(store.TryGet(token, "browser-b", "/hello/login", out _));
        Assert.False(store.TryGet(token, "browser-a", "/hello/account", out _));
        Assert.False(store.TryGet("unknown", "browser-a", "/hello/login", out _));
    }

    [Fact]
    public void ALaterSubmissionReplacesAndInvalidatesTheEarlierOutcome()
    {
        using var store = new HelloUiPostRedirectStore();
        var first = store.Store(Snapshot("browser", "/hello/login"));
        var second = store.Store(Snapshot("browser", "/hello/login"));

        Assert.False(store.TryGet(first, "browser", "/hello/login", out _));
        Assert.True(store.TryGet(second, "browser", "/hello/login", out _));

        store.Invalidate("browser", "/hello/login");

        Assert.False(store.TryGet(second, "browser", "/hello/login", out _));
    }

    [Fact]
    public void AnOutcomeLargerThanTheBoundIsNotRetained()
    {
        using var store = new HelloUiPostRedirectStore();

        var token = store.Store(Snapshot(
            "browser",
            "/hello/login",
            checked((int)HelloUiPostRedirectStore.SizeLimit) + 1));

        Assert.Null(token);
    }

    [Theory]
    [InlineData("Input.Password", true)]
    [InlineData("Input.ConfirmPassword", true)]
    [InlineData("Phone.Code", true)]
    [InlineData("verificationCode", true)]
    [InlineData("SecondFactorCode", true)]
    [InlineData("BindingCode", true)]
    [InlineData("Input.UserCode", true)]
    [InlineData("Passkey.Signature", true)]
    [InlineData("Input.Login", false)]
    [InlineData("Phone.PhoneNumber", false)]
    [InlineData("DeviceCode", false)]
    [InlineData("Token", false)]
    [InlineData("BindingTicket", false)]
    public void TypedProofsAreNeverRetained(string key, bool secret)
        => Assert.Equal(secret, HelloUiPostRedirectFilter.IsSecret(key));

    [Fact]
    public void RedirectTargetDropsTheHandlerAndKeepsGetState()
    {
        var context = new DefaultHttpContext();
        context.Request.PathBase = "/app";
        context.Request.Path = "/hello/cross-device/waiting";
        context.Request.QueryString = new QueryString("?handler=Complete&ReturnUrl=%2Fcourses&hello-prg=old");

        var target = HelloUiPostRedirectFilter.RedirectTarget(
            context.Request,
            "token",
            [new("DeviceCode", "device-1"), new("ReturnUrl", "/ignored")]);

        Assert.Equal(
            "/app/hello/cross-device/waiting?ReturnUrl=%2Fcourses&DeviceCode=device-1&hello-prg=token",
            target);
        Assert.Equal(
            "/app/hello/cross-device/waiting?ReturnUrl=%2Fcourses",
            HelloUiPostRedirectFilter.CleanUrl(context.Request));
    }

    [Fact]
    public async Task OnlyANavigatingSubmissionReplacesTheRetainedOutcome()
    {
        using var store = new HelloUiPostRedirectStore();
        var filter = new HelloUiPostRedirectFilter(store, new SkopkaHelloUiOptions(), new NoTempData());
        const string cookie = "browser-binding-cookie-value-0123456789";
        // Anonymous binding: the browser cookie and an empty account.
        var binding = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cookie + '\n')));
        var token = store.Store(Snapshot(binding, "/hello/login"));

        // A script asking for a passkey challenge answers with JSON.
        await ExecuteAsync(filter, cookie, new JsonResult(new { challenge = "x" }));
        Assert.True(store.TryGet(token, binding, "/hello/login", out _));

        await ExecuteAsync(filter, cookie, new RedirectToPageResult("/SkopkaHello/Account/Index"));
        Assert.False(store.TryGet(token, binding, "/hello/login", out _));
    }

    [Fact]
    public void GetStateCarriesGetValuesButNoSecretsOrTokens()
    {
        var state = HelloUiPostRedirectFilter.GetState(new SampleModel())
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        Assert.Equal("device-1", state["DeviceCode"]);
        Assert.Equal("/courses", state["returnUrl"]);
        Assert.Equal("true", state["Flag"]);
        Assert.False(state.ContainsKey("Code"));
        Assert.False(state.ContainsKey("Token"));
        Assert.False(state.ContainsKey("Posted"));
        Assert.False(state.ContainsKey("Off"));
    }

    private static Task ExecuteAsync(HelloUiPostRedirectFilter filter, string cookie, IActionResult result)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = HttpMethods.Post;
        http.Request.Path = "/hello/login";
        http.Request.Headers.Cookie = "__Host-Skopka.Hello.UI.Prg=" + cookie;
        var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
        var executing = new ResultExecutingContext(action, [], result, new object());
        return filter.OnResultExecutionAsync(
            executing,
            () => Task.FromResult(new ResultExecutedContext(action, [], result, new object())));
    }

    private sealed class SampleModel
    {
        [BindProperty(SupportsGet = true)]
        public string DeviceCode { get; set; } = "device-1";

        [BindProperty(SupportsGet = true, Name = "returnUrl")]
        public string? ReturnUrl { get; set; } = "/courses";

        [BindProperty(SupportsGet = true)]
        public bool Flag { get; set; } = true;

        [BindProperty(SupportsGet = true)]
        public bool Off { get; set; }

        [BindProperty(SupportsGet = true)]
        public string Code { get; set; } = "123456";

        [BindProperty(SupportsGet = true)]
        public string Token { get; set; } = "reset-token";

        [BindProperty]
        public string Posted { get; set; } = "posted";
    }

    private sealed class NoTempData : ITempDataDictionaryFactory
    {
        public ITempDataDictionary GetTempData(HttpContext context)
            => throw new NotSupportedException();
    }
}

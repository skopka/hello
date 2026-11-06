using System.Security.Cryptography;
using System.Text;
using Microsoft.Playwright;
using Skopka.Hello.IntegrationTests;

namespace Skopka.Hello.BrowserTests;

public sealed class NativeSignInBrowserTests(BrowserFixture browser) : IClassFixture<BrowserFixture>
{
    [Fact]
    public async Task BrowserApprovalCompletesOnlyInOriginalWebView()
    {
        await using var host = await AuthenticationFlowTests.NativeAppBrowserHost.CreateAsync();
        await using var webView = await browser.Browser.NewContextAsync(new() { IgnoreHTTPSErrors = true });
        await using var systemBrowser = await browser.Browser.NewContextAsync(new() { IgnoreHTTPSErrors = true });
        await using var attacker = await browser.Browser.NewContextAsync(new() { IgnoreHTTPSErrors = true });
        var embedded = await webView.NewPageAsync();
        var external = await systemBrowser.NewPageAsync();
        var circuitDiagnostics = new System.Collections.Concurrent.ConcurrentQueue<string>();
        embedded.Response += (_, response) =>
        {
            var path = new Uri(response.Url).AbsolutePath;
            if (path.StartsWith("/_framework/", StringComparison.Ordinal)
                || path.StartsWith("/_blazor", StringComparison.Ordinal))
                circuitDiagnostics.Enqueue($"{response.Request.Method} {path} {response.Status}");
        };
        embedded.Console += (_, message) => circuitDiagnostics.Enqueue(
            $"console: {message.Type} {ClassifyCircuitError(message.Text)}");
        embedded.PageError += (_, message) => circuitDiagnostics.Enqueue(
            $"page error: {ClassifyCircuitError(message)}");
        embedded.RequestFailed += (_, request) =>
        {
            var path = new Uri(request.Url).AbsolutePath;
            circuitDiagnostics.Enqueue($"embedded request failed: {path} {ClassifyCircuitError(request.Failure ?? string.Empty)}");
        };
        external.RequestFailed += (_, request) => circuitDiagnostics.Enqueue(
            $"external request failed: {new Uri(request.Url).AbsolutePath} {ClassifyCircuitError(request.Failure ?? string.Empty)}");
        try
        {
            await embedded.GotoAsync(new Uri(host.Origin, "/hello/native-sign-in").ToString());
            var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
            var state = Base64Url(RandomNumberGenerator.GetBytes(32));
            var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
            var started = await embedded.EvaluateAsync<StartResult>("""
                async ({clientId, callback, state, challenge}) => {
                    const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
                    const form = new URLSearchParams({clientId, callback, state, codeChallenge: challenge, returnUrl: '/native-test-blazor', __RequestVerificationToken: token});
                    const response = await fetch('?handler=Start', {method:'POST', body:form});
                    return {status:response.status, data:response.ok ? await response.json() : null};
                }
                """, new { clientId = host.ClientId, callback = host.CallbackUri, state, challenge });
            Assert.Equal(200, started.Status);
            Assert.NotNull(started.Data);
            var rejected = await embedded.EvaluateAsync<int>("""
                async () => (await fetch('?handler=Status', {method:'POST'})).status
                """);
            Assert.Equal(400, rejected);
            var responses = new System.Collections.Concurrent.ConcurrentQueue<string>();
            external.Response += (_, response) =>
            {
                var uri = new Uri(response.Url);
                responses.Enqueue($"{response.Request.Method} {uri.AbsolutePath} {response.Status}");
            };
            await external.GotoAsync(new Uri(host.Origin, started.Data.LaunchUrl).ToString());
            try
            {
                await Assertions.Expect(external.Locator("[name='Input.Login']")).ToBeVisibleAsync();
            }
            catch (Exception exception) when (exception is PlaywrightException or TimeoutException)
            {
                throw new InvalidOperationException("Native browser responses: " + string.Join(", ", responses), exception);
            }
            Assert.DoesNotContain("launch=", external.Url, StringComparison.Ordinal);
            await external.Locator("[name='Input.Login']").FillAsync("browser-admin@example.test");
            await external.Locator("[name='Input.Password']").FillAsync("correct horse battery staple");
            await external.Locator("form:has([name='Input.Password']) button[type='submit']").ClickAsync();
            await Assertions.Expect(external.Locator("button[name='approve'][value='true']")).ToBeVisibleAsync();
            await Assertions.Expect(external.Locator("body")).ToContainTextAsync("Browser Admin");
            var sourcePage = await systemBrowser.NewPageAsync();
            await sourcePage.GotoAsync(new Uri(host.Origin, "/integration/current-ui-user").ToString());
            var sourceSession = await sourcePage.EvaluateAsync<string>("() => JSON.parse(document.body.innerText).sessionId");
            var attackerPage = await attacker.NewPageAsync();
            await attackerPage.GotoAsync(new Uri(host.Origin, "/hello/native-sign-in").ToString());
            var attackerComplete = await attackerPage.EvaluateAsync<int>("""
                async verifier => { const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
                  return (await fetch('?handler=Complete', {method:'POST', body:new URLSearchParams({codeVerifier:verifier, __RequestVerificationToken:token})})).status; }
                """, verifier);
            Assert.Equal(400, attackerComplete);
            await attackerPage.EvaluateAsync("async () => { await fetch('?handler=Complete'); }");
            var attackerCookies = await attacker.CookiesAsync();
            Assert.DoesNotContain(attackerCookies, cookie => cookie.Name == "__Host-Skopka.Hello.UI");
            // Subscribe before approval: the intermediate response can immediately navigate to the callback.
            var callbackNavigation = external.WaitForURLAsync(
                url => new Uri(url).AbsolutePath == new Uri(host.CallbackUri).AbsolutePath,
                new() { WaitUntil = WaitUntilState.Commit });
            await external.Locator("button[name='approve'][value='true']").ClickAsync();
            await callbackNavigation;
            var fragment = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery("?" + new Uri(external.Url).Fragment.TrimStart('#'));
            Assert.Equal(state, fragment["state"].ToString());
            var receipt = fragment["receipt"].ToString();
            Assert.NotEmpty(receipt);
            Assert.Empty(new Uri(external.Url).Query);
            var stolenReceipt = await attackerPage.EvaluateAsync<int>("""
                async ({verifier, receipt}) => { const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
                  return (await fetch('?handler=Complete', {method:'POST', body:new URLSearchParams({codeVerifier:verifier, completionReceipt:receipt, __RequestVerificationToken:token})})).status; }
                """, new { verifier, receipt });
            Assert.Equal(400, stolenReceipt);
            var withoutReceipt = await embedded.EvaluateAsync<int>("""
                async verifier => { const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
                  return (await fetch('?handler=Complete', {method:'POST', body:new URLSearchParams({codeVerifier:verifier, __RequestVerificationToken:token})})).status; }
                """, verifier);
            Assert.Equal(400, withoutReceipt);
            var wrong = await embedded.EvaluateAsync<int>("""
                async receipt => { const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
                  return (await fetch('?handler=Complete', {method:'POST', body:new URLSearchParams({codeVerifier:'wrong', completionReceipt:receipt, __RequestVerificationToken:token}), redirect:'manual'})).status; }
                """, receipt);
            Assert.Equal(400, wrong);
            await embedded.EvaluateAsync("""
                ({verifier, receipt}) => { const form = document.createElement('form'); form.method='POST'; form.action='?handler=Complete';
                  for (const [name,value] of Object.entries({codeVerifier:verifier, completionReceipt:receipt, __RequestVerificationToken:document.querySelector('input[name="__RequestVerificationToken"]').value})) {
                    const input=document.createElement('input'); input.name=name; input.value=value; form.appendChild(input); }
                  document.body.appendChild(form); form.submit(); }
                """, new { verifier, receipt });
            await embedded.WaitForURLAsync(url => new Uri(url).AbsolutePath == "/native-test-blazor");
            await Assertions.Expect(embedded.Locator("#circuit-user")).ToContainTextAsync("browser-admin");
            try
            {
                await embedded.Locator("#circuit-button").ClickAsync(new() { Timeout = 10000 });
                await Assertions.Expect(embedded.Locator("#circuit-button")).ToContainTextAsync("1");
            }
            catch (Exception exception) when (exception is PlaywrightException or TimeoutException)
            {
                var stateOfBlazor = await embedded.EvaluateAsync<string>("() => typeof window.Blazor + ':' + typeof window.Blazor?.start");
                throw new InvalidOperationException($"Circuit diagnostics: {stateOfBlazor}; {string.Join(", ", circuitDiagnostics.Take(30))}", exception);
            }
            var embeddedSession = await embedded.EvaluateAsync<string>("async () => (await (await fetch('/integration/current-ui-user')).json()).sessionId");
            Assert.NotEqual(sourceSession, embeddedSession);
            await external.GotoAsync(new Uri(host.Origin, "/integration/current-ui-user").ToString());
            await Assertions.Expect(external.Locator("body")).ToContainTextAsync("Browser Admin");
            var sourceAfter = await external.EvaluateAsync<string>("async () => (await (await fetch('/integration/current-ui-user')).json()).sessionId");
            Assert.Equal(sourceSession, sourceAfter);
        }
        catch (Exception exception) when (exception is PlaywrightException or TimeoutException)
        {
            throw new InvalidOperationException(
                $"Native browser diagnostics: {string.Join(", ", circuitDiagnostics.Take(60))}", exception);
        }
    }

    // Classify browser diagnostics without recording URLs, tokens, or exception messages.
    private static string ClassifyCircuitError(string message)
    {
        var networkError = System.Text.RegularExpressions.Regex.Match(
            message, @"\bERR_[A-Z0-9_]{2,60}\b", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        if (networkError.Success) return networkError.Value;
        foreach (var type in new[] { "TypeError", "ReferenceError", "SyntaxError", "SecurityError", "AbortError" })
        {
            if (message.Contains(type, StringComparison.Ordinal)) return type;
        }
        if (message.Contains("certificate", StringComparison.OrdinalIgnoreCase)
            || message.Contains("ERR_CERT", StringComparison.OrdinalIgnoreCase)) return "certificate";
        if (message.Contains("WebSocket", StringComparison.OrdinalIgnoreCase)) return "websocket";
        if (message.Contains("circuit", StringComparison.OrdinalIgnoreCase)) return "circuit";
        if (message.Contains("SignalR", StringComparison.OrdinalIgnoreCase)) return "signalr";
        if (message.Contains("connection", StringComparison.OrdinalIgnoreCase)) return "connection";
        return "other";
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private sealed class StartResult
    {
        public int Status { get; set; }
        public StartData? Data { get; set; }
    }
    private sealed class StartData
    {
        public string LaunchUrl { get; set; } = string.Empty;
    }
}

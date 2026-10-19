using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Xunit;

namespace Skopka.Hello.BrowserTests;

public sealed class PhoneSqliteBlazorBrowserTests(BrowserFixture browser)
    : IClassFixture<BrowserFixture>
{
    [Fact]
    public async Task NewPhoneRegistersInRazorAndAuthenticatesStaticBlazorPage()
    {
        var root = FindRepositoryRoot();
        var project = Path.Combine(root, "samples",
            "Skopka.Hello.PhoneSqliteBlazor",
            "Skopka.Hello.PhoneSqliteBlazor.csproj");
        var database = Path.Combine(Path.GetTempPath(),
            $"skopka-phone-browser-{Guid.NewGuid():N}.db");
        var address = $"http://127.0.0.1:{GetFreePort()}";
        var code = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };
        foreach (var argument in new[]
        {
            "run", "--no-build", "--no-restore", "--no-launch-profile",
            "-c", "Release", "--project", project,
        })
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.StartInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        process.StartInfo.Environment["ASPNETCORE_URLS"] = address;
        process.StartInfo.Environment["ConnectionStrings__Identity"] =
            $"Data Source={database}";
        process.StartInfo.Environment["Logging__LogLevel__Default"] = "Warning";
        process.OutputDataReceived += (_, eventArgs) => CaptureCode(eventArgs.Data);
        process.ErrorDataReceived += (_, eventArgs) => CaptureCode(eventArgs.Data);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            using var client = new HttpClient();
            await WaitForHostAsync(client, address, process);
            using var invalidTicket = await client.PostAsJsonAsync(
                $"{address}/auth/phone/register",
                new
                {
                    registrationToken = "not-a-ticket",
                    profile = new { displayName = "Invalid", locale = "ru" },
                    acceptTermsOfService = false,
                    acceptPrivacyPolicy = false,
                });
            Assert.Equal(HttpStatusCode.Unauthorized,
                invalidTicket.StatusCode);
            await using var context = await browser.Browser.NewContextAsync();
            var page = await context.NewPageAsync();
            await page.GotoAsync($"{address}/hello/login");
            Assert.Equal(0, await page.Locator(
                "input[name='Input.Password']").CountAsync());
            await page.Locator(
                "input[name='Phone.PhoneNumber'][type='tel']")
                .FillAsync("+79991234571");
            await page.Locator("button[data-phone-resend]").ClickAsync();
            var deliveredCode = await code.Task.WaitAsync(
                TimeSpan.FromSeconds(20));
            await page.Locator("input[name='Phone.Code']")
                .FillAsync(deliveredCode);
            await page.Locator(
                "form:has(input[name='Phone.Code']) button[type='submit']")
                .ClickAsync();
            await page.Locator("input[name='Phone.DisplayName']")
                .FillAsync("Browser Guest");
            await page.Locator(
                "form:has(input[name='Phone.DisplayName']) button[type='submit']")
                .ClickAsync();
            await page.WaitForURLAsync("**/hello/account");
            await page.GotoAsync($"{address}/private");
            Assert.Equal($"{address}/private", page.Url);
            Assert.Contains("Закрытая страница",
                await page.Locator("body").InnerTextAsync());
            Assert.Contains("Browser Guest",
                await page.Locator("body").InnerTextAsync());
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }

            foreach (var path in new[] { database, database + "-wal", database + "-shm" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        void CaptureCode(string? line)
        {
            if (line is null || !line.Contains("Development SMS:",
                    StringComparison.Ordinal))
            {
                return;
            }

            var match = Regex.Match(line, @"(?<!\d)\d{6}(?!\d)");
            if (match.Success)
            {
                code.TrySetResult(match.Value);
            }
        }
    }

    private static async Task WaitForHostAsync(
        HttpClient client,
        string address,
        Process process)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (process.HasExited)
            {
                throw new InvalidOperationException(
                    "The phone sample exited before it became ready.");
            }

            try
            {
                using var response = await client.GetAsync(address);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Kestrel is still starting.
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("The phone sample did not become ready.");
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName,
                    "Skopka.Hello.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "The Hello repository root was not found.");
    }
}

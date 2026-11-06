using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Skopka.Abstraction.OperationResult;
using Skopka.Hello.Admin;
using Skopka.Hello.Endpoints;
using Testcontainers.PostgreSql;

namespace Skopka.Hello.IntegrationTests;

public sealed partial class AuthenticationFlowTests
{
    [Fact]
    public async Task AdministratorResetsExactAuthenticatorAfterStepUpAndInvalidatesTargetSession()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await postgres.StartAsync();
        var audit = new ResetAuditSink();
        await using var app = await TestApplication.CreateAsync(postgres.GetConnectionString(),
            configureHello: options => options.Totp.Enabled = true,
            configureServices: services =>
            {
                services.RemoveAll<IHelloSecurityEventSink>();
                services.AddSingleton<IHelloSecurityEventSink>(audit);
            });
        using var client = app.CreateClient();
        const string password = "correct horse battery staple";
        const string adminEmail = "reset-admin@example.test";
        const string targetEmail = "reset-target@example.test";
        var adminId = await RegisterUiAccountAsync(client, "reset-admin", adminEmail, "Admin");
        var targetId = await RegisterUiAccountAsync(client, "reset-target", targetEmail, "Target");
        using var request = await client.PostAsJsonAsync("/auth/email-confirmation/request", new { email = adminEmail });
        Assert.Equal(HttpStatusCode.Accepted, request.StatusCode);
        var message = await app.WaitForMessageAsync(HelloAccountMessageKind.EmailConfirmation);
        var query = QueryHelpers.ParseQuery(Assert.IsType<Uri>(message.ActionUrl).Query);
        using var confirmEmail = await client.PostAsJsonAsync("/auth/email-confirmation/confirm", new
        {
            userId = Guid.Parse(query["userId"].Single()!),
            email = query["email"].Single(),
            token = query["token"].Single(),
        });
        Assert.Equal(HttpStatusCode.NoContent, confirmEmail.StatusCode);
        var target = await LoginAsync(client, targetEmail, password);
        using var enrollmentResponse = await SendAuthorizedJsonAsync(client, HttpMethod.Post,
            "/account/authenticator/enrollment", target.AccessToken, new { password });
        var enrollment = await enrollmentResponse.Content.ReadFromJsonAsync<TotpEnrollmentResponse>();
        Assert.NotNull(enrollment);
        using var confirmed = await SendAuthorizedJsonAsync(client, HttpMethod.Post,
            "/account/authenticator/enrollment/confirm", target.AccessToken,
            new { enrollmentId = enrollment.EnrollmentId, enrollmentTicket = enrollment.EnrollmentTicket, code = ComputeTotp(enrollment.Secret) });
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        await app.GrantAdministratorAsync(adminId);
        var admin = await LoginAsync(client, adminEmail, password);
        using var list = new HttpRequestMessage(HttpMethod.Get, "/admin/users?search=reset-target");
        list.Headers.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);
        using var listed = await client.SendAsync(list);
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        using var users = JsonDocument.Parse(await listed.Content.ReadAsStringAsync());
        var version = Assert.Single(users.RootElement.GetProperty("items").EnumerateArray()).GetProperty("version").GetInt64();
        var path = $"/admin/users/{targetId:D}/actions/reset-authenticator";
        using var unauthorized = await SendAuthorizedJsonAsync(client, HttpMethod.Post,
            path + "/challenge", target.AccessToken, new { expectedVersion = version });
        Assert.Equal(HttpStatusCode.Forbidden, unauthorized.StatusCode);
        using var begin = await SendAuthorizedJsonAsync(client, HttpMethod.Post,
            path + "/challenge", admin.AccessToken, new { expectedVersion = version });
        Assert.Equal(HttpStatusCode.OK, begin.StatusCode);
        using var challenge = JsonDocument.Parse(await begin.Content.ReadAsStringAsync());
        var challengeId = challenge.RootElement.GetProperty("challengeId").GetGuid();
        var otp = app.Messages.Last(item => item.Kind == HelloAccountMessageKind.AdminActionVerification).VerificationCode;
        using var reset = await SendAuthorizedJsonAsync(client, HttpMethod.Post, path, admin.AccessToken,
            new { expectedVersion = version, challengeId, verificationCode = otp });
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        var auditEvent = Assert.Single(audit.Events,
            item => item.EventType == HelloAdminSecurityEventTypes.AuthenticatorReset);
        Assert.Equal(adminId, auditEvent.ActorUserId);
        Assert.Equal(targetId, auditEvent.SubjectUserId);
        Assert.Empty(auditEvent.Metadata);
        using var oldSession = new HttpRequestMessage(HttpMethod.Get, "/account/me");
        oldSession.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.AccessToken);
        using var denied = await client.SendAsync(oldSession);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using var replay = await SendAuthorizedJsonAsync(client, HttpMethod.Post, path, admin.AccessToken,
            new { expectedVersion = version, challengeId, verificationCode = otp });
        Assert.False(replay.IsSuccessStatusCode);
    }

    private sealed class ResetAuditSink : IHelloSecurityEventSink
    {
        private readonly List<HelloSecurityEventEnvelope> events = [];

        public IReadOnlyList<HelloSecurityEventEnvelope> Events
        {
            get
            {
                lock (events)
                {
                    return events.ToArray();
                }
            }
        }

        public OperationResult Write(HelloSecurityEventEnvelope securityEvent)
        {
            lock (events)
            {
                events.Add(securityEvent);
            }

            return OperationResultFactory.Success();
        }
    }
}

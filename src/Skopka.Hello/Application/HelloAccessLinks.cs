using Skopka.Abstraction.OperationResult;
using Skopka.Identity.AccessLinks;

namespace Skopka.Hello;

/// <summary>Trusted host service for issuing site-bound account access links.</summary>
public interface IHelloAccessLinks<TProfile>
{
    /// <summary>Issues a one-use URL for an allowlisted site; the trusted host authorizes and delivers it.</summary>
    Task<OperationResult<HelloIssuedAccessLink>> IssueAsync(Guid userId, long expectedVersion, string siteId, bool invalidateExisting, CancellationToken cancellationToken);
    /// <summary>Inspects a link against the current site without consuming it.</summary>
    Task<OperationResult<IdentityAccessLinkInfo>> InspectAsync(string token, CancellationToken cancellationToken);
    /// <summary>Starts authenticator enrollment for a valid link.</summary>
    Task<OperationResult<IdentityAccessLinkEnrollment>> BeginAsync(string token, string? clientKey, CancellationToken cancellationToken);
    /// <summary>Atomically sets the password and authenticator and consumes the link.</summary>
    Task<OperationResult<HelloCompletedAccessLink<TProfile>>> CompleteAsync(string token, Guid enrollmentId, string code, string newPassword, string? clientKey, CancellationToken cancellationToken);
}

/// <summary>Safe account projection and recovery codes shown once after link consumption.</summary>
public sealed record HelloCompletedAccessLink<TProfile>(HelloAccount<TProfile> Account, IReadOnlyList<string> RecoveryCodes)
{
    public override string ToString() => $"{nameof(HelloCompletedAccessLink<TProfile>)} {{ RecoveryCodes = [redacted] }}";
}

/// <summary>A secret one-use URL for delivery by the trusted host.</summary>
public sealed record HelloIssuedAccessLink(Uri Url, DateTimeOffset ExpiresAt)
{
    public override string ToString() => $"{nameof(HelloIssuedAccessLink)} {{ Url = [redacted], ExpiresAt = {ExpiresAt} }}";
}

internal sealed class HelloAccessLinks<TProfile>(IIdentityAccessLinkService<TProfile> identity,
    IHelloSiteContext sites, HelloUiRoutePaths routes) : IHelloAccessLinks<TProfile>
{
    private static Error Unavailable() => new("hello.access_link.unavailable", "Access link is unavailable.", ErrorType.Forbidden);
    public async Task<OperationResult<HelloIssuedAccessLink>> IssueAsync(Guid userId, long expectedVersion, string siteId, bool invalidateExisting, CancellationToken cancellationToken)
    {
        var site = sites.Find(siteId);
        if (site?.PublicOrigin is null || !site.IsEnabled(HelloSiteFeatures.Login) || !site.PasswordSignInEnabled)
            return OperationResultFactory.Fail<HelloIssuedAccessLink>(Unavailable());
        var issued = await identity.IssueAsync(new(userId, expectedVersion, site.Id, invalidateExisting), cancellationToken);
        if (!issued.IsSuccess) return OperationResultFactory.Fail<HelloIssuedAccessLink>(issued.Errors);
        var url = new Uri(site.PublicOrigin, routes.PathPrefix + "/access-link");
        return OperationResultFactory.Success(new HelloIssuedAccessLink(new Uri(url + "#token=" + Uri.EscapeDataString(issued.Value.Token)), issued.Value.ExpiresAt));
    }
    public Task<OperationResult<IdentityAccessLinkInfo>> InspectAsync(string token, CancellationToken cancellationToken)
        => sites.Current is { } site ? identity.InspectAsync(token, site.Id, cancellationToken) : Task.FromResult(OperationResultFactory.Fail<IdentityAccessLinkInfo>(Unavailable()));
    public Task<OperationResult<IdentityAccessLinkEnrollment>> BeginAsync(string token, string? clientKey, CancellationToken cancellationToken)
        => sites.Current is { } site && site.PasswordSignInEnabled && site.IsEnabled(HelloSiteFeatures.Login)
            ? identity.BeginEnrollmentAsync(new(token, site.Id, clientKey), cancellationToken)
            : Task.FromResult(OperationResultFactory.Fail<IdentityAccessLinkEnrollment>(Unavailable()));
    public async Task<OperationResult<HelloCompletedAccessLink<TProfile>>> CompleteAsync(string token, Guid enrollmentId, string code, string newPassword, string? clientKey, CancellationToken cancellationToken)
    {
        if (sites.Current is not { } site || !site.PasswordSignInEnabled || !site.IsEnabled(HelloSiteFeatures.Login))
            return OperationResultFactory.Fail<HelloCompletedAccessLink<TProfile>>(Unavailable());
        var result = await identity.CompleteAsync(new(token, site.Id, enrollmentId, code, newPassword, clientKey), cancellationToken);
        return result.IsSuccess
            ? OperationResultFactory.Success(new HelloCompletedAccessLink<TProfile>(HelloIdentityApplication<TProfile>.ToAccount(result.Value.User), result.Value.RecoveryCodes))
            : OperationResultFactory.Fail<HelloCompletedAccessLink<TProfile>>(result.Errors);
    }
}

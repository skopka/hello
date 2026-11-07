using Microsoft.AspNetCore.Http;
using Skopka.Abstraction.OperationResult;
using Skopka.Identity.AccessLinks;

namespace Skopka.Hello.UI;

internal interface IHelloUiAccessLinks
{
    Task<OperationResult<IdentityAccessLinkInfo>> InspectAsync(string token, CancellationToken cancellationToken);
    Task<OperationResult<IdentityAccessLinkEnrollment>> BeginAsync(string token, HttpContext context, CancellationToken cancellationToken);
    Task<OperationResult<IReadOnlyList<string>>> CompleteAsync(string token, Guid enrollmentId, string code, string password, HttpContext context, CancellationToken cancellationToken);
}

internal sealed class HelloUiAccessLinks<TProfile>(IHelloAccessLinks<TProfile> links, IHelloRequestContext requestContext) : IHelloUiAccessLinks
{
    public Task<OperationResult<IdentityAccessLinkInfo>> InspectAsync(string token, CancellationToken cancellationToken) => links.InspectAsync(token, cancellationToken);
    public Task<OperationResult<IdentityAccessLinkEnrollment>> BeginAsync(string token, HttpContext context, CancellationToken cancellationToken)
        => links.BeginAsync(token, requestContext.CreateClientKey(context), cancellationToken);
    public async Task<OperationResult<IReadOnlyList<string>>> CompleteAsync(string token, Guid enrollmentId, string code, string password, HttpContext context, CancellationToken cancellationToken)
    {
        var result = await links.CompleteAsync(token, enrollmentId, code, password, requestContext.CreateClientKey(context), cancellationToken);
        return result.IsSuccess ? OperationResultFactory.Success(result.Value.RecoveryCodes)
            : OperationResultFactory.Fail<IReadOnlyList<string>>(result.Errors);
    }
}

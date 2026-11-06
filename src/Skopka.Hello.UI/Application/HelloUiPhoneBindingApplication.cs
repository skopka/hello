using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Skopka.Abstraction.OperationResult;
using Skopka.Identity.Errors;

namespace Skopka.Hello.UI;

public interface IHelloUiPhoneBindingApplication
{
    Task<OperationResult<HelloPhoneBindingChallenge>> BeginAsync(string phone, HttpContext context, CancellationToken ct);
    Task<OperationResult> CompleteAsync(string ticket, string code, HttpContext context, CancellationToken ct);
}

internal sealed class HelloUiPhoneBindingApplication<TProfile>(
    IHelloPhoneBindingApplication<TProfile> application, IHelloRequestContext requestContext)
    : IHelloUiPhoneBindingApplication
{
    private static async Task<string?> Token(HttpContext context)
        => (await context.AuthenticateAsync(HelloUiDefaults.AuthenticationScheme))
            .Properties?.GetTokenValue(HelloUiDefaults.AccessTokenName);
    private static Error Invalid() => new(IdentityErrorCodes.RefreshTokenInvalid,
        "The session is invalid or expired.", ErrorType.Unauthorized);
    public async Task<OperationResult<HelloPhoneBindingChallenge>> BeginAsync(string phone, HttpContext context, CancellationToken ct)
    {
        var token = await Token(context);
        return token is null ? OperationResultFactory.Fail<HelloPhoneBindingChallenge>(Invalid())
            : await application.BeginAsync(token, phone, requestContext.CreateClientKey(context), ct);
    }
    public async Task<OperationResult> CompleteAsync(string ticket, string code, HttpContext context, CancellationToken ct)
    {
        var token = await Token(context);
        if (token is null) return OperationResultFactory.Fail(Invalid());
        var result = await application.CompleteAsync(token, ticket, code, requestContext.CreateClientKey(context), ct);
        return result.IsSuccess ? OperationResultFactory.Success() : OperationResultFactory.Fail(result.Errors);
    }
}

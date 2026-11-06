using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Skopka.Abstraction.OperationResult;

namespace Skopka.Hello.Endpoints;

public sealed record SecondFactorTicketRequest(string Ticket);
public sealed record SecondFactorCodeRequest(string Ticket, string Code);

internal static class HelloSecondFactorEndpoints
{
    public static void MapHelloSecondFactor<TProfile>(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/auth/second-factor/inspect", InspectAsync<TProfile>).AllowAnonymous();
        endpoints.MapPost("/auth/second-factor/verification", BeginVerificationAsync<TProfile>).AllowAnonymous();
        endpoints.MapPost("/auth/second-factor/verification/complete", CompleteVerificationAsync<TProfile>).AllowAnonymous();
        endpoints.MapPost("/auth/second-factor/enrollment", BeginEnrollmentAsync<TProfile>).AllowAnonymous();
        endpoints.MapPost("/auth/second-factor/enrollment/confirm", ConfirmEnrollmentAsync<TProfile>).AllowAnonymous();
        endpoints.MapPost("/auth/second-factor/enrollment/acknowledge", AcknowledgeAsync<TProfile>).AllowAnonymous();
    }

    private static void Sensitive(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
    }

    private static async Task<IResult> InspectAsync<TProfile>(SecondFactorTicketRequest request,
        IHelloSecondFactorApplication<TProfile> app, HttpContext context, CancellationToken ct)
    {
        Sensitive(context);
        var result = await app.InspectAsync(request.Ticket, ct);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : OperationResultProblemMapper.ToResult(result, context);
    }

    private static async Task<IResult> BeginVerificationAsync<TProfile>(SecondFactorTicketRequest request,
        IHelloSecondFactorApplication<TProfile> app, IHelloRequestContext client, HttpContext context, CancellationToken ct)
    {
        Sensitive(context);
        var result = await app.BeginVerificationAsync(request.Ticket, client.CreateClientKey(context), ct);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : OperationResultProblemMapper.ToResult(result, context);
    }

    private static async Task<IResult> BeginEnrollmentAsync<TProfile>(SecondFactorTicketRequest request,
        IHelloSecondFactorApplication<TProfile> app, IHelloRequestContext client, HttpContext context, CancellationToken ct)
    {
        Sensitive(context);
        var result = await app.BeginEnrollmentAsync(request.Ticket, client.CreateClientKey(context), ct);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : OperationResultProblemMapper.ToResult(result, context);
    }

    private static async Task<IResult> ConfirmEnrollmentAsync<TProfile>(SecondFactorCodeRequest request,
        IHelloSecondFactorApplication<TProfile> app, IHelloRequestContext client, HttpContext context, CancellationToken ct)
    {
        Sensitive(context);
        var result = await app.ConfirmEnrollmentAsync(request.Ticket, request.Code, client.CreateClientKey(context), ct);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : OperationResultProblemMapper.ToResult(result, context);
    }

    private static async Task<IResult> CompleteVerificationAsync<TProfile>(SecondFactorCodeRequest request,
        IHelloSecondFactorApplication<TProfile> app, IHelloRequestContext client, IHelloSessionCookieManager cookies,
        HttpContext context, CancellationToken ct)
    {
        Sensitive(context);
        var transport = cookies.ValidateTransport(context);
        if (!transport.IsSuccess) return OperationResultProblemMapper.ToResult(transport, context);
        return Complete(await app.CompleteVerificationAsync(request.Ticket, request.Code, client.CreateClientKey(context), ct), cookies, context);
    }

    private static async Task<IResult> AcknowledgeAsync<TProfile>(SecondFactorTicketRequest request,
        IHelloSecondFactorApplication<TProfile> app, IHelloSessionCookieManager cookies, HttpContext context, CancellationToken ct)
    {
        Sensitive(context);
        var transport = cookies.ValidateTransport(context);
        if (!transport.IsSuccess) return OperationResultProblemMapper.ToResult(transport, context);
        return Complete(await app.AcknowledgeEnrollmentAsync(request.Ticket, ct), cookies, context);
    }

    private static IResult Complete<TProfile>(OperationResult<HelloSignIn<TProfile>> result,
        IHelloSessionCookieManager cookies, HttpContext context)
    {
        if (!result.IsSuccess) return OperationResultProblemMapper.ToResult(result, context);
        var session = result.Value.Session;
        cookies.WriteSessionCookies(context, session);
        return TypedResults.Ok(new SessionResponse(session.SessionId, session.AccessToken,
            session.AccessTokenExpiresAt, session.RefreshTokenExpiresAt));
    }
}

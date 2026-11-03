using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Skopka.Hello.AuthorizationServer;
using Skopka.Hello.Endpoints;

namespace Skopka.Hello.Admin;

public static partial class SkopkaHelloAdminEndpointRouteBuilderExtensions
{
    private static void MapClientEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/clients", ListClientsAsync);
        group.MapGet("/clients/catalog", ClientCatalogAsync);
        group.MapPost("/clients/actions/challenge", BeginClientAsync);
        group.MapPost("/clients/actions", CompleteClientAsync);
    }

    private static async Task<IResult> ListClientsAsync(
        int? offset,
        int? count,
        IHelloAdminClientApplication app,
        IAuthorizationService auth,
        SkopkaHelloAdminOptions options,
        HttpContext http,
        CancellationToken ct)
    {
        ApplySensitiveResponseHeaders(http);
        if (!await IsAuthorizedAsync(auth, http, options.ReadPolicyName))
        {
            return Results.Forbid();
        }
        var token = ReadBearerToken(http);
        if (token is null)
        {
            return InvalidSession(http);
        }
        var result = await app.ListAsync(token, offset ?? 0, count ?? 50, ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : OperationResultProblemMapper.ToResult(result, http);
    }

    private static async Task<IResult> ClientCatalogAsync(
        IHelloAuthorizationClientManagement clients,
        IHelloAdminClientApplication app,
        IAuthorizationService auth,
        SkopkaHelloAdminOptions options,
        HttpContext http,
        CancellationToken ct)
    {
        ApplySensitiveResponseHeaders(http);
        if (!await IsAuthorizedAsync(auth, http, options.ReadPolicyName))
        {
            return Results.Forbid();
        }
        var token = ReadBearerToken(http);
        if (token is null)
        {
            return InvalidSession(http);
        }
        var actor = await app.ListAsync(token, 0, 1, ct);
        return actor.IsSuccess
            ? Results.Ok(clients.GetCatalog())
            : OperationResultProblemMapper.ToResult(actor, http);
    }

    private static async Task<IResult> BeginClientAsync(
        HelloAdminClientAction request,
        IHelloAdminClientApplication app,
        IHelloRequestContext context,
        IAuthorizationService auth,
        SkopkaHelloAdminOptions options,
        HttpContext http,
        CancellationToken ct)
    {
        ApplySensitiveResponseHeaders(http);
        if (!await IsAuthorizedAsync(auth, http, options.DeletePolicyName))
        {
            return Results.Forbid();
        }
        var token = ReadBearerToken(http);
        if (token is null)
        {
            return InvalidSession(http);
        }
        var result = await app.BeginAsync(token, request, context.CreateClientKey(http), ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : OperationResultProblemMapper.ToResult(result, http);
    }

    private static async Task<IResult> CompleteClientAsync(
        HelloAdminClientCompletion request,
        IHelloAdminClientApplication app,
        IHelloRequestContext context,
        IAuthorizationService auth,
        SkopkaHelloAdminOptions options,
        HttpContext http,
        CancellationToken ct)
    {
        ApplySensitiveResponseHeaders(http);
        if (!await IsAuthorizedAsync(auth, http, options.DeletePolicyName))
        {
            return Results.Forbid();
        }
        var token = ReadBearerToken(http);
        if (token is null)
        {
            return InvalidSession(http);
        }
        var result = await app.CompleteAsync(token, request, context.CreateClientKey(http), ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : OperationResultProblemMapper.ToResult(result, http);
    }
}

using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Skopka.Hello;

namespace Microsoft.AspNetCore.Builder;

public static class SkopkaHelloUiApplicationBuilderExtensions
{
    public static IApplicationBuilder UseSkopkaHelloUiErrorPages(
        this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var routes = app.ApplicationServices
            .GetRequiredService<HelloUiRoutePaths>();
        var uiOptions = app.ApplicationServices
            .GetRequiredService<Skopka.Hello.UI.SkopkaHelloUiOptions>();

        app.UseWhen(
            Skopka.Hello.UI.HelloUiRequestNegotiation.PrefersHtml,
            branch =>
            {
                branch.UseExceptionHandler(
                    errorBranch => errorBranch.Run(
                        context => WriteErrorPageAsync(
                            context,
                            SelectStatusCode(
                                context.Features
                                    .Get<IExceptionHandlerFeature>()
                                    ?.Error),
                            routes,
                            uiOptions)));
                branch.Use(
                    (context, next) => WriteHtmlStatusCodePageAsync(
                        context,
                        next,
                        routes,
                        uiOptions));
            });
        app.UseWhen(
            context => !Skopka.Hello.UI.HelloUiRequestNegotiation
                .PrefersHtml(context),
            branch =>
            {
                branch.UseExceptionHandler();
                branch.Use(WriteProblemDetailsStatusCodePageAsync);
            });

        return app;
    }

    private static async Task WriteHtmlStatusCodePageAsync(
        HttpContext context,
        RequestDelegate next,
        HelloUiRoutePaths routes,
        Skopka.Hello.UI.SkopkaHelloUiOptions uiOptions)
    {
        await next(context);
        if (!CanWriteStatusCodePage(context))
        {
            return;
        }

        await WriteErrorPageAsync(
            context,
            context.Response.StatusCode,
            routes,
            uiOptions);
    }

    private static async Task WriteProblemDetailsStatusCodePageAsync(
        HttpContext context,
        RequestDelegate next)
    {
        await next(context);
        if (!CanWriteStatusCodePage(context))
        {
            return;
        }

        var statusCode = context.Response.StatusCode;
        context.Response.Clear();
        await Results.Problem(
                statusCode: statusCode,
                instance: context.Request.Path)
            .ExecuteAsync(context);
    }

    private static bool CanWriteStatusCodePage(HttpContext context)
    {
        var response = context.Response;
        var statusCodePages = context.Features
            .Get<IStatusCodePagesFeature>();
        return !response.HasStarted
            && response.StatusCode is >= 400 and < 600
            && String.IsNullOrEmpty(response.ContentType)
            && response.ContentLength is null or 0
            && statusCodePages is not { Enabled: false };
    }

    private static async Task WriteErrorPageAsync(
        HttpContext context,
        int statusCode,
        HelloUiRoutePaths routes,
        Skopka.Hello.UI.SkopkaHelloUiOptions uiOptions)
    {
        statusCode = statusCode is >= 400 and <= 599
            ? statusCode
            : StatusCodes.Status500InternalServerError;
        var selectedCulture = await context.RequestServices
            .GetRequiredService<
                Skopka.Hello.UI.HelloUiRequestCultureFilter>()
            .ResolveCultureAsync(context);
        var russian = selectedCulture.Name.StartsWith(
            "ru",
            StringComparison.OrdinalIgnoreCase);
        var (title, detail) = GetLocalizedContent(statusCode, russian);
        var eyebrow = russian ? "Ошибка запроса" : "Request error";
        var returnText = russian
            ? "Вернуться к аккаунту"
            : "Return to the account";
        var requestIdLabel = russian
            ? "Идентификатор запроса:"
            : "Request ID:";
        var encoder = HtmlEncoder.Default;
        var requestId = Activity.Current?.Id
            ?? context.TraceIdentifier;

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store, max-age=0";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";

        var html = new StringBuilder();
        html.Append("<!doctype html><html lang=\"")
            .Append(russian ? "ru" : "en")
            .Append("\"><head><meta charset=\"utf-8\">")
            .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">")
            .Append("<title>")
            .Append(encoder.Encode(title))
            .Append(" · Skopka.Hello</title>");
        if (uiOptions.BuiltInStylesEnabled)
        {
            html.Append("<link rel=\"stylesheet\" href=\"")
                .Append(Skopka.Hello.UI.HelloUiDefaults
                    .BuiltInStylesheetPath)
                .Append("\">");
        }

        if (uiOptions.CustomCssFilePath is not null)
        {
            html.Append("<link rel=\"stylesheet\" href=\"")
                .Append(encoder.Encode(uiOptions.CustomCssRequestPath))
                .Append("\">");
        }

        html.Append("</head><body><header class=\"hello-header\">")
            .Append("<a class=\"hello-brand\" href=\"")
            .Append(encoder.Encode(routes.RootPath))
            .Append("\">Skopka.Hello</a></header>")
            .Append("<main class=\"hello-main\"><section class=\"hello-card hello-auth-card\">")
            .Append("<p class=\"hello-eyebrow\">")
            .Append(encoder.Encode(eyebrow))
            .Append(" · ")
            .Append(statusCode)
            .Append("</p><h1>")
            .Append(encoder.Encode(title))
            .Append("</h1><p class=\"hello-muted\">")
            .Append(encoder.Encode(detail))
            .Append("</p><p class=\"hello-muted\">")
            .Append(encoder.Encode(requestIdLabel))
            .Append(" <code>")
            .Append(encoder.Encode(requestId))
            .Append("</code></p><p class=\"hello-card-footer\">")
            .Append("<a href=\"")
            .Append(encoder.Encode(routes.RootPath))
            .Append("\">")
            .Append(encoder.Encode(returnText))
            .Append("</a></p></section></main></body></html>");

        await context.Response.WriteAsync(html.ToString());
    }

    private static int SelectStatusCode(Exception? exception)
        => exception is BadHttpRequestException
                or Microsoft.AspNetCore.Antiforgery
                    .AntiforgeryValidationException
            ? StatusCodes.Status400BadRequest
            : StatusCodes.Status500InternalServerError;

    private static (string Title, string Detail) GetLocalizedContent(
        int statusCode,
        bool russian)
        => (statusCode, russian) switch
        {
            (StatusCodes.Status400BadRequest, true) =>
                ("Не удалось обработать запрос",
                    "Проверьте введённые данные и повторите попытку."),
            (StatusCodes.Status401Unauthorized, true) =>
                ("Требуется вход",
                    "Войдите в систему и повторите действие."),
            (StatusCodes.Status403Forbidden, true) =>
                ("Недостаточно прав",
                    "У вашей учётной записи нет прав для этого действия."),
            (StatusCodes.Status404NotFound, true) =>
                ("Страница не найдена",
                    "Возможно, адрес указан неверно или страница больше недоступна."),
            (StatusCodes.Status409Conflict, true) =>
                ("Данные уже изменились",
                    "Обновите страницу и повторите действие."),
            (StatusCodes.Status429TooManyRequests, true) =>
                ("Слишком много запросов",
                    "Немного подождите и повторите попытку."),
            (_, true) =>
                ("Что-то пошло не так",
                    "Не удалось завершить запрос. Попробуйте ещё раз позднее."),
            (StatusCodes.Status400BadRequest, false) =>
                ("The request could not be processed",
                    "Check the entered data and try again."),
            (StatusCodes.Status401Unauthorized, false) =>
                ("Sign-in required",
                    "Sign in and repeat the action."),
            (StatusCodes.Status403Forbidden, false) =>
                ("Access denied",
                    "Your account does not have permission to perform this action."),
            (StatusCodes.Status404NotFound, false) =>
                ("Page not found",
                    "The address may be incorrect, or the page may no longer be available."),
            (StatusCodes.Status409Conflict, false) =>
                ("The request conflicts with the current state",
                    "Refresh the data and try again."),
            (StatusCodes.Status429TooManyRequests, false) =>
                ("Too many requests",
                    "Wait a little and try again."),
            _ =>
                ("Something went wrong",
                    "The request could not be completed. Try again later."),
        };
}

using Microsoft.AspNetCore.Mvc.ModelBinding;
using Skopka.Abstraction.OperationResult;
using Skopka.Hello.UI;
using Skopka.Identity.Errors;

namespace Skopka.Hello.Admin.Pages;

/// <summary>
/// What an Admin form submission leaves for the GET it redirects to: errors,
/// the non-secret values the person typed and, after a consumed or expired
/// confirmation, which result to verify against the current state.
/// </summary>
/// <remarks>
/// The feedback survives refresh until the next submission on the page; a
/// successful status message is shown once.
/// </remarks>
internal sealed record HelloAdminFeedback(
    IReadOnlyList<KeyValuePair<string, string>> Errors,
    IReadOnlyDictionary<string, string?> Values,
    HelloAdminOutcomeCheck? Check = null)
{
    public static HelloAdminFeedback From(
        ModelStateDictionary modelState,
        IReadOnlyDictionary<string, string?>? values = null,
        HelloAdminOutcomeCheck? check = null)
        => new(
            Capture(modelState),
            values ?? new Dictionary<string, string?>(),
            check);

    public static IReadOnlyList<KeyValuePair<string, string>> Capture(
        ModelStateDictionary modelState)
        => modelState
            .Where(entry => entry.Value is { Errors.Count: > 0 })
            .SelectMany(entry => entry.Value!.Errors.Select(error =>
                new KeyValuePair<string, string>(entry.Key, error.ErrorMessage)))
            .ToArray();

    public void Apply(ModelStateDictionary modelState)
    {
        foreach (var (key, message) in Errors)
        {
            modelState.AddModelError(key, message);
        }
    }

    public string? Value(string target, string name)
        => Values.TryGetValue("target", out var stored)
            && string.Equals(stored, target, StringComparison.OrdinalIgnoreCase)
            && Values.TryGetValue(name, out var value)
                ? value
                : null;

    public override string ToString() => "HelloAdminFeedback { [redacted] }";
}

/// <summary>
/// The intended result of a confirmation whose challenge was already used or
/// expired. The page compares it with the current state and says whether the
/// action is in effect or must be started again.
/// </summary>
internal sealed record HelloAdminOutcomeCheck(
    string Action,
    Guid? UserId,
    Guid? RoleId,
    string ActionText);

internal static class HelloAdminErrorText
{
    public static void AddErrors(
        ModelStateDictionary modelState,
        IReadOnlyCollection<Error> errors,
        IHelloUiLocalizer text)
    {
        foreach (var error in errors)
        {
            if (error.Details is ValidationDetails validation)
            {
                foreach (var field in validation.Fields)
                {
                    foreach (var message in field.Value)
                    {
                        modelState.AddModelError(
                            field.Key,
                            Localize(text, error, field.Key, message));
                    }
                }

                continue;
            }

            modelState.AddModelError(
                string.Empty,
                Localize(text, error, null, error.Message));
        }
    }

    public static string Localize(
        IHelloUiLocalizer text,
        Error error,
        string? field,
        string fallback)
    {
        if (field is not null
            && text.TryGetString(
                $"Errors.{error.Code}.{field}",
                out var fieldMessage))
        {
            return fieldMessage;
        }

        return text.TryGetString(
            $"Errors.{error.Code}",
            out var message)
                ? message
                : fallback;
    }

    public static bool HasCode(IReadOnlyCollection<Error> errors, string code)
        => errors.Any(error => string.Equals(error.Code, code, StringComparison.Ordinal));
}

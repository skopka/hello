using Microsoft.AspNetCore.Mvc.ModelBinding;
using Skopka.Abstraction.OperationResult;
using Skopka.Identity.Errors;

namespace Skopka.Hello.UI.Pages;

internal static class HelloUiModelState
{
    public static void AddErrors(
        ModelStateDictionary modelState,
        IReadOnlyCollection<Error> errors,
        IHelloUiLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(modelState);
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(localizer);

        var hasFieldErrors = false;
        foreach (var error in errors)
        {
            if (error.Details is not ValidationDetails details)
            {
                continue;
            }

            foreach (var field in details.Fields)
            {
                foreach (var message in field.Value)
                {
                    modelState.AddModelError(
                        ToInputKey(field.Key),
                        LocalizeError(
                            localizer,
                            error,
                            field.Key,
                            message));
                    hasFieldErrors = true;
                }
            }
        }

        if (hasFieldErrors)
        {
            return;
        }

        foreach (var error in errors)
        {
            modelState.AddModelError(
                string.Empty,
                LocalizeError(
                    localizer,
                    error,
                    field: null,
                    error.Message));
        }
    }

    private static string LocalizeError(
        IHelloUiLocalizer localizer,
        Error error,
        string? field,
        string fallback)
    {
        if (field is not null
            && localizer.TryGetString(
                $"Errors.{error.Code}.{NormalizeField(field)}",
                out var fieldMessage))
        {
            return fieldMessage;
        }

        return localizer.TryGetString(
            $"Errors.{error.Code}",
            out var message)
                ? message
                : fallback;
    }

    private static string NormalizeField(string field)
        => field.StartsWith(
                "Input.",
                StringComparison.OrdinalIgnoreCase)
            ? field["Input.".Length..]
            : field;

    private static string ToInputKey(string key)
        => key.StartsWith(
                "Input.",
                StringComparison.OrdinalIgnoreCase)
            ? key
            : $"Input.{key}";
}

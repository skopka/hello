using Microsoft.AspNetCore.Mvc.ModelBinding;
using Skopka.Abstraction.OperationResult;
using Skopka.Identity.Errors;

namespace Skopka.Hello.UI.Pages;

internal static class HelloUiModelState
{
    public static void AddErrors(
        ModelStateDictionary modelState,
        IReadOnlyCollection<Error> errors,
        IHelloUiLocalizer localizer,
        Func<string, string>? fieldKeyMapper = null)
    {
        ArgumentNullException.ThrowIfNull(modelState);
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(localizer);

        foreach (var error in errors)
        {
            if (error.Details is not ValidationDetails details)
            {
                AddSummaryError(modelState, localizer, error);
                continue;
            }

            var addedDetail = false;
            foreach (var field in details.Fields)
            {
                foreach (var message in field.Value)
                {
                    var fieldKey = fieldKeyMapper?.Invoke(field.Key)
                        ?? ToInputKey(field.Key);
                    modelState.AddModelError(
                        modelState.ContainsKey(fieldKey)
                            ? fieldKey
                            : string.Empty,
                        LocalizeError(
                            localizer,
                            error,
                            field.Key,
                            message));
                    addedDetail = true;
                }
            }

            if (!addedDetail)
            {
                AddSummaryError(modelState, localizer, error);
            }
        }
    }

    private static void AddSummaryError(
        ModelStateDictionary modelState,
        IHelloUiLocalizer localizer,
        Error error)
        => modelState.AddModelError(
            string.Empty,
            LocalizeError(
                localizer,
                error,
                field: null,
                error.Message));

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

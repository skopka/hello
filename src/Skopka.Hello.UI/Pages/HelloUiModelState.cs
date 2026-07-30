using Microsoft.AspNetCore.Mvc.ModelBinding;
using Skopka.Abstraction.OperationResult;
using Skopka.Identity.Errors;

namespace Skopka.Hello.UI.Pages;

internal static class HelloUiModelState
{
    public static void AddErrors(
        ModelStateDictionary modelState,
        IReadOnlyCollection<Error> errors)
    {
        ArgumentNullException.ThrowIfNull(modelState);
        ArgumentNullException.ThrowIfNull(errors);

        var hasFieldErrors = false;
        foreach (var details in errors
            .Select(error => error.Details)
            .OfType<ValidationDetails>())
        {
            foreach (var field in details.Fields)
            {
                foreach (var message in field.Value)
                {
                    modelState.AddModelError(
                        ToInputKey(field.Key),
                        message);
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
                error.Message);
        }
    }

    private static string ToInputKey(string key)
        => key.StartsWith(
                "Input.",
                StringComparison.OrdinalIgnoreCase)
            ? key
            : $"Input.{key}";
}

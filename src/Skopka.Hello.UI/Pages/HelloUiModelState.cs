using System.Globalization;
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

        if (TryLocalizePasswordPolicyDetail(
                localizer,
                error,
                fallback,
                out var passwordPolicyMessage))
        {
            return passwordPolicyMessage;
        }

        // Password validators use ValidationDetails to explain the rule that
        // rejected the proposed password. Do not replace that actionable
        // explanation with the generic error-code translation.
        if (field is not null
            && String.Equals(
                error.Code,
                IdentityErrorCodes.PasswordRejected,
                StringComparison.Ordinal))
        {
            return fallback;
        }

        return localizer.TryGetString(
            $"Errors.{error.Code}",
            out var message)
                ? message
                : fallback;
    }

    private static bool TryLocalizePasswordPolicyDetail(
        IHelloUiLocalizer localizer,
        Error error,
        string fallback,
        out string message)
    {
        message = string.Empty;
        if (!String.Equals(
                error.Code,
                IdentityErrorCodes.PasswordRejected,
                StringComparison.Ordinal))
        {
            return false;
        }

        const string minimumPrefix =
            "Password must contain at least ";
        const string maximumPrefix =
            "Password must not exceed ";
        const string suffix = " characters.";

        if (TryReadPasswordLength(
                fallback,
                minimumPrefix,
                suffix,
                out var minimum))
        {
            return TryFormat(
                localizer,
                "Errors.identity.password.rejected.minimum_length",
                minimum,
                out message);
        }

        return TryReadPasswordLength(
                fallback,
                maximumPrefix,
                suffix,
                out var maximum)
            && TryFormat(
                localizer,
                "Errors.identity.password.rejected.maximum_length",
                maximum,
                out message);
    }

    private static bool TryReadPasswordLength(
        string message,
        string prefix,
        string suffix,
        out int length)
    {
        length = 0;
        if (!message.StartsWith(prefix, StringComparison.Ordinal)
            || !message.EndsWith(suffix, StringComparison.Ordinal))
        {
            return false;
        }

        var value = message.AsSpan(
            prefix.Length,
            message.Length - prefix.Length - suffix.Length);
        return Int32.TryParse(
            value,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out length);
    }

    private static bool TryFormat(
        IHelloUiLocalizer localizer,
        string key,
        int value,
        out string message)
    {
        if (!localizer.TryGetString(key, out var format))
        {
            message = string.Empty;
            return false;
        }

        message = String.Format(
            CultureInfo.CurrentCulture,
            format,
            value);
        return true;
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

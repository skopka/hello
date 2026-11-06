using Skopka.Abstraction.OperationResult;
using Skopka.Identity.Errors;

namespace Skopka.Hello.UI;

internal static class HelloUiRegistrationFieldsValidator
{
    internal static OperationResult Validate(IReadOnlyList<HelloUiProfileField> fields,
        IReadOnlyDictionary<string, string?> values)
    {
        if (fields.Count > 32 || values.Count > 32 || fields.Any(field => string.IsNullOrWhiteSpace(field.Name)
            || field.Name.Length > 64 || field.Name.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch != '_'))
            || fields.Select(field => field.Name).Distinct(StringComparer.Ordinal).Count() != fields.Count
            || values.Keys.Any(key => !fields.Any(field => field.Name == key)))
            return OperationResultFactory.Fail(new Error("hello.ui.registration_fields_invalid",
                "Registration fields are invalid.", ErrorType.Validation));
        foreach (var field in fields)
        {
            values.TryGetValue(field.Name, out var value);
            if (field.Required && string.IsNullOrWhiteSpace(value))
                return InvalidField(field.Name, "hello.ui.registration_field_required", "This field is required.");
            if (value?.Length > Math.Min(field.MaximumLength ?? 1024, 1024))
                return InvalidField(field.Name, "hello.ui.registration_field_too_long", "This field is too long.");
        }
        return OperationResultFactory.Success();
    }

    private static OperationResult InvalidField(string name, string code, string message)
        => OperationResultFactory.Fail(new Error(code, message, ErrorType.Validation,
            new ValidationDetails(new Dictionary<string, string[]> { [name] = [message] })));
}

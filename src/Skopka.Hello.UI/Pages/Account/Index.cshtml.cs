using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Skopka.Hello.UI.Pages;

[Authorize(Policy = HelloUiDefaults.AuthorizationPolicy)]
public sealed class AccountModel(
    IHelloUiApplication application,
    IHelloSessionCookieManager sessionCookies,
    IHelloAccountMessageSender messageSender,
    SkopkaHelloUiOptions uiOptions,
    IHelloUiLocalizer text)
    : PageModel
{
    public Guid UserId { get; private set; }

    public string DisplayName { get; private set; } = string.Empty;

    public string? UserName { get; private set; }

    public string? Email { get; private set; }

    public bool EmailConfirmed { get; private set; }

    public string? Phone { get; private set; }

    public bool PhoneConfirmed { get; private set; }

    public bool EmailConfirmationRequested { get; private set; }

    public bool PhoneConfirmationRequested { get; private set; }

    public bool EmailConfirmationRequired { get; private set; }

    public bool EmailConfirmationApplicable { get; private set; }

    public bool PhoneConfirmationRequired { get; private set; }

    public IReadOnlyList<HelloUiProfileField> ProfileFields
    {
        get;
        private set;
    } = [];

    public ChangeUserNameInput UserNameInput { get; set; } = new();

    public ChangeEmailInput EmailInput { get; set; } = new();

    public ChangePhoneInput PhoneInput { get; set; } = new();

    [BindProperty]
    public long ProfileExpectedVersion { get; set; }

    [BindProperty]
    public Dictionary<string, string?> ProfileValues { get; set; } = [];

    public Task<IActionResult> OnGetAsync(
        CancellationToken cancellationToken)
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        return LoadAccountAsync(
            preserveSubmittedValues: false,
            cancellationToken);
    }

    public async Task<IActionResult> OnPostChangeUserNameAsync(
        [Bind(Prefix = nameof(UserNameInput))]
        ChangeUserNameInput input,
        CancellationToken cancellationToken)
    {
        UserNameInput = input;
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        if (!HelloUiAccountOptions.IsEditable(uiOptions.Account.UserName))
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return await LoadAccountAsync(
                preserveSubmittedValues: true,
                cancellationToken);
        }

        var result = await application.ChangeUserNameAsync(
            new HelloUiChangeUserNameCommand(
                UserNameInput.ExpectedVersion,
                UserNameInput.UserName),
            HttpContext,
            cancellationToken);
        return await FinishMutationAsync(result, cancellationToken);
    }

    public async Task<IActionResult> OnPostChangeEmailAsync(
        [Bind(Prefix = nameof(EmailInput))]
        ChangeEmailInput input,
        CancellationToken cancellationToken)
    {
        EmailInput = input;
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        if (!HelloUiAccountOptions.IsEditable(uiOptions.Account.Email))
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return await LoadAccountAsync(
                preserveSubmittedValues: true,
                cancellationToken);
        }

        var result = await application.ChangeEmailAsync(
            new HelloUiChangeEmailCommand(
                EmailInput.ExpectedVersion,
                EmailInput.Email),
            HttpContext,
            cancellationToken);
        return await FinishMutationAsync(result, cancellationToken);
    }

    public async Task<IActionResult> OnPostChangePhoneAsync(
        [Bind(Prefix = nameof(PhoneInput))]
        ChangePhoneInput input,
        CancellationToken cancellationToken)
    {
        PhoneInput = input;
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        if (!HelloUiAccountOptions.IsEditable(uiOptions.Account.Phone))
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return await LoadAccountAsync(
                preserveSubmittedValues: true,
                cancellationToken);
        }

        var result = await application.ChangePhoneAsync(
            new HelloUiChangePhoneCommand(
                PhoneInput.ExpectedVersion,
                PhoneInput.Phone),
            HttpContext,
            cancellationToken);
        return await FinishMutationAsync(result, cancellationToken);
    }

    public async Task<IActionResult> OnPostUpdateProfileAsync(
        CancellationToken cancellationToken)
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        var result = await application.UpdateProfileAsync(
            new HelloUiUpdateProfileCommand(
                ProfileExpectedVersion,
                ProfileValues),
            HttpContext,
            cancellationToken);
        return await FinishMutationAsync(
            result,
            cancellationToken,
            ToProfileValueKey);
    }

    public async Task<IActionResult>
        OnPostRequestEmailConfirmationAsync(
            CancellationToken cancellationToken)
    {
        if (!uiOptions.IsEnabled(HelloUiPages.ContactConfirmation)
            || !uiOptions.ContactConfirmation.EmailEnabled)
        {
            return NotFound();
        }

        var loaded = await LoadAccountAsync(
            preserveSubmittedValues: false,
            cancellationToken);
        if (loaded is not PageResult)
        {
            return loaded;
        }

        if (string.IsNullOrWhiteSpace(Email))
        {
            ModelState.AddModelError(
                string.Empty,
                text["Account.NoEmailAddress"]);
            return Page();
        }

        if (!uiOptions.ContactConfirmation
                .IsEmailConfirmationRequired(Email))
        {
            return NotFound();
        }

        var deliveryAvailable = messageSender.CheckAvailability(
            HelloDeliveryChannel.Email);
        if (!deliveryAvailable.IsSuccess)
        {
            HelloUiModelState.AddErrors(
                ModelState,
                deliveryAvailable.Errors,
                text);
            return Page();
        }

        var result =
            await application.RequestEmailConfirmationAsync(
                Email,
                HttpContext,
                cancellationToken);
        if (!result.IsSuccess)
        {
            HelloUiModelState.AddErrors(
                ModelState,
                result.Errors,
                text);
            return Page();
        }

        EmailConfirmationRequested = true;
        return Page();
    }

    public async Task<IActionResult>
        OnPostRequestPhoneConfirmationAsync(
            CancellationToken cancellationToken)
    {
        if (!uiOptions.IsEnabled(HelloUiPages.ContactConfirmation)
            || !uiOptions.ContactConfirmation.PhoneEnabled)
        {
            return NotFound();
        }

        var loaded = await LoadAccountAsync(
            preserveSubmittedValues: false,
            cancellationToken);
        if (loaded is not PageResult)
        {
            return loaded;
        }

        if (string.IsNullOrWhiteSpace(Phone))
        {
            ModelState.AddModelError(
                string.Empty,
                text["Account.NoPhoneNumber"]);
            return Page();
        }

        var deliveryAvailable = messageSender.CheckAvailability(
            HelloDeliveryChannel.Sms);
        if (!deliveryAvailable.IsSuccess)
        {
            HelloUiModelState.AddErrors(
                ModelState,
                deliveryAvailable.Errors,
                text);
            return Page();
        }

        var result =
            await application.RequestPhoneConfirmationAsync(
                Phone,
                HttpContext,
                cancellationToken);
        if (!result.IsSuccess)
        {
            HelloUiModelState.AddErrors(
                ModelState,
                result.Errors,
                text);
            return Page();
        }

        PhoneConfirmationRequested = true;
        return Page();
    }

    private async Task<IActionResult> LoadAccountAsync(
        bool preserveSubmittedValues,
        CancellationToken cancellationToken)
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        var result = await application.GetAccountAsync(
            HttpContext,
            cancellationToken);
        if (!result.IsSuccess)
        {
            if (result.Errors.Any(
                    error => error.Type == Skopka.Abstraction
                        .OperationResult.ErrorType.Unauthorized))
            {
                return Challenge();
            }

            HelloUiModelState.AddErrors(
                ModelState,
                result.Errors,
                text);
            return Page();
        }

        ApplyAccount(result.Value, preserveSubmittedValues);
        return Page();
    }

    private async Task<IActionResult> FinishMutationAsync(
        Skopka.Abstraction.OperationResult
            .OperationResult<HelloUiAccount> result,
        CancellationToken cancellationToken,
        Func<string, string>? fieldKeyMapper = null)
    {
        if (result.IsSuccess)
        {
            return RedirectToPage();
        }

        HelloUiModelState.AddErrors(
            ModelState,
            result.Errors,
            text,
            fieldKeyMapper);
        return await LoadAccountAsync(
            preserveSubmittedValues: true,
            cancellationToken);
    }

    private static string ToProfileValueKey(string key)
    {
        const string inputPrefix = "Input.";
        const string dictionaryPrefix = $"{nameof(ProfileValues)}[";

        if (key.StartsWith(
                dictionaryPrefix,
                StringComparison.OrdinalIgnoreCase)
            && key.EndsWith(']'))
        {
            return key;
        }

        var normalized = key.StartsWith(
                inputPrefix,
                StringComparison.OrdinalIgnoreCase)
            ? key[inputPrefix.Length..]
            : key;
        return $"{dictionaryPrefix}{normalized}]";
    }

    private void ApplyAccount(
        HelloUiAccount account,
        bool preserveSubmittedValues)
    {
        UserId = account.UserId;
        DisplayName = account.DisplayName;
        UserName = account.UserName;
        Email = account.Email;
        EmailConfirmed = account.EmailConfirmed;
        Phone = account.Phone;
        PhoneConfirmed = account.PhoneConfirmed;
        EmailConfirmationApplicable = uiOptions.ContactConfirmation
            .IsEmailConfirmationRequired(Email);
        EmailConfirmationRequired = !EmailConfirmed
            && EmailConfirmationApplicable;
        PhoneConfirmationRequired = !PhoneConfirmed
            && uiOptions.ContactConfirmation.PhoneEnabled
            && Phone is not null;

        UserNameInput.ExpectedVersion = account.Version;
        EmailInput.ExpectedVersion = account.Version;
        PhoneInput.ExpectedVersion = account.Version;
        ProfileExpectedVersion = account.Version;
        if (!preserveSubmittedValues)
        {
            UserNameInput.UserName = account.UserName ?? string.Empty;
            EmailInput.Email = account.Email;
            PhoneInput.Phone = account.Phone;
            ProfileValues = account.ProfileFields.ToDictionary(
                field => field.Name,
                field => field.Value,
                StringComparer.Ordinal);
        }

        ProfileFields = account.ProfileFields
            .Select(field => preserveSubmittedValues
                && ProfileValues.TryGetValue(field.Name, out var value)
                    ? field with { Value = value }
                    : field)
            .ToArray();
    }

    public async Task<IActionResult> OnPostLogoutAsync(
        CancellationToken cancellationToken)
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        var refreshToken = sessionCookies.ReadRefreshToken(
            HttpContext);
        if (refreshToken is not null)
        {
            await application.LogoutAsync(
                refreshToken,
                cancellationToken);
        }

        sessionCookies.DeleteSessionCookies(HttpContext);
        await HttpContext.SignOutAsync(
            HelloUiDefaults.AuthenticationScheme);
        return RedirectToPage("/SkopkaHello/Login");
    }

    public sealed class ChangeUserNameInput
    {
        public long ExpectedVersion { get; set; }

        [Required(ErrorMessage = "Validation.Required")]
        [StringLength(100, ErrorMessage = "Validation.StringLength")]
        [Display(Name = "Field.UserName")]
        public string UserName { get; set; } = string.Empty;
    }

    public sealed class ChangeEmailInput
    {
        public long ExpectedVersion { get; set; }

        [EmailAddress(ErrorMessage = "Validation.EmailAddress")]
        [StringLength(320, ErrorMessage = "Validation.StringLength")]
        public string? Email { get; set; }
    }

    public sealed class ChangePhoneInput
    {
        public long ExpectedVersion { get; set; }

        [StringLength(
            Skopka.Identity.Authentication.IdentityLoginLimits
                .MaximumLoginLength,
            ErrorMessage = "Validation.StringLength")]
        public string? Phone { get; set; }
    }
}

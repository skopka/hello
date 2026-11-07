using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace Skopka.Hello.UI.Pages;

[Authorize(Policy = HelloUiDefaults.AuthorizationPolicy)]
public sealed class AccountModel(
    IHelloUiApplication application,
    IHelloSessionCookieManager sessionCookies,
    IHelloAccountMessageSender messageSender,
    SkopkaHelloUiOptions uiOptions,
    IHelloUiLocalizer text,
    IHelloUiPhoneBindingApplication? phoneBinding = null)
    : PageModel
{
    public Guid UserId { get; private set; }

    public string DisplayName { get; private set; } = string.Empty;

    public string? UserName { get; private set; }

    public string? Email { get; private set; }

    public bool EmailConfirmed { get; private set; }

    public string? Phone { get; private set; }

    public bool PhoneConfirmed { get; private set; }

    [BindProperty(SupportsGet = true)]
    public bool EmailConfirmationRequested { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool PhoneConfirmationRequested { get; set; }

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

    [BindProperty]
    public long ProfileExpectedVersion { get; set; }

    [BindProperty]
    public Dictionary<string, string?> ProfileValues { get; set; } = [];

    private IReadOnlyDictionary<HelloAccountArea, HelloAccountAccess> access =
        new Dictionary<HelloAccountArea, HelloAccountAccess>();
    public bool CanView(HelloAccountArea area) => !access.TryGetValue(area, out var value) || value.Visible;
    public bool CanEdit(HelloAccountArea area) => !access.TryGetValue(area, out var value) || (value.Visible && value.Editable);
    public bool PhoneBindingEnabled => uiOptions.Account.PhoneBindingEnabled && phoneBinding is not null
        && (HttpContext.RequestServices.GetService<IHelloSiteContext>()?.Current?.PhoneOtpEnabled ?? true)
        && CanEdit(HelloAccountArea.Phone);

    /// <summary>Shows a one-time confirmation after successful verified phone binding.</summary>
    [TempData]
    public bool PhoneBindingSucceeded { get; set; }

    [BindProperty, ValidateNever]
    public string? BindingPhone { get; set; }
    [BindProperty, ValidateNever]
    public string? BindingTicket { get; set; }
    [BindProperty, ValidateNever]
    public string? BindingCode { get; set; }

    public async Task<IActionResult> OnPostBeginPhoneBindingAsync(CancellationToken cancellationToken)
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        if (!uiOptions.Account.PhoneBindingEnabled || phoneBinding is null) return NotFound();
        ModelState.Clear();
        var result = await phoneBinding.BeginAsync(BindingPhone ?? string.Empty, HttpContext, cancellationToken);
        if (result.IsSuccess) BindingTicket = result.Value.Ticket;
        else HelloUiModelState.AddErrors(ModelState, result.Errors, text);
        return await LoadAccountAsync(true, cancellationToken);
    }

    public async Task<IActionResult> OnPostCompletePhoneBindingAsync(CancellationToken cancellationToken)
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        if (!uiOptions.Account.PhoneBindingEnabled || phoneBinding is null) return NotFound();
        ModelState.Clear();
        var result = await phoneBinding.CompleteAsync(BindingTicket ?? string.Empty, BindingCode ?? string.Empty, HttpContext, cancellationToken);
        if (result.IsSuccess)
        {
            PhoneBindingSucceeded = true;
            return RedirectToPage(pageName: null, pageHandler: null,
                routeValues: null, fragment: "phone-binding");
        }
        if (result.Errors.Any(error => error.Code == Skopka.Identity.Errors.IdentityErrorCodes.DuplicatePhone))
            ModelState.AddModelError(nameof(BindingCode), text["Errors.identity.phone.duplicate"].Value);
        else HelloUiModelState.AddErrors(ModelState, result.Errors, text);
        return await LoadAccountAsync(true, cancellationToken);
    }

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

        return RedirectToPage(
            "/SkopkaHello/Account/Index",
            new { emailConfirmationRequested = true });
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

        return RedirectToPage(
            "/SkopkaHello/Account/Index",
            new { phoneConfirmationRequested = true });
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
        access = account.Access;
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
        ProfileExpectedVersion = account.Version;
        if (!preserveSubmittedValues)
        {
            UserNameInput.UserName = account.UserName ?? string.Empty;
            EmailInput.Email = account.Email;
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
        var accountSwitcher = HttpContext.RequestServices
            .GetRequiredService<IHelloUiAccountSwitcher>();
        var savedAccounts = accountSwitcher.List(HttpContext);
        if (uiOptions.AccountSwitching.Enabled
            && HelloUiPrincipalFactory.TryGetUserId(
                User,
                out var userId))
        {
            await accountSwitcher.RemoveAsync(
                HttpContext,
                userId,
                revokeSession: true,
                cancellationToken);
        }
        else
        {
            var refreshToken = sessionCookies.ReadRefreshToken(
                HttpContext);
            if (refreshToken is not null)
            {
                await application.LogoutAsync(
                    refreshToken,
                    cancellationToken);
            }
        }

        sessionCookies.DeleteSessionCookies(HttpContext);
        await HttpContext.SignOutAsync(
            HelloUiDefaults.AuthenticationScheme);
        if (HelloUiSite.LogoutPath(HttpContext, uiOptions) is { } redirectPath)
        {
            return LocalRedirect(redirectPath);
        }
        return uiOptions.AccountSwitching.Enabled
            && savedAccounts.Count > 1
                ? RedirectToPage("/SkopkaHello/Accounts")
                : RedirectToPage("/SkopkaHello/Login");
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
}

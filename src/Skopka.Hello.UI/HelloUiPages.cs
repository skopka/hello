namespace Skopka.Hello.UI;

[Flags]
public enum HelloUiPages
{
    None = 0,
    Login = 1 << 0,
    Registration = 1 << 1,
    PasswordRecovery = 1 << 2,
    ContactConfirmation = 1 << 3,
    Account = 1 << 4,
    Sessions = 1 << 5,
    AccountSecurity = 1 << 6,
    ExternalIdentity = 1 << 7,

    All = Login
        | Registration
        | PasswordRecovery
        | ContactConfirmation
        | Account
        | Sessions
        | AccountSecurity
        | ExternalIdentity,
}

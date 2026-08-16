namespace Skopka.Hello.Endpoints;

public sealed record ExternalRegisterRequest<TProfile>(
    string? UserName,
    string? Email,
    string? Phone,
    TProfile Profile)
{
    public bool AcceptTermsOfService { get; init; }

    public bool AcceptPrivacyPolicy { get; init; }
}

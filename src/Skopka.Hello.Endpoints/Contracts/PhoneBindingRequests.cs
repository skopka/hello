namespace Skopka.Hello.Endpoints;

public sealed record BeginPhoneBindingRequest(string Phone);
public sealed record CompletePhoneBindingRequest(string Ticket, string Code);

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;

namespace Skopka.Hello.WebAuthn;

internal enum HelloWebAuthnCeremony
{
    Registration = 1,
    SignIn = 2,
}

internal sealed record HelloWebAuthnTicket(
    Guid FlowId,
    HelloWebAuthnCeremony Ceremony,
    Guid? UserId,
    byte[] Challenge,
    DateTimeOffset ExpiresAt);

/// <summary>
/// Seals a challenge into a string the browser carries and hands back.
///
/// The alternative is a row per started ceremony, most of which are abandoned:
/// a page opened and closed, an authenticator that never answered. Protecting
/// the value instead means the server keeps nothing until something succeeds,
/// and still cannot be talked into accepting a challenge it did not issue —
/// which is the only property a stored challenge would have provided.
///
/// What protection cannot say is whether a ticket has already been answered.
/// That is <see cref="IHelloWebAuthnFlowStore"/>.
/// </summary>
internal sealed class HelloWebAuthnTickets
{
    private const string Purpose = "Skopka.Hello.WebAuthn.Ticket.v1";

    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IDataProtector protector;
    private readonly TimeProvider time;

    public HelloWebAuthnTickets(
        IDataProtectionProvider dataProtection,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(dataProtection);
        ArgumentNullException.ThrowIfNull(time);
        protector = dataProtection.CreateProtector(Purpose);
        this.time = time;
    }

    public (string Ticket, HelloWebAuthnTicket Payload) Issue(
        HelloWebAuthnCeremony ceremony,
        Guid? userId,
        TimeSpan lifetime)
    {
        var payload = new HelloWebAuthnTicket(
            Guid.NewGuid(),
            ceremony,
            userId,
            RandomNumberGenerator.GetBytes(32),
            time.GetUtcNow().Add(lifetime));
        var ticket = protector.Protect(JsonSerializer.Serialize(
            new Wire(
                payload.FlowId,
                (int)payload.Ceremony,
                payload.UserId,
                Base64Url.EncodeToString(payload.Challenge),
                payload.ExpiresAt.ToUnixTimeSeconds()),
            Json));
        return (ticket, payload);
    }

    /// <summary>
    /// Null for anything this server did not seal, sealed for another purpose,
    /// expired, or answering the other ceremony. The caller learns nothing
    /// about which: a ticket that will not do is a ticket that will not do.
    /// </summary>
    public HelloWebAuthnTicket? Read(
        string? ticket,
        HelloWebAuthnCeremony expected)
    {
        if (string.IsNullOrWhiteSpace(ticket) || ticket.Length > 4096)
        {
            return null;
        }

        Wire? wire;
        try
        {
            wire = JsonSerializer.Deserialize<Wire>(
                protector.Unprotect(ticket),
                Json);
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }

        if (wire is null
            || wire.FlowId == Guid.Empty
            || wire.Ceremony != (int)expected
            || !Base64Url.IsValid(wire.Challenge))
        {
            return null;
        }

        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(wire.ExpiresAt);
        if (expiresAt <= time.GetUtcNow())
        {
            return null;
        }

        return new HelloWebAuthnTicket(
            wire.FlowId,
            expected,
            wire.UserId,
            Base64Url.DecodeFromChars(wire.Challenge),
            expiresAt);
    }

    private sealed record Wire(
        [property: JsonPropertyName("f")] Guid FlowId,
        [property: JsonPropertyName("c")] int Ceremony,
        [property: JsonPropertyName("u")] Guid? UserId,
        [property: JsonPropertyName("h")] string Challenge,
        [property: JsonPropertyName("e")] long ExpiresAt);
}

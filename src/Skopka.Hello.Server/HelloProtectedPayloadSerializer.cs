using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Skopka.Hello.Server;

internal sealed class HelloProtectedPayloadSerializer(
    IDataProtectionProvider dataProtectionProvider)
{
    private readonly IDataProtector anonymousRequestProtector =
        dataProtectionProvider.CreateProtector(
            "Skopka.Hello.Server.AnonymousAccountMessage.v1");
    private readonly IDataProtector accountMessageProtector =
        dataProtectionProvider.CreateProtector(
            "Skopka.Hello.Server.AccountMessage.v1");

    public byte[] ProtectAnonymousRequest(
        HelloAnonymousAccountMessageRequest request)
        => Protect(anonymousRequestProtector, request);

    public HelloAnonymousAccountMessageRequest UnprotectAnonymousRequest(
        byte[] payload)
        => Unprotect<HelloAnonymousAccountMessageRequest>(
            anonymousRequestProtector,
            payload);

    public byte[] ProtectAccountMessage(HelloAccountMessage message)
        => Protect(accountMessageProtector, message);

    public HelloAccountMessage UnprotectAccountMessage(byte[] payload)
        => Unprotect<HelloAccountMessage>(
            accountMessageProtector,
            payload);

    private static byte[] Protect<T>(IDataProtector protector, T value)
    {
        var serialized = JsonSerializer.SerializeToUtf8Bytes(value);
        try
        {
            return protector.Protect(serialized);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(serialized);
        }
    }

    private static T Unprotect<T>(
        IDataProtector protector,
        byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var serialized = protector.Unprotect(payload);
        try
        {
            return JsonSerializer.Deserialize<T>(serialized)
                ?? throw new CryptographicException(
                    "The protected delivery payload is empty.");
        }
        catch (JsonException exception)
        {
            throw new CryptographicException(
                "The protected delivery payload is invalid.",
                exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(serialized);
        }
    }
}

using System.Security.Cryptography;

namespace Skopka.Hello.Oidc;

internal static class HelloOidcFlowId
{
    public static Guid Create()
    {
        Span<byte> value = stackalloc byte[16];
        RandomNumberGenerator.Fill(value);
        value[7] = (byte)((value[7] & 0x0f) | 0x40);
        value[8] = (byte)((value[8] & 0x3f) | 0x80);
        return new Guid(value, bigEndian: true);
    }
}

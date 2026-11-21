using System.Security.Cryptography;

namespace Prism.Api.Auth;

/// <summary>Unguessable random values for cookies and form nonces.</summary>
internal static class SecureToken
{
    public static string Hex(int bytes) => Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes));

    public static string Base64(int bytes) => Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes));
}

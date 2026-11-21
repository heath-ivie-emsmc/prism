using System.Net;
using System.Net.Sockets;

namespace Prism.Api.Auth;

/// <summary>Turns a client address into a rate-limit partition key, so one client cannot hold many buckets.</summary>
internal static class ClientAddress
{
    public const string Unknown = "unknown";

    /// <summary>
    /// IPv4-mapped IPv6 addresses become plain IPv4 (one client, one key). IPv6 clients are keyed by their /64, because a
    /// single subscriber normally controls a whole /64 and could otherwise rotate through addresses to dodge the limit.
    /// </summary>
    public static string PartitionKey(IPAddress? address)
    {
        if (address is null)
        {
            return Unknown;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return address.ToString();
        }

        Span<byte> bytes = stackalloc byte[16];
        address.TryWriteBytes(bytes, out _);
        bytes[8..].Clear();
        return new IPAddress(bytes).ToString() + "/64";
    }
}

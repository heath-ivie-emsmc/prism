using System.Collections.Concurrent;
using System.Globalization;
using System.Net;

namespace Prism.Testing.Common;

/// <summary>
/// Makes it impossible for a test or a test helper to run DDL, DROP or INSERT against anything but a throwaway container database. The warehouse is read-only and
/// production-shaped: a developer may well have its connection string in user secrets or the environment, and no test may ever reach it.
/// A fixture that starts a Testcontainers database calls <see cref="Register"/> with that container's connection string, which is accepted only for a local or
/// private-network host. Every helper that writes calls <see cref="Assert"/> first, which throws unless the host, port and database are one a fixture registered.
/// The check needs no database library: it reads the connection string text itself.
/// </summary>
public static class TestDatabaseGuard
{
    private static readonly ConcurrentDictionary<string, byte> Created = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Records a container database as the only kind of target a test may write to. Returns the connection string for chaining.</summary>
    public static string Register(string containerConnectionString)
    {
        Endpoint endpoint = Parse(containerConnectionString);
        if (!IsLocalOrContainerHost(endpoint.Host))
        {
            throw new InvalidOperationException("A test database must be a local container: its host is not localhost, a loopback address or a private-network address.");
        }

        Created[endpoint.Key] = 0;
        return containerConnectionString;
    }

    /// <summary>Returns the connection string when it points at a registered container database, and throws otherwise. Never echoes the connection string.</summary>
    public static string Assert(string connectionString)
    {
        Endpoint endpoint = Parse(connectionString);
        if (!IsLocalOrContainerHost(endpoint.Host) || !Created.ContainsKey(endpoint.Key))
        {
            throw new InvalidOperationException("Refusing to run a statement that writes: the connection is not a database created by a test fixture on a local container. Tests must never touch the warehouse or any real database.");
        }

        return connectionString;
    }

    /// <summary>True for localhost, loopback addresses, host.docker.internal and private-network or link-local IP addresses (a remote Docker host).</summary>
    internal static bool IsLocalOrContainerHost(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host.Equals("host.docker.internal", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!IPAddress.TryParse(host, out IPAddress? address))
        {
            return false;
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        byte[] bytes = address.GetAddressBytes();
        return bytes.Length == 4 && (bytes[0] == 10 || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) || (bytes[0] == 192 && bytes[1] == 168) || (bytes[0] == 169 && bytes[1] == 254));
    }

    private static Endpoint Parse(string connectionString)
    {
        ArgumentNullException.ThrowIfNull(connectionString);

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = part.IndexOf('=', StringComparison.Ordinal);
            if (equals > 0)
            {
                values[part[..equals].Trim()] = part[(equals + 1)..].Trim();
            }
        }

        string host = First(values, "Host", "Server", "Data Source");
        string database = First(values, "Database", "Db");
        string port = values.TryGetValue("Port", out string? p) ? p : "5432";
        if (host.Length == 0 || host.Contains(',', StringComparison.Ordinal) || database.Length == 0 || !int.TryParse(port, NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            throw new InvalidOperationException("The test database connection string must name one host, a port and a database.");
        }

        return new Endpoint(host, $"{host}:{port}/{database}");
    }

    private static string First(Dictionary<string, string> values, params string[] names) =>
        names.Select(name => values.GetValueOrDefault(name)).FirstOrDefault(value => !string.IsNullOrEmpty(value)) ?? "";

    private sealed record Endpoint(string Host, string Key);
}

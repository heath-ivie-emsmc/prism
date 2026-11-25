using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Npgsql;

namespace Prism.Infrastructure.Configuration;

/// <summary>Parses AWS Secrets Manager RDS-style JSON into an Npgsql connection string.</summary>
public static class PostgreSqlRdsSecretJson
{
    /// <summary>
    /// Expected shape (SecretString): <c>hostname</c>, <c>port</c>, <c>engine</c>, <c>database</c>, <c>username</c>, <c>password</c>.
    /// </summary>
    public static bool TryParse(string json, [NotNullWhen(true)] out string? connectionString)
    {
        connectionString = null;
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(json.Trim());
            if (!TryCreatePayload(doc.RootElement, out RdsSecretPayload payload))
                return false;

            connectionString = BuildConnectionString(payload);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private readonly record struct RdsSecretPayload(
        string Host,
        int Port,
        string Database,
        string Username,
        string Password);

    private static bool TryCreatePayload(JsonElement root, out RdsSecretPayload payload)
    {
        payload = default;
        if (!TryGetString(root, "hostname", out var host) || string.IsNullOrWhiteSpace(host))
            return false;
        if (!TryGetString(root, "database", out var database) || string.IsNullOrWhiteSpace(database))
            return false;
        if (!TryGetString(root, "username", out var username) || string.IsNullOrWhiteSpace(username))
            return false;
        if (!TryGetString(root, "password", out var password))
            return false;
        if (!TryGetPort(root, out var port))
            return false;
        if (!IsPostgresEngineOrUnset(root))
            return false;

        payload = new RdsSecretPayload(host.Trim(), port, database.Trim(), username.Trim(), password);
        return true;
    }

    private static bool IsPostgresEngineOrUnset(JsonElement root)
    {
        if (!root.TryGetProperty("engine", out JsonElement engineEl) || engineEl.ValueKind != JsonValueKind.String)
            return true;

        var engine = engineEl.GetString();
        return string.IsNullOrWhiteSpace(engine)
               || string.Equals(engine, "postgres", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildConnectionString(RdsSecretPayload payload)
    {
        var b = new NpgsqlConnectionStringBuilder
        {
            Host = payload.Host,
            Port = payload.Port,
            Database = payload.Database,
            Username = payload.Username,
            Password = payload.Password,
            Timezone = "UTC",
            SslMode = IsLocalHost(payload.Host) ? SslMode.Disable : SslMode.Require,
        };

        return b.ConnectionString;
    }

    private static bool IsLocalHost(string host)
    {
        return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
               || string.Equals(host, "127.0.0.1", StringComparison.Ordinal);
    }

    private static bool TryGetString(JsonElement root, string name, [NotNullWhen(true)] out string? value)
    {
        value = null;
        if (!root.TryGetProperty(name, out JsonElement el) || el.ValueKind != JsonValueKind.String)
            return false;
        value = el.GetString();
        return !string.IsNullOrEmpty(value);
    }

    private static bool TryGetPort(JsonElement root, out int port)
    {
        port = 5432;
        if (!root.TryGetProperty("port", out JsonElement el))
            return true;
        return el.ValueKind switch
        {
            JsonValueKind.String => int.TryParse(el.GetString(), out port),
            JsonValueKind.Number => el.TryGetInt32(out port),
            _ => false
        };
    }
}

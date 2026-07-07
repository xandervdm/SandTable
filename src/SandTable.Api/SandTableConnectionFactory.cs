using Npgsql;

namespace SandTable.Api;

public sealed class SandTableConnectionFactory(IConfiguration configuration)
{
    public NpgsqlConnection CreateConnection()
    {
        var connectionString = configuration.GetConnectionString("SandTableDatabase")
            ?? Environment.GetEnvironmentVariable("VULTR_POSTGRES_URL_SAND_TABLE_DEV");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Set ConnectionStrings:SandTableDatabase or VULTR_POSTGRES_URL_SAND_TABLE_DEV.");
        }

        return new NpgsqlConnection(NormalizeConnectionString(connectionString));
    }

    private static string NormalizeConnectionString(string connectionString)
    {
        if (!connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            && !connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return connectionString;
        }

        var uri = new Uri(connectionString);
        var userParts = uri.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = uri.AbsolutePath.TrimStart('/'),
            Username = Uri.UnescapeDataString(userParts[0]),
            Password = userParts.Length > 1 ? Uri.UnescapeDataString(userParts[1]) : null,
            SslMode = SslMode.Require
        };

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var keyValue = pair.Split('=', 2);
            if (keyValue.Length != 2)
            {
                continue;
            }

            var key = Uri.UnescapeDataString(keyValue[0]);
            var value = Uri.UnescapeDataString(keyValue[1]);
            if (key.Equals("sslmode", StringComparison.OrdinalIgnoreCase)
                && Enum.TryParse<SslMode>(value, ignoreCase: true, out var sslMode))
            {
                builder.SslMode = sslMode;
            }
        }

        return builder.ConnectionString;
    }
}

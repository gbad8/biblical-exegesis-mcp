using Npgsql;

namespace BiblicalExegesisMcp.Infrastructure.Database;

public static class PostgresConnectionFactory
{
    public static string ResolveConnectionString(string? databaseUrlOrConnString)
    {
        var raw = string.IsNullOrWhiteSpace(databaseUrlOrConnString)
            ? Environment.GetEnvironmentVariable("DATABASE_URL")
              ?? Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            : databaseUrlOrConnString;

        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException(
                "Connection string não configurada. Defina DATABASE_URL ou ConnectionStrings__Postgres.");

        raw = raw.Trim();
        if (!raw.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
            !raw.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            var rawBuilder = new NpgsqlConnectionStringBuilder(raw);
            if (!raw.Contains("gss encryption mode", StringComparison.OrdinalIgnoreCase) &&
                !raw.Contains("gssencryptionmode", StringComparison.OrdinalIgnoreCase))
            {
                rawBuilder.GssEncryptionMode = GssEncryptionMode.Disable;
            }
            return rawBuilder.ConnectionString;
        }

        var uri = new Uri(raw);
        var userInfo = uri.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = uri.AbsolutePath.TrimStart('/'),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty,
            Pooling = true,
            MinPoolSize = 1,
            MaxPoolSize = 20,
            GssEncryptionMode = GssEncryptionMode.Disable
        };

        if (!string.IsNullOrEmpty(uri.Query))
        {
            foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Split('=', 2);
                if (kv.Length != 2) continue;
                var key = Uri.UnescapeDataString(kv[0]).ToLowerInvariant();
                var val = Uri.UnescapeDataString(kv[1]);
                if (key == "sslmode" && Enum.TryParse<SslMode>(val, ignoreCase: true, out var ssl))
                    builder.SslMode = ssl;
                else if (key == "gssencmode" && Enum.TryParse<GssEncryptionMode>(val, ignoreCase: true, out var gss))
                    builder.GssEncryptionMode = gss;
            }
        }

        return builder.ConnectionString;
    }

    public static NpgsqlDataSource CreateDataSource(string? databaseUrlOrConnString = null)
    {
        var connString = ResolveConnectionString(databaseUrlOrConnString);
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connString);
        return dataSourceBuilder.Build();
    }
}

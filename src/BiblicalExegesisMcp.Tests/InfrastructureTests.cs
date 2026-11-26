using BiblicalExegesisMcp.Infrastructure.Database;
using Npgsql;
using Xunit;

namespace BiblicalExegesisMcp.Tests;

public sealed class InfrastructureTests
{
    [Fact]
    public void ResolveConnectionString_DisablesGssEncryptionByDefaultForUri()
    {
        var connStr = PostgresConnectionFactory.ResolveConnectionString(
            "postgres://exegesis:secret@db:5432/greek_nt");

        var parsed = new NpgsqlConnectionStringBuilder(connStr);
        Assert.Equal("db", parsed.Host);
        Assert.Equal(5432, parsed.Port);
        Assert.Equal("greek_nt", parsed.Database);
        Assert.Equal("exegesis", parsed.Username);
        Assert.Equal("secret", parsed.Password);
        Assert.Equal(GssEncryptionMode.Disable, parsed.GssEncryptionMode);
    }

    [Fact]
    public void ResolveConnectionString_RespectsExplicitGssEncModeQueryParam()
    {
        var connStr = PostgresConnectionFactory.ResolveConnectionString(
            "postgresql://exegesis:secret@db:5432/greek_nt?sslmode=Require&gssencmode=Prefer");

        var parsed = new NpgsqlConnectionStringBuilder(connStr);
        Assert.Equal(SslMode.Require, parsed.SslMode);
        Assert.Equal(GssEncryptionMode.Prefer, parsed.GssEncryptionMode);
    }

    [Fact]
    public void ResolveConnectionString_DisablesGssByDefaultForRawStringAndPreservesExplicit()
    {
        var defaultRaw = PostgresConnectionFactory.ResolveConnectionString(
            "Host=db;Port=5432;Database=greek_nt;Username=exegesis;Password=secret");
        Assert.Equal(GssEncryptionMode.Disable, new NpgsqlConnectionStringBuilder(defaultRaw).GssEncryptionMode);

        var explicitRaw = PostgresConnectionFactory.ResolveConnectionString(
            "Host=db;Port=5432;Database=greek_nt;Username=exegesis;Password=secret;GSS Encryption Mode=Prefer");
        Assert.Equal(GssEncryptionMode.Prefer, new NpgsqlConnectionStringBuilder(explicitRaw).GssEncryptionMode);
    }
}

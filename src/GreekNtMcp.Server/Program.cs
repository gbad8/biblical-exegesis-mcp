using GreekNtMcp.Infrastructure.Database;
using GreekNtMcp.Infrastructure.Repositories;
using GreekNtMcp.Server.Middleware;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

var dataSource = PostgresConnectionFactory.CreateDataSource(
    builder.Configuration.GetConnectionString("Postgres"));

builder.Services.AddSingleton<NpgsqlDataSource>(dataSource);
builder.Services.AddSingleton<ExegesisRepository>();

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new()
        {
            Name = "greek-nt-exegesis-mcp",
            Version = "1.0.0"
        };
    })
    .WithHttpTransport(options =>
    {
        options.Stateless = true;
    })
    .WithToolsFromAssembly();

var app = builder.Build();

app.UseMiddleware<ApiKeyAuthMiddleware>();

app.MapGet("/health", async (ExegesisRepository repo, CancellationToken ct) =>
{
    try
    {
        var status = await repo.CheckHealthAsync(ct);
        var ready = status.LexiconCount > 0 && status.TagntTokenCount > 0;
        return Results.Json(new
        {
            status = ready ? "healthy" : "degraded_no_data",
            database = status.DatabaseConnected,
            counts = new
            {
                lexiconEntries = status.LexiconCount,
                abbottSmithEntries = status.AbbottSmithCount,
                lsjEntries = status.LsjCount,
                tagntTokens = status.TagntTokenCount,
                f35Tokens = status.F35TokenCount
            },
            sources = status.Sources
        }, statusCode: ready ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
    }
    catch (Exception ex)
    {
        return Results.Json(new
        {
            status = "unhealthy",
            error = ex.Message
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapMcp("/mcp");

app.Run();

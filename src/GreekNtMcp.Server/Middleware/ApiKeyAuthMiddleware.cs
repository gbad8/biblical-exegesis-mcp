using System.Security.Cryptography;
using System.Text;

namespace GreekNtMcp.Server.Middleware;

public sealed class ApiKeyAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string? _apiKey;
    private readonly bool _requireApiKey;

    public ApiKeyAuthMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _apiKey = configuration["MCP_API_KEY"] ?? Environment.GetEnvironmentVariable("MCP_API_KEY");
        var requireFlag = configuration["REQUIRE_API_KEY"] ?? Environment.GetEnvironmentVariable("REQUIRE_API_KEY");
        _requireApiKey = string.Equals(requireFlag, "true", StringComparison.OrdinalIgnoreCase);

        if (_requireApiKey && string.IsNullOrWhiteSpace(_apiKey))
            throw new InvalidOperationException(
                "REQUIRE_API_KEY=true, mas MCP_API_KEY não foi definida no ambiente.");
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Health endpoint stays accessible for Docker/OCI probes
        if (context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            await _next(context);
            return;
        }

        var authHeader = context.Request.Headers.Authorization.ToString();
        string? provided = null;

        if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            provided = authHeader["Bearer ".Length..].Trim();
        else if (context.Request.Headers.TryGetValue("X-API-Key", out var headerKey))
            provided = headerKey.ToString().Trim();

        if (string.IsNullOrEmpty(provided) || !FixedTimeEquals(provided, _apiKey))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("""{"error":"Unauthorized: valid Bearer token required."}""");
            return;
        }

        await _next(context);
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        var bytesA = Encoding.UTF8.GetBytes(a);
        var bytesB = Encoding.UTF8.GetBytes(b);
        return CryptographicOperations.FixedTimeEquals(bytesA, bytesB);
    }
}

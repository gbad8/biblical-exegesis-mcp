using System.Security.Cryptography;
using System.Text.Json;

namespace GreekNtMcp.Etl.Download;

public sealed record DatasetSource(
    string Name,
    string FileName,
    string Url,
    string VersionRef);

public sealed record DatasetManifest(List<DatasetSource> Sources);

public sealed record DownloadedDataset(
    DatasetSource Source,
    string LocalPath,
    string Sha256Hex,
    long SizeBytes);

public sealed class DatasetDownloader
{
    private readonly HttpClient _http;
    private readonly string _dataDir;

    public DatasetDownloader(string dataDir)
    {
        _dataDir = dataDir;
        Directory.CreateDirectory(_dataDir);
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("GreekNtMcp-Etl/1.0");
    }

    public static DatasetManifest LoadManifest(string manifestPath)
    {
        var json = File.ReadAllText(manifestPath);
        var manifest = JsonSerializer.Deserialize<DatasetManifest>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        return manifest ?? throw new InvalidOperationException($"Manifesto inválido: {manifestPath}");
    }

    public async Task<Dictionary<string, DownloadedDataset>> EnsureAllAsync(
        DatasetManifest manifest,
        bool forceDownload = false,
        CancellationToken ct = default)
    {
        var result = new Dictionary<string, DownloadedDataset>(StringComparer.OrdinalIgnoreCase);

        foreach (var src in manifest.Sources)
        {
            var localPath = Path.Combine(_dataDir, src.FileName);
            if (forceDownload || !File.Exists(localPath) || new FileInfo(localPath).Length == 0)
            {
                Console.WriteLine($"[ETL] Baixando {src.Name} -> {src.FileName}...");
                var tmpPath = localPath + ".part";
                using var response = await _http.GetAsync(src.Url, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                await using (var remote = await response.Content.ReadAsStreamAsync(ct))
                await using (var local = File.Create(tmpPath))
                {
                    await remote.CopyToAsync(local, ct);
                }

                File.Move(tmpPath, localPath, overwrite: true);
            }
            else
            {
                Console.WriteLine($"[ETL] Usando cache local para {src.Name} ({src.FileName}).");
            }

            var sha = await ComputeSha256Async(localPath, ct);
            var size = new FileInfo(localPath).Length;
            result[src.Name] = new DownloadedDataset(src, localPath, sha, size);
        }

        return result;
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexStringLower(hash);
    }
}

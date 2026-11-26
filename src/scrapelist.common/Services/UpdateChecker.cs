using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Scrapelist.Services;

public sealed record UpdateInfo(Version Version, string Tag, string AssetName, string DownloadUrl, long Size, string? Notes);

/// <summary>
/// Looks up the latest GitHub release and reports whether it is newer than the running build.
/// Releases are tagged v1.0.N by .github/workflows/release.yml and carry one asset per platform.
/// </summary>
public sealed class UpdateChecker
{
    public const string DefaultRepo = "matt-dx/scrapelist";
    public const string WindowsAsset = "scrapelist-windows-x64.zip";
    public const string AndroidAsset = "scrapelist-android.apk";

    private static readonly Regex TagPattern = new(@"^v?(\d+(\.\d+){1,3})$", RegexOptions.Compiled);

    private readonly HttpClient _http;
    private readonly string _repo;

    public UpdateChecker(HttpClient? http = null, string repo = DefaultRepo)
    {
        _http = http ?? new HttpClient();
        _repo = repo;
    }

    /// <summary>Returns the newer release containing <paramref name="assetName"/>, or null if up to date.</summary>
    public async Task<UpdateInfo?> CheckAsync(Version current, string assetName, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{_repo}/releases/latest");
        req.Headers.UserAgent.Add(new ProductInfoHeaderValue("scrapelist-updater", "1.0"));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return Parse(doc.RootElement, current, assetName);
    }

    public static UpdateInfo? Parse(JsonElement release, Version current, string assetName)
    {
        if (release.TryGetProperty("draft", out var d) && d.GetBoolean()) return null;
        if (release.TryGetProperty("prerelease", out var p) && p.GetBoolean()) return null;

        var tag = release.GetProperty("tag_name").GetString() ?? "";
        var m = TagPattern.Match(tag);
        if (!m.Success || !Version.TryParse(m.Groups[1].Value, out var latest)) return null;
        if (Normalize(latest) <= Normalize(current)) return null;

        foreach (var asset in release.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() != assetName) continue;
            return new UpdateInfo(
                latest, tag, assetName,
                asset.GetProperty("browser_download_url").GetString()!,
                asset.GetProperty("size").GetInt64(),
                release.TryGetProperty("body", out var b) ? b.GetString() : null);
        }
        return null; // release exists but lacks an asset for this platform
    }

    // "1.0" and "1.0.0.0" must compare equal to "1.0.0"
    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));

    /// <summary>Downloads <paramref name="url"/> to <paramref name="destination"/>, reporting 0..1 progress.</summary>
    public async Task DownloadAsync(string url, string destination, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.UserAgent.Add(new ProductInfoHeaderValue("scrapelist-updater", "1.0"));
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        var total = resp.Content.Headers.ContentLength;
        var tmp = destination + ".part";
        await using (var src = await resp.Content.ReadAsStreamAsync(ct))
        await using (var dst = File.Create(tmp))
        {
            var buf = new byte[81920];
            long read = 0;
            int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                read += n;
                if (total > 0) progress?.Report((double)read / total.Value);
            }
        }
        File.Move(tmp, destination, overwrite: true);
    }
}

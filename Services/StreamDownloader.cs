namespace Scrapelist.Services;

public class StreamDownloader
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    private const int BufferSize = 81920; // 80KB

    private readonly DebugLogger _log;

    public StreamDownloader(DebugLogger log)
    {
        _log = log;
    }

    public async Task DownloadAsync(
        string url,
        string partFilePath,
        long? totalBytes,
        Action<long, long>? onProgress,
        CancellationToken ct)
    {
        long existingBytes = 0;

        if (File.Exists(partFilePath))
        {
            existingBytes = new FileInfo(partFilePath).Length;

            if (totalBytes.HasValue && existingBytes >= totalBytes.Value)
            {
                _log.Log("Download", $"Skipping (already complete): {Path.GetFileName(partFilePath)}");
                return;
            }

            if (existingBytes > 0)
                _log.Log("Download", $"Resuming from {existingBytes} bytes: {Path.GetFileName(partFilePath)}");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (existingBytes > 0)
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existingBytes, null);

        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            _log.Log("Download", $"416 Range Not Satisfiable — deleting stale .part: {Path.GetFileName(partFilePath)}");
            File.Delete(partFilePath);
            await DownloadAsync(url, partFilePath, totalBytes, onProgress, ct);
            return;
        }

        response.EnsureSuccessStatusCode();

        var contentLength = response.Content.Headers.ContentLength;
        var total = totalBytes ?? (contentLength.HasValue ? contentLength.Value + existingBytes : 0L);

        _log.Log("Download", $"Starting: {Path.GetFileName(partFilePath)} ({total} bytes, resume={existingBytes > 0})");

        await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(
            partFilePath,
            existingBytes > 0 ? FileMode.Append : FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            useAsync: true);

        var buffer = new byte[BufferSize];
        var downloaded = existingBytes;
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            downloaded += bytesRead;
            onProgress?.Invoke(downloaded, total);
        }

        _log.Log("Download", $"Complete: {Path.GetFileName(partFilePath)} ({downloaded} bytes)");
    }
}

using Scrapelist.Models;

namespace Scrapelist.Services;

public class PlaylistWriter
{
    public async Task WriteAsync(
        string filePath,
        IReadOnlyList<DownloadItem> items,
        DownloadType downloadType)
    {
        await using var writer = new StreamWriter(filePath, append: false);
        await writer.WriteLineAsync("#EXTM3U");
        await writer.WriteLineAsync();

        var orderedItems = items
            .Where(i => i.Status is DownloadStatus.Completed or DownloadStatus.Skipped)
            .OrderBy(i => i.PlaylistIndex);

        foreach (var item in orderedItems)
        {
            var durationSeconds = item.Duration.HasValue
                ? (int)item.Duration.Value.TotalSeconds
                : -1;

            var displayName = $"{item.ChannelName} - {item.Title}";
            await writer.WriteLineAsync($"#EXTINF:{durationSeconds},{displayName}");

            var filePaths = GetPlaylistEntryPath(item, downloadType);
            await writer.WriteLineAsync(filePaths);
            await writer.WriteLineAsync();
        }
    }

    private static string GetPlaylistEntryPath(DownloadItem item, DownloadType downloadType)
    {
        return downloadType switch
        {
            DownloadType.Audio => item.AudioFilePath ?? "",
            DownloadType.Video => item.VideoFilePath ?? "",
            DownloadType.Both => item.VideoFilePath ?? "",
            _ => ""
        };
    }
}

namespace Scrapelist.Models;

public record PlaylistInfo(
    string Title,
    string PlaylistId,
    List<DownloadItem> Items
);

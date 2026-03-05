using Scrapelist.Models;
using YoutubeExplode;
using YoutubeExplode.Common;
using YoutubeExplode.Playlists;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.ClosedCaptions;
using YoutubeExplode.Videos.Streams;

namespace Scrapelist.Services;

public class YouTubeService
{
    private readonly YoutubeClient _client = new();

    public async Task<PlaylistInfo?> TryResolvePlaylistAsync(Uri uri, int maxRetries, CancellationToken ct = default)
    {
        var playlistId = PlaylistId.TryParse(uri.ToString());
        if (playlistId is null)
            return null;

        var playlist = await _client.Playlists.GetAsync(playlistId.Value, ct);
        var videos = await _client.Playlists.GetVideosAsync(playlistId.Value, ct);

        var items = videos.Select((v, i) => new DownloadItem
        {
            VideoId = v.Id,
            Title = v.Title,
            ChannelName = v.Author.ChannelTitle,
            PlaylistIndex = i + 1,
            Duration = v.Duration,
            MaxRetries = maxRetries
        }).ToList();

        return new PlaylistInfo(playlist.Title ?? "Untitled Playlist", playlistId.Value, items);
    }

    public async Task<DownloadItem> ResolveVideoAsync(Uri uri, int maxRetries, CancellationToken ct = default)
    {
        var videoId = VideoId.Parse(uri.ToString());
        var video = await _client.Videos.GetAsync(videoId, ct);

        return new DownloadItem
        {
            VideoId = video.Id,
            Title = video.Title,
            ChannelName = video.Author.ChannelTitle,
            PlaylistIndex = 0,
            Duration = video.Duration,
            MaxRetries = maxRetries
        };
    }

    public async Task<StreamManifest> GetStreamManifestAsync(string videoId, CancellationToken ct = default)
    {
        return await _client.Videos.Streams.GetManifestAsync(videoId, ct);
    }

    public IStreamInfo GetBestAudioStream(StreamManifest manifest)
    {
        return manifest
            .GetAudioOnlyStreams()
            .GetWithHighestBitrate();
    }

    public IStreamInfo GetBestVideoStream(StreamManifest manifest)
    {
        return manifest
            .GetVideoOnlyStreams()
            .GetWithHighestVideoQuality();
    }

    public string GetStreamUrl(IStreamInfo streamInfo)
    {
        return streamInfo.Url;
    }

    public long? GetStreamSize(IStreamInfo streamInfo)
    {
        return streamInfo.Size.Bytes > 0 ? (long)streamInfo.Size.Bytes : null;
    }

    public string GetStreamContainer(IStreamInfo streamInfo)
    {
        return streamInfo.Container.Name;
    }

    public async Task<ClosedCaptionTrackInfo?> GetBestCaptionTrackAsync(string videoId, CancellationToken ct = default)
    {
        var manifest = await _client.Videos.ClosedCaptions.GetManifestAsync(videoId, ct);

        // Prefer English, fall back to first available
        return manifest.GetByLanguage("en")
            ?? manifest.Tracks.FirstOrDefault();
    }

    public async Task DownloadCaptionAsync(ClosedCaptionTrackInfo track, string outputPath, CancellationToken ct = default)
    {
        await _client.Videos.ClosedCaptions.DownloadAsync(track, outputPath, progress: null, cancellationToken: ct);
    }
}

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Scrapelist.Models;
using YoutubeExplode;
using YoutubeExplode.Channels;
using YoutubeExplode.Common;
using YoutubeExplode.Playlists;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.ClosedCaptions;
using YoutubeExplode.Videos.Streams;

namespace Scrapelist.Services;

public class YouTubeService
{
    private readonly YoutubeClient _client = new();

    // Consent cookie so EU requests get the channel page rather than the consent interstitial
    private const string ConsentCookie = "SOCS=CAISEwgDEgk0ODE3Nzk3MjQaAmVuIAEaBgiA_LyaBg";
    private const int MaxContinuationPages = 50;

    private static readonly HttpClient Http = new();
    private static readonly Regex PlaylistIdRegex = new("\"playlistId\":\"([A-Za-z0-9_-]{10,})\"", RegexOptions.Compiled);
    private static readonly Regex ContinuationRegex = new("\"continuationCommand\":\\{\"token\":\"([^\"]+)\"", RegexOptions.Compiled);
    private static readonly Regex ClientVersionRegex = new("\"INNERTUBE_CLIENT_VERSION\":\"([^\"]+)\"", RegexOptions.Compiled);

    public async Task<PlaylistInfo?> TryResolvePlaylistAsync(Uri uri, int maxRetries, CancellationToken ct = default)
    {
        var playlistId = PlaylistId.TryParse(uri.ToString());
        if (playlistId is not null)
        {
            var playlist = await _client.Playlists.GetAsync(playlistId.Value, ct);
            var videos = await _client.Playlists.GetVideosAsync(playlistId.Value, ct);

            return new PlaylistInfo(playlist.Title ?? "Untitled Playlist", playlistId.Value, ToItems(videos, maxRetries));
        }

        return await TryResolveChannelAsync(uri, maxRetries, ct);
    }

    /// <summary>
    /// Resolves channel URLs (/@handle, /channel/UC..., /c/slug, /user/name) optionally followed by a tab:
    /// videos, shorts, streams, releases or playlists. A bare channel URL resolves to all uploads.
    /// </summary>
    private async Task<PlaylistInfo?> TryResolveChannelAsync(Uri uri, int maxRetries, CancellationToken ct)
    {
        var url = uri.ToString();
        Channel channel;
        if (ChannelId.TryParse(url) is { } channelId)
            channel = await _client.Channels.GetAsync(channelId, ct);
        else if (ChannelHandle.TryParse(url) is { } handle)
            channel = await _client.Channels.GetByHandleAsync(handle, ct);
        else if (ChannelSlug.TryParse(url) is { } slug)
            channel = await _client.Channels.GetBySlugAsync(slug, ct);
        else if (UserName.TryParse(url) is { } user)
            channel = await _client.Channels.GetByUserAsync(user, ct);
        else
            return null;

        var tab = GetChannelTab(uri);

        // Uploads playlists: UU = all uploads, UULF = videos, UUSH = shorts, UULV = live streams
        var uploadsPrefix = tab switch
        {
            "videos" => "UULF",
            "shorts" => "UUSH",
            "streams" => "UULV",
            "releases" or "playlists" => null,
            _ => "UU"
        };

        if (uploadsPrefix is not null)
        {
            var uploadsId = uploadsPrefix + channel.Id.Value[2..];
            var videos = await _client.Playlists.GetVideosAsync(uploadsId, ct);
            var label = tab is null ? "Uploads" : Capitalize(tab);
            return new PlaylistInfo($"{channel.Title} - {label}", uploadsId, ToItems(videos, maxRetries));
        }

        var channelUrl = $"https://www.youtube.com/channel/{channel.Id}/{tab}";
        var playlistIds = await ScrapePlaylistIdsAsync(channelUrl, ct);

        var allVideos = new List<PlaylistVideo>();
        var seen = new HashSet<string>();
        foreach (var id in playlistIds)
        {
            var videos = await _client.Playlists.GetVideosAsync(id, ct);
            allVideos.AddRange(videos.Where(v => seen.Add(v.Id.Value)));
        }

        return new PlaylistInfo($"{channel.Title} - {Capitalize(tab!)}", channel.Id, ToItems(allVideos, maxRetries));
    }

    private static string? GetChannelTab(Uri uri)
    {
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var idIndex = Array.FindIndex(segments, s => s.StartsWith('@'));
        if (idIndex < 0 && segments.Length > 0 && segments[0] is "channel" or "c" or "user")
            idIndex = 1;

        return idIndex >= 0 && idIndex + 1 < segments.Length
            ? segments[idIndex + 1].ToLowerInvariant()
            : null;
    }

    /// <summary>
    /// Collects the playlist IDs listed on a channel tab page, following browse continuations for long grids.
    /// </summary>
    private static async Task<List<string>> ScrapePlaylistIdsAsync(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Cookie", ConsentCookie);
        request.Headers.Add("Accept-Language", "en-US,en;q=0.9");
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36");

        using var response = await Http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(ct);

        var ids = new List<string>();
        var seen = new HashSet<string>();
        void Collect(string text)
        {
            foreach (Match m in PlaylistIdRegex.Matches(text))
                if (seen.Add(m.Groups[1].Value))
                    ids.Add(m.Groups[1].Value);
        }

        Collect(html);

        var clientVersion = ClientVersionRegex.Match(html) is { Success: true } v ? v.Groups[1].Value : "2.20250101.00.00";
        var pending = new Queue<string>(ContinuationRegex.Matches(html).Select(m => m.Groups[1].Value));
        var visited = new HashSet<string>();

        while (pending.Count > 0 && visited.Count < MaxContinuationPages)
        {
            var token = pending.Dequeue();
            if (!visited.Add(token))
                continue;

            var body = JsonSerializer.Serialize(new
            {
                context = new { client = new { clientName = "WEB", clientVersion, hl = "en" } },
                continuation = token
            });

            using var contResponse = await Http.PostAsync(
                "https://www.youtube.com/youtubei/v1/browse?prettyPrint=false",
                new StringContent(body, Encoding.UTF8, "application/json"), ct);
            if (!contResponse.IsSuccessStatusCode)
                continue;

            var json = await contResponse.Content.ReadAsStringAsync(ct);
            Collect(json);
            foreach (Match m in ContinuationRegex.Matches(json))
                pending.Enqueue(m.Groups[1].Value);
        }

        return ids;
    }

    private static List<DownloadItem> ToItems(IEnumerable<PlaylistVideo> videos, int maxRetries) =>
        videos.Select((v, i) => new DownloadItem
        {
            VideoId = v.Id,
            Title = v.Title,
            ChannelName = v.Author.ChannelTitle,
            PlaylistIndex = i + 1,
            Duration = v.Duration,
            MaxRetries = maxRetries
        }).ToList();

    private static string Capitalize(string s) => char.ToUpperInvariant(s[0]) + s[1..];

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

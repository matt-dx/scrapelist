using Scrapelist.Models;
using Scrapelist.Extensions;
namespace Scrapelist.Services;

public class FileNamingService
{
    private static readonly char[] InvalidChars = Path.GetInvalidFileNameChars();
    private const int MaxFileNameLength = 200;

    public static string GetFileName(DownloadItem item, string extension, bool indexed)
    {
        List<string?> parts =
        [
            indexed ? $"{item?.PlaylistIndex:D2}" : null,
            item?.ChannelName,
            item?.Title
                .Replace($"{item.ChannelName} - ", "")
                .Replace($" - {item.ChannelName}", "")
        ];

        var name = string.Join(
            " - ",
            parts.RemoveNulls());

        name = Sanitize(name);

        if (name.Length > MaxFileNameLength)
            name = name[..MaxFileNameLength];

        return $"{name}.{extension}";
    }

    public static string GetPlaylistFileName(string playlistTitle)
    {
        var name = $"{Sanitize(playlistTitle)}";

        if (name.Length > MaxFileNameLength)
            name = name[..MaxFileNameLength];

        return $"# {name}.m3u";
    }

    public static string GetPartFileName(string fileName)
    {
        return $"{fileName}.part";
    }

    private static string Sanitize(string name)
    {
        var chars = name.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (InvalidChars.Contains(chars[i]))
                chars[i] = chars[i] switch
                {
                    '"' => '\uFF02',
                    '*' => '\uFF0A',
                    ':' => '\uA789',
                    '<' => '\u2039',
                    '>' => '\u203A',
                    '?' => '\uFF1F',
                    '|' => '\u01C0',
                    '/' => '\uFF0F',
                    '\\' => '\uFF3C',
                    _ => '_',
                };

        }
        return new string(chars).Trim();
    }
}

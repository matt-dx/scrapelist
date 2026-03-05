namespace Scrapelist;

public static class Formatting
{
    public static string FormatTime(TimeSpan ts)
    {
        return ts.Hours > 0
            ? $"{ts.Hours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes}:{ts.Seconds:D2}";
    }

    public static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1}GB",
            >= 1_048_576 => $"{bytes / 1_048_576.0:F1}MB",
            >= 1024 => $"{bytes / 1024.0:F1}KB",
            _ => $"{bytes}B"
        };
    }
}

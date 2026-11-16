namespace NetDownloader.Gui.ViewModels.Helpers;

public static class FormatHelper
{
    private static readonly string[] SizeSuffixes = { "B", "KB", "MB", "GB", "TB" };

    public static string FormatBytes(long bytes)
    {
        if (bytes < 0) return "--";
        if (bytes == 0) return "0 B";

        int place = Convert.ToInt32(Math.Floor(Math.Log(bytes, 1024)));
        place = Math.Clamp(place, 0, SizeSuffixes.Length - 1);
        double num = bytes / Math.Pow(1024, place);
        return $"{num:0.#} {SizeSuffixes[place]}";
    }

    public static string FormatSpeed(long bytesPerSecond)
    {
        if (bytesPerSecond <= 0) return "--";
        double mbps = bytesPerSecond / (1024.0 * 1024.0);
        if (mbps >= 1.0)
            return $"{mbps:0.#} MB/s";

        double kbps = bytesPerSecond / 1024.0;
        return $"{kbps:0.#} KB/s";
    }

    public static string FormatEta(long remainingBytes, long speedBytesPerSecond)
    {
        if (remainingBytes <= 0) return "00m 00s";
        if (speedBytesPerSecond <= 0) return "--";

        double seconds = (double)remainingBytes / speedBytesPerSecond;
        if (seconds > 86400 * 7) return "> 7 days";

        var ts = TimeSpan.FromSeconds(seconds);
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours:D2}h {ts.Minutes:D2}m";

        return $"{ts.Minutes:D2}m {ts.Seconds:D2}s";
    }
}

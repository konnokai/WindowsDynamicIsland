namespace WindowsDynamicIsland.Models;

/// <summary>Normalizes provider timestamps and advances a playing timeline between provider updates.</summary>
public sealed record MediaProgress(TimeSpan Elapsed, TimeSpan Duration)
{
    public double Percent => Elapsed.TotalSeconds / Duration.TotalSeconds * 100;
    public string Text => $"{Format(Elapsed)} / {Format(Duration)}";

    public static MediaProgress? Create(TimeSpan start, TimeSpan end, TimeSpan position,
        DateTimeOffset updatedAt, DateTimeOffset now, bool isPlaying, double playbackRate)
    {
        // Zero or reversed ranges mean the provider has not supplied a usable duration.
        if (start < TimeSpan.Zero || end <= start || position < start || position > end)
            return null;

        var duration = end - start;
        var seconds = (position - start).TotalSeconds;
        if (isPlaying && updatedAt > DateTimeOffset.FromUnixTimeSeconds(0) && updatedAt <= now
            && double.IsFinite(playbackRate))
            seconds += (now - updatedAt).TotalSeconds * playbackRate;

        return new MediaProgress(TimeSpan.FromSeconds(Math.Clamp(seconds, 0, duration.TotalSeconds)), duration);
    }

    private static string Format(TimeSpan value) => value.TotalHours >= 1
        ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
        : $"{(int)value.TotalMinutes}:{value.Seconds:00}";
}

using WindowsDynamicIsland.Models;

internal static class MediaProgressTests
{
    public static int Run()
    {
        var now = DateTimeOffset.UtcNow;
        MediaProgress? Read(double start, double end, double position, bool playing = false, double rate = 1) =>
            MediaProgress.Create(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), TimeSpan.FromSeconds(position),
                now.AddSeconds(-5), now, playing, rate);

        Check(Read(0, 0, 0) is null, "missing duration is hidden");
        Check(Read(20, 10, 20) is null, "invalid duration is hidden");
        Check(Read(0, 100, 101) is null, "mismatched position is hidden");
        Check(Read(0, 100, 0)?.Text == "0:00 / 1:40", "track beginning is valid");
        Check(Read(10, 110, 40)?.Elapsed.TotalSeconds == 30, "nonzero timeline origin");
        Check(Read(0, 100, 40)?.Elapsed.TotalSeconds == 40, "paused position does not advance");
        Check(Read(0, 100, 40, true, 2)?.Elapsed.TotalSeconds == 50, "playing position respects playback rate");
        Check(Read(0, 100, 99, true)?.Percent == 100, "position stops at duration");
        Check(Read(0, 7200, 3661)?.Text == "1:01:01 / 2:00:00", "long media formats hours");
        Check(Read(0, 100, 15)?.Elapsed.TotalSeconds == 15, "seek uses the new provider position");
        return 0;
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        Console.WriteLine($"PASS {name}");
    }
}

using NAudio.Wave;
using WindowsDynamicIsland.Services;

internal static class AudioRecoveryTests
{
    public static async Task<int> RunAsync()
    {
        var captures = new List<FakeCapture>();
        var unavailable = false;
        using var service = new SystemAudioLevelService(() =>
        {
            if (unavailable) throw new InvalidOperationException("Output disconnected");
            var capture = new FakeCapture();
            captures.Add(capture);
            return capture;
        });
        float[]? latest = null;
        service.SpectrumChanged += (_, bands) => latest = bands;
        service.Start();
        service.Start();
        Require(captures.Count == 1, "Start is idempotent");
        var original = captures[0];
        await service.QueueRecovery();
        Require(original.Disposed && captures.Count == 2 && captures[1].Started,
            "Changing output replaces and starts capture");
        await service.QueueRecovery(original);
        Require(captures.Count == 2, "Stale stop callback cannot replace current capture");
        unavailable = true;
        await service.QueueRecovery();
        Require(captures[1].Disposed, "Disconnected output releases old capture");
        Require(latest is { Length: 5 } && latest.All(value => value == 0), "Disconnected output clears frozen spectrum");
        unavailable = false;
        await service.QueueRecovery();
        Require(captures.Count == 3 && captures[2].Started, "Returning output resumes capture");
        await service.QueueRecovery(captures[2]);
        Require(captures.Count == 4 && captures[2].Disposed, "Unexpected stop recovers capture");
        service.Dispose();
        await service.QueueRecovery();
        service.Start();
        Require(captures.Count == 4 && captures[3].Disposed, "Shutdown prevents queued restarts");
        return 0;
    }

    private static void Require(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        Console.WriteLine($"PASS {description}");
    }

    private sealed class FakeCapture : IWaveIn
    {
        public WaveFormat WaveFormat { get; set; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        public bool Started { get; private set; }
        public bool Disposed { get; private set; }
        public event EventHandler<WaveInEventArgs>? DataAvailable { add { } remove { } }
        public event EventHandler<StoppedEventArgs>? RecordingStopped;
        public void StartRecording() => Started = true;
        public void StopRecording() => RecordingStopped?.Invoke(this, new StoppedEventArgs());
        public void Dispose() => Disposed = true;
    }
}

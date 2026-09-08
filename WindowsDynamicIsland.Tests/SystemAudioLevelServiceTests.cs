using System.Reflection;
using NAudio.Wave;
using WindowsDynamicIsland.Services;

// Run without arguments for deterministic regressions; --live samples one second
// of spectrum events from the current default output without saving audio.
if (args.Contains("--codex"))
    return await CodexNotificationServiceTests.RunAsync();

if (args.Contains("--media"))
    return MediaProgressTests.Run();

if (args.Contains("--live"))
{
    using var formatProbe = new WasapiLoopbackCapture();
    Console.WriteLine($"Default output: {formatProbe.WaveFormat}");
    using var service = new SystemAudioLevelService();
    var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var frameCount = 0;
    var equalNonzeroFrames = 0;
    var signalFrames = 0;
    var sums = new double[5];
    // Each published window advances 512 samples; observe a full second of updates.
    var framesPerSecond = (int)Math.Ceiling(formatProbe.WaveFormat.SampleRate / 512d);
    service.SpectrumChanged += (_, bands) =>
    {
        if (completion.Task.IsCompleted) return;
        if (bands.Any(value => value > 0))
        {
            signalFrames++;
            if (bands.All(value => value == bands[0])) equalNonzeroFrames++;
        }
        for (var band = 0; band < bands.Length; band++) sums[band] += bands[band];
        frameCount++;
        if (frameCount == 1) Console.WriteLine($"First frame: {string.Join(", ", bands.Select(value => value.ToString("G9")))}");
        if (frameCount >= framesPerSecond) completion.TrySetResult();
    };
    service.Start();
    await completion.Task;
    Console.WriteLine($"Frames: {frameCount}; signal: {signalFrames}; identical nonzero bands: {equalNonzeroFrames}");
    Console.WriteLine($"Band means: {string.Join(", ", sums.Select(value => (value / frameCount).ToString("G9")))}");
    return signalFrames > 0 && equalNonzeroFrames == 0 ? 0 : 1;
}

using var subject = new SystemAudioLevelService();
var buffer = (float[])typeof(SystemAudioLevelService)
    .GetField("_sampleBuffer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(subject)!;
var publish = typeof(SystemAudioLevelService)
    .GetMethod("PublishSpectrum", BindingFlags.Instance | BindingFlags.NonPublic)!;
float[] latest = [];
subject.SpectrumChanged += (_, bands) => latest = bands;
const int sampleRate = 48000;
var failures = 0;

// Bin-centered tones isolate each configured band. Quiet signals must retain
// their frequency identity instead of becoming five copies of broadband RMS.
int[] toneBins = [3, 8, 16, 32, 80];
foreach (var amplitude in new[] { 0.001f, 0.00001f })
{
    for (var band = 0; band < toneBins.Length; band++)
    {
        for (var index = 0; index < buffer.Length; index++)
            buffer[index] = amplitude * MathF.Sin(2 * MathF.PI * toneBins[band] * index / buffer.Length);
        publish.Invoke(subject, [sampleRate]);
        var valid = latest.All(float.IsFinite)
            && latest.All(value => value >= 0 && value <= 1)
            && latest[band] > amplitude
            && latest.Where((_, index) => index != band).All(value => value < latest[band]);
        Console.WriteLine($"{(valid ? "PASS" : "FAIL")} {toneBins[band] * sampleRate / (float)buffer.Length} Hz amplitude {amplitude}: {string.Join(", ", latest)}");
        if (!valid) failures++;
    }
}
// A quiet treble tone should receive the same visibility lift as the other bars.
// The uncompressed Hann peak cannot exceed amplitude * the existing display gain.
const float quietTrebleAmplitude = 0.001f;
for (var index = 0; index < buffer.Length; index++)
    buffer[index] = quietTrebleAmplitude * MathF.Sin(2 * MathF.PI * toneBins[4] * index / buffer.Length);
publish.Invoke(subject, [sampleRate]);
var trebleVisible = latest[4] > quietTrebleAmplitude * 32;
Console.WriteLine($"{(trebleVisible ? "PASS" : "FAIL")} quiet treble visibility: {latest[4]}");
if (!trebleVisible) failures++;
Array.Clear(buffer);
publish.Invoke(subject, [sampleRate]);
var silenceValid = latest.All(value => value == 0);
Console.WriteLine($"{(silenceValid ? "PASS" : "FAIL")} silence");
return failures == 0 && silenceValid ? 0 : 1;

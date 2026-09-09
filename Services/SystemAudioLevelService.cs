using NAudio.Dsp;
using NAudio.Wave;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace WindowsDynamicIsland.Services;

/// <summary>Reads the default system render mix through WASAPI loopback and exposes five frequency-band levels.</summary>
public sealed class SystemAudioLevelService : IDisposable, IMMNotificationClient
{
    private const int FftLength = 1024;
    private const int FftOrder = 10;
    private const int HopLength = FftLength / 2;
    private static readonly Guid IeeeFloatSubFormat = new("00000003-0000-0010-8000-00aa00389b71");
    private static readonly (float Min, float Max)[] FrequencyBands =
    [
        // Focus the five music bars on bass, three midrange regions, and treble
        // with common vocal/instrument energy rather than the sparse upper octave.
        (60, 250),
        (250, 500),
        (500, 1000),
        (1000, 2000),
        (2000, 6000)
    ];

    private IWaveIn? _capture;
    private readonly object _lifecycleLock = new();
    private readonly Func<IWaveIn> _createCapture;
    private readonly bool _monitorDevices;
    private MMDeviceEnumerator? _deviceEnumerator;
    private bool _started;
    private bool _disposed;
    private readonly float[] _sampleBuffer = new float[FftLength];
    private readonly Complex[] _fftBuffer = new Complex[FftLength];
    private int _sampleCount;

    public event EventHandler<float[]>? SpectrumChanged;

    public SystemAudioLevelService() : this(() => new WasapiLoopbackCapture(), true) { }

    internal SystemAudioLevelService(Func<IWaveIn> createCapture, bool monitorDevices = false)
    {
        _createCapture = createCapture;
        _monitorDevices = monitorDevices;
    }

    public void Start()
    {
        lock (_lifecycleLock)
        {
            if (_disposed || _started) return;
            _started = true;
            if (_monitorDevices)
            {
                _deviceEnumerator = new MMDeviceEnumerator();
                _deviceEnumerator.RegisterEndpointNotificationCallback(this);
            }
            ReplaceCapture();
        }
    }

    /// <summary>Rebinds loopback after endpoint invalidation. Only lifecycle work holds this lock;
    /// capture callbacks never take it because StopRecording waits for the capture thread.</summary>
    private void ReplaceCapture()
    {
        ReleaseCapture();
        _sampleCount = 0;
        Array.Clear(_sampleBuffer);
        Array.Clear(_fftBuffer);
        SpectrumChanged?.Invoke(this, new float[FrequencyBands.Length]);
        try
        {
            _capture = _createCapture();
            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;
            _capture.StartRecording();
        }
        catch
        {
            // An endpoint can temporarily disappear while a monitor powers down.
            // Keep notifications registered so its return can restart capture.
            ReleaseCapture();
        }
    }

    private void ReleaseCapture()
    {
        var capture = _capture;
        _capture = null;
        if (capture is null) return;
        capture.DataAvailable -= OnDataAvailable;
        capture.RecordingStopped -= OnRecordingStopped;
        try { capture.StopRecording(); }
        catch { /* An invalidated endpoint may already have stopped. */ }
        finally { capture.Dispose(); }
    }

    /// <summary>Moves COM and capture callbacks onto a worker before stopping or disposing
    /// WASAPI, avoiding capture-thread self-joins and blocking Windows notification delivery.</summary>
    internal Task QueueRecovery(IWaveIn? stoppedCapture = null)
    {
        return Task.Run(() =>
        {
            lock (_lifecycleLock)
            {
                if (_disposed || !_started) return;
                if (stoppedCapture is not null && !ReferenceEquals(stoppedCapture, _capture)) return;
                ReplaceCapture();
            }
        });
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs args)
    {
        if (sender is IWaveIn capture) _ = QueueRecovery(capture);
    }

    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (flow == DataFlow.Render && role == Role.Multimedia) _ = QueueRecovery();
    }

    public void OnDeviceStateChanged(string deviceId, DeviceState newState) => _ = QueueRecovery();
    public void OnDeviceAdded(string deviceId) => _ = QueueRecovery();
    public void OnDeviceRemoved(string deviceId) => _ = QueueRecovery();
    public void OnPropertyValueChanged(string deviceId, PropertyKey key) { }

    private void OnDataAvailable(object? sender, WaveInEventArgs args)
    {
        var capture = _capture;
        if (capture is null || !ReferenceEquals(sender, capture) || args.BytesRecorded == 0)
        {
            return;
        }

        var format = capture.WaveFormat;
        var bytesPerSample = Math.Max(1, format.BitsPerSample / 8);
        var bytesPerFrame = bytesPerSample * Math.Max(1, format.Channels);
        if (bytesPerFrame == 0)
        {
            return;
        }

        for (var offset = 0; offset + bytesPerFrame <= args.BytesRecorded; offset += bytesPerFrame)
        {
            var frame = 0f;
            for (var channel = 0; channel < format.Channels; channel++)
            {
                frame += ReadSample(args.Buffer, offset + channel * bytesPerSample, format);
            }

            _sampleBuffer[_sampleCount++] = float.IsFinite(frame) ? Math.Clamp(frame / format.Channels, -1f, 1f) : 0f;
            if (_sampleCount == FftLength)
            {
                PublishSpectrum(format.SampleRate);
                Array.Copy(_sampleBuffer, HopLength, _sampleBuffer, 0, HopLength);
                _sampleCount = HopLength;
            }
        }
    }

    /// <summary>Publishes independent music-band peaks with square-root compression for visible quiet detail.</summary>
    private void PublishSpectrum(int sampleRate)
    {
        for (var index = 0; index < FftLength; index++)
        {
            var window = 0.5f - 0.5f * MathF.Cos(2f * MathF.PI * index / (FftLength - 1));
            _fftBuffer[index].X = _sampleBuffer[index] * window;
            _fftBuffer[index].Y = 0;
        }

        FastFourierTransform.FFT(true, FftOrder, _fftBuffer);
        var spectrum = new float[FrequencyBands.Length];
        var binWidth = sampleRate / (float)FftLength;
        for (var bandIndex = 0; bandIndex < FrequencyBands.Length; bandIndex++)
        {
            // Half-open frequency intervals assign each bin to only one music bar.
            var minBin = Math.Max(1, (int)MathF.Ceiling(FrequencyBands[bandIndex].Min / binWidth));
            var maxBin = Math.Min(FftLength / 2, (int)MathF.Ceiling(FrequencyBands[bandIndex].Max / binWidth) - 1);
            var peak = 0f;
            for (var bin = minBin; bin <= maxBin; bin++)
            {
                // NAudio's forward FFT already divides by FftLength. Dividing again
                // suppresses quiet signals and previously triggered the flat RMS fallback.
                var magnitude = MathF.Sqrt(
                    _fftBuffer[bin].X * _fftBuffer[bin].X +
                    _fftBuffer[bin].Y * _fftBuffer[bin].Y);
                peak = Math.Max(peak, magnitude);
            }

            // A shared curve lifts weaker mid/treble detail while preserving band
            // ordering and absolute silence; it never invents energy for an empty band.
            spectrum[bandIndex] = MathF.Sqrt(Math.Clamp(peak * 32f, 0f, 1f));
        }

        SpectrumChanged?.Invoke(this, spectrum);
    }

    private static float ReadSample(byte[] buffer, int offset, WaveFormat format)
    {
        var isExtensibleFloat = format is WaveFormatExtensible extensible && extensible.SubFormat == IeeeFloatSubFormat;
        if (format.BitsPerSample >= 32 &&
            (format.Encoding == WaveFormatEncoding.IeeeFloat || isExtensibleFloat))
        {
            return BitConverter.ToSingle(buffer, offset);
        }

        return format.BitsPerSample switch
        {
            16 => BitConverter.ToInt16(buffer, offset) / 32768f,
            24 => Read24BitSample(buffer, offset),
            32 => BitConverter.ToInt32(buffer, offset) / 2147483648f,
            _ => 0f
        };
    }

    private static float Read24BitSample(byte[] buffer, int offset)
    {
        var sample = buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16);
        if ((sample & 0x800000) != 0)
        {
            sample |= unchecked((int)0xFF000000);
        }

        return sample / 8388608f;
    }

    public void Dispose()
    {
        lock (_lifecycleLock)
        {
            if (_disposed) return;
            _disposed = true;
            if (_deviceEnumerator is not null)
            {
                _deviceEnumerator.UnregisterEndpointNotificationCallback(this);
                _deviceEnumerator.Dispose();
                _deviceEnumerator = null;
            }
            ReleaseCapture();
        }
    }
}

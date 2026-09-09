using WindowsDynamicIsland.Models;
using Windows.Media.Control;

namespace WindowsDynamicIsland.Services;

public sealed class MediaSessionService
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private int _refreshVersion;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    public event EventHandler<MediaSnapshot?>? SnapshotChanged;

    /// <summary>Reads progress independently of artwork so timer updates do not reload the cover.</summary>
    public MediaProgress? GetProgress()
    {
        try
        {
            var session = _session;
            if (session is null) return null;
            var timeline = session.GetTimelineProperties();
            var playback = session.GetPlaybackInfo();
            if (timeline is null || playback is null) return null;
            return MediaProgress.Create(timeline.StartTime, timeline.EndTime, timeline.Position,
                timeline.LastUpdatedTime, DateTimeOffset.UtcNow,
                playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                playback.PlaybackRate ?? 1d);
        }
        catch (Exception)
        {
            // Providers can omit their timeline or disappear during a read.
            return null;
        }
    }

    public async Task InitializeAsync()
    {
        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        _manager.CurrentSessionChanged += OnCurrentSessionChanged;
        await RefreshAsync();
    }

    public async Task TogglePlayPauseAsync()
    {
        var session = _session;
        if (session is null)
        {
            return;
        }

        var playback = session.GetPlaybackInfo();
        if (playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
        {
            await session.TryPauseAsync();
        }
        else
        {
            await session.TryPlayAsync();
        }

        await RefreshAsync();
    }

    private async void OnCurrentSessionChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        CurrentSessionChangedEventArgs args)
    {
        await RefreshAsync();
    }

    private async void OnSessionChanged(
        GlobalSystemMediaTransportControlsSession sender,
        object args)
    {
        await RefreshAsync();
    }

    /// <summary>Serializes session subscriptions across provider callbacks. Invalidate older
    /// reads before waiting so a slow provider cannot publish over a newer session change.</summary>
    private async Task RefreshAsync()
    {
        var version = Interlocked.Increment(ref _refreshVersion);
        await _refreshGate.WaitAsync();
        try
        {
            if (version != Volatile.Read(ref _refreshVersion)) return;
            await RefreshSessionAsync(version);
        }
        catch (Exception)
        {
            // A provider can disappear during discovery or event registration too,
            // before the asynchronous media-property read has even started.
            if (version == Volatile.Read(ref _refreshVersion))
                SnapshotChanged?.Invoke(this, null);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task RefreshSessionAsync(int version)
    {
        if (_manager is null)
        {
            return;
        }

        var previousSession = _session;
        if (previousSession is not null)
        {
            previousSession.MediaPropertiesChanged -= OnSessionChanged;
            previousSession.PlaybackInfoChanged -= OnSessionChanged;
        }

        var session = _manager.GetCurrentSession();
        _session = session;
        if (session is null)
        {
            SnapshotChanged?.Invoke(this, null);
            return;
        }

        session.MediaPropertiesChanged += OnSessionChanged;
        session.PlaybackInfoChanged += OnSessionChanged;

        try
        {
            var properties = await session.TryGetMediaPropertiesAsync();
            // A previous provider read must not replace a newer track or session.
            if (version != Volatile.Read(ref _refreshVersion))
            {
                return;
            }
            var playback = session.GetPlaybackInfo();
            if (properties is null || playback is null)
            {
                SnapshotChanged?.Invoke(this, null);
                return;
            }

            var title = string.IsNullOrWhiteSpace(properties.Title) ? "Unknown title" : properties.Title;
            var artist = string.IsNullOrWhiteSpace(properties.Artist) ? "Unknown artist" : properties.Artist;
            var source = string.IsNullOrWhiteSpace(session.SourceAppUserModelId)
                ? "Media"
                : session.SourceAppUserModelId.Split('!')[0];

            SnapshotChanged?.Invoke(this, new MediaSnapshot(
                title,
                artist,
                source,
                playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                properties.Thumbnail));
        }
        catch (Exception)
        {
            // Media providers may disappear between obtaining a session and reading it.
            if (version == Volatile.Read(ref _refreshVersion))
            {
                SnapshotChanged?.Invoke(this, null);
            }
        }
    }
}

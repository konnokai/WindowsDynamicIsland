using WindowsDynamicIsland.Models;
using Windows.Media.Control;

namespace WindowsDynamicIsland.Services;

public sealed class MediaSessionService
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private int _refreshVersion;

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
        if (_session is null)
        {
            return;
        }

        var playback = _session.GetPlaybackInfo();
        if (playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
        {
            await _session.TryPauseAsync();
        }
        else
        {
            await _session.TryPlayAsync();
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

    private async Task RefreshAsync()
    {
        var version = Interlocked.Increment(ref _refreshVersion);
        if (_manager is null)
        {
            return;
        }

        if (_session is not null)
        {
            _session.MediaPropertiesChanged -= OnSessionChanged;
            _session.PlaybackInfoChanged -= OnSessionChanged;
        }

        _session = _manager.GetCurrentSession();
        if (_session is null)
        {
            SnapshotChanged?.Invoke(this, null);
            return;
        }

        _session.MediaPropertiesChanged += OnSessionChanged;
        _session.PlaybackInfoChanged += OnSessionChanged;
        var session = _session;

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

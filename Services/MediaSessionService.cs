using WindowsDynamicIsland.Models;
using Windows.Media.Control;

namespace WindowsDynamicIsland.Services;

public sealed class MediaSessionService
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;

    public event EventHandler<MediaSnapshot?>? SnapshotChanged;

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

        try
        {
            var properties = await _session.TryGetMediaPropertiesAsync();
            var playback = _session.GetPlaybackInfo();
            if (properties is null || playback is null)
            {
                SnapshotChanged?.Invoke(this, null);
                return;
            }

            var title = string.IsNullOrWhiteSpace(properties.Title) ? "Unknown title" : properties.Title;
            var artist = string.IsNullOrWhiteSpace(properties.Artist) ? "Unknown artist" : properties.Artist;
            var source = string.IsNullOrWhiteSpace(_session.SourceAppUserModelId)
                ? "Media"
                : _session.SourceAppUserModelId.Split('!')[0];

            SnapshotChanged?.Invoke(this, new MediaSnapshot(
                title,
                artist,
                source,
                playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing));
        }
        catch (Exception)
        {
            // Media providers may disappear between obtaining a session and reading it.
            SnapshotChanged?.Invoke(this, null);
        }
    }
}

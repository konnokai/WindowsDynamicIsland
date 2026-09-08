using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using WindowsDynamicIsland.Models;
using WindowsDynamicIsland.Services;

namespace WindowsDynamicIsland.ViewModels;

/// <summary>Combines system activity into the small state surface shown by the island.</summary>
public sealed class IslandViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly MediaSessionService _media;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly DispatcherQueueTimer _progressTimer;
    private readonly PowerService _power;
    private MediaProgress? _mediaProgress;
    private bool _isExpanded;
    private MediaSnapshot? _mediaSnapshot;
    private BitmapImage? _mediaCover;
    private PowerSnapshot? _powerSnapshot;
    private AgentNotification? _openCodeNotification;

    public event PropertyChangedEventHandler? PropertyChanged;

    public IslandViewModel(MediaSessionService media, PowerService power, DispatcherQueue dispatcherQueue)
    {
        _media = media;
        _dispatcherQueue = dispatcherQueue;
        _power = power;
        _progressTimer = dispatcherQueue.CreateTimer();
        // The displayed time has second precision; artwork is not refreshed on this timer.
        _progressTimer.Interval = TimeSpan.FromSeconds(1);
        _progressTimer.Tick += OnProgressTick;
        media.SnapshotChanged += OnMediaSnapshotChanged;
        power.SnapshotChanged += OnPowerSnapshotChanged;
        ToggleExpandedCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
        TogglePlaybackCommand = new AsyncRelayCommand(_media.TogglePlayPauseAsync);
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
            {
                return;
            }

            _isExpanded = value;
            OnPropertyChanged();
        }
    }

    public MediaSnapshot? MediaSnapshot
    {
        get => _mediaSnapshot;
        private set
        {
            _mediaSnapshot = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasMedia));
            OnPropertyChanged(nameof(MediaTitle));
            OnPropertyChanged(nameof(MediaArtist));
            OnPropertyChanged(nameof(MediaSource));
            OnPropertyChanged(nameof(PlaybackGlyph));
        }
    }

    public PowerSnapshot? PowerSnapshot
    {
        get => _powerSnapshot;
        private set
        {
            _powerSnapshot = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasPower));
            OnPropertyChanged(nameof(PowerText));
        }
    }

    public AgentNotification? AgentNotification
    {
        get => _openCodeNotification;
        private set
        {
            _openCodeNotification = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasAgentNotification));
            OnPropertyChanged(nameof(AgentTitle));
            OnPropertyChanged(nameof(AgentMessage));
            OnPropertyChanged(nameof(AgentGlyph));
            OnPropertyChanged(nameof(HasAgentQuestion));
            OnPropertyChanged(nameof(AgentQuestion));
            OnPropertyChanged(nameof(AgentOptions));
        }
    }

    public bool HasMedia => MediaSnapshot is not null;
    public string MediaProgressText => _mediaProgress?.Text ?? string.Empty;
    public double MediaProgressPercent => _mediaProgress?.Percent ?? 0;
    public Visibility MediaProgressVisibility => _mediaProgress is null ? Visibility.Collapsed : Visibility.Visible;

    private void OnProgressTick(DispatcherQueueTimer sender, object args) => UpdateProgress();

    private void UpdateProgress()
    {
        _mediaProgress = HasMedia ? _media.GetProgress() : null;
        OnPropertyChanged(nameof(MediaProgressText));
        OnPropertyChanged(nameof(MediaProgressPercent));
        OnPropertyChanged(nameof(MediaProgressVisibility));
    }

    public void Dispose()
    {
        _progressTimer.Stop();
        _progressTimer.Tick -= OnProgressTick;
        _media.SnapshotChanged -= OnMediaSnapshotChanged;
        _power.SnapshotChanged -= OnPowerSnapshotChanged;
    }
    public BitmapImage? MediaCover
    {
        get => _mediaCover;
        private set
        {
            _mediaCover = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MediaCoverPlaceholderVisibility));
        }
    }
    public Visibility MediaCoverPlaceholderVisibility => MediaCover is null ? Visibility.Visible : Visibility.Collapsed;
    public bool HasPower => PowerSnapshot is not null;
    public string MediaTitle => MediaSnapshot?.Title ?? "No media session";
    public string MediaArtist => MediaSnapshot?.Artist ?? "Open a supported player to begin";
    public string MediaSource => MediaSnapshot?.Source ?? "Dynamic Island";
    public string PlaybackGlyph => MediaSnapshot?.IsPlaying == true ? "\uE769" : "\uE768";
    public string PowerText => PowerSnapshot is null
        ? string.Empty
        : $"{PowerSnapshot.ChargePercent}%{(PowerSnapshot.IsPluggedIn ? " · plugged in" : string.Empty)}";
    public bool HasAgentNotification => AgentNotification is not null;
    public string AgentTitle => AgentNotification?.Title ?? "Agent";
    public string AgentMessage => AgentNotification?.Message ?? string.Empty;
    public string AgentGlyph => AgentNotification?.Glyph ?? "\uE768";
    public bool HasAgentQuestion => AgentNotification is { Source: "OpenCode", RequestId: not null } && AgentOptions.Count > 0;
    public string AgentQuestion => AgentNotification?.Question ?? AgentMessage;
    public IReadOnlyList<string> AgentOptions => AgentNotification?.Options ?? Array.Empty<string>();

    public ICommand ToggleExpandedCommand { get; }
    public ICommand TogglePlaybackCommand { get; }

    public void UpdateAgentNotification(AgentNotification? notification) => AgentNotification = notification;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    // GSMTC and power callbacks are not guaranteed to run on the XAML thread.
    private void OnMediaSnapshotChanged(object? sender, MediaSnapshot? snapshot) =>
        _dispatcherQueue.TryEnqueue(async () =>
        {
            MediaSnapshot = snapshot;
            UpdateProgress();
            if (HasMedia) _progressTimer.Start();
            else _progressTimer.Stop();
            await LoadMediaCoverAsync(snapshot);
        });

    /// <summary>Decodes cover art on the UI thread and discards results from a superseded snapshot.</summary>
    private async Task LoadMediaCoverAsync(MediaSnapshot? snapshot)
    {
        MediaCover = null;
        if (snapshot?.Thumbnail is not { } thumbnail)
        {
            return;
        }

        try
        {
            using var stream = await thumbnail.OpenReadAsync();
            var image = new BitmapImage();
            await image.SetSourceAsync(stream);
            if (ReferenceEquals(MediaSnapshot, snapshot))
            {
                MediaCover = image;
            }
        }
        catch (Exception)
        {
            // Missing or invalid provider artwork keeps the music placeholder visible.
        }
    }

    private void OnPowerSnapshotChanged(object? sender, PowerSnapshot? snapshot) =>
        _dispatcherQueue.TryEnqueue(() => PowerSnapshot = snapshot);

    private sealed class RelayCommand(Action execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => execute();
    }

    private sealed class AsyncRelayCommand(Func<Task> execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public async void Execute(object? parameter) => await execute();
    }
}

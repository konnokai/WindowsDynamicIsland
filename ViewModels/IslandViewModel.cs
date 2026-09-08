using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.UI.Dispatching;
using WindowsDynamicIsland.Models;
using WindowsDynamicIsland.Services;

namespace WindowsDynamicIsland.ViewModels;

/// <summary>Combines system activity into the small state surface shown by the island.</summary>
public sealed class IslandViewModel : INotifyPropertyChanged
{
    private readonly MediaSessionService _media;
    private readonly DispatcherQueue _dispatcherQueue;
    private bool _isExpanded;
    private MediaSnapshot? _mediaSnapshot;
    private PowerSnapshot? _powerSnapshot;
    private OpenCodeNotification? _openCodeNotification;

    public event PropertyChangedEventHandler? PropertyChanged;

    public IslandViewModel(MediaSessionService media, PowerService power, DispatcherQueue dispatcherQueue)
    {
        _media = media;
        _dispatcherQueue = dispatcherQueue;
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

    public OpenCodeNotification? OpenCodeNotification
    {
        get => _openCodeNotification;
        private set
        {
            _openCodeNotification = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasOpenCodeNotification));
            OnPropertyChanged(nameof(OpenCodeTitle));
            OnPropertyChanged(nameof(OpenCodeMessage));
            OnPropertyChanged(nameof(OpenCodeGlyph));
            OnPropertyChanged(nameof(IsOpenCodeVisualizerActive));
            OnPropertyChanged(nameof(HasOpenCodeQuestion));
            OnPropertyChanged(nameof(OpenCodeQuestion));
            OnPropertyChanged(nameof(OpenCodeOptions));
        }
    }

    public bool HasMedia => MediaSnapshot is not null;
    public bool HasPower => PowerSnapshot is not null;
    public string MediaTitle => MediaSnapshot?.Title ?? "No media session";
    public string MediaArtist => MediaSnapshot?.Artist ?? "Open a supported player to begin";
    public string MediaSource => MediaSnapshot?.Source ?? "Dynamic Island";
    public string PlaybackGlyph => MediaSnapshot?.IsPlaying == true ? "\uE769" : "\uE768";
    public string PowerText => PowerSnapshot is null
        ? string.Empty
        : $"{PowerSnapshot.ChargePercent}%{(PowerSnapshot.IsPluggedIn ? " · plugged in" : string.Empty)}";
    public bool HasOpenCodeNotification => OpenCodeNotification is not null;
    public string OpenCodeTitle => OpenCodeNotification?.Title ?? "OpenCode";
    public string OpenCodeMessage => OpenCodeNotification?.Message ?? string.Empty;
    public string OpenCodeGlyph => OpenCodeNotification?.Glyph ?? "\uE768";
    public bool IsOpenCodeVisualizerActive => OpenCodeNotification?.IsVisualizerActive == true;
    public bool HasOpenCodeQuestion => OpenCodeNotification?.RequestId is not null && OpenCodeOptions.Count > 0;
    public string OpenCodeQuestion => OpenCodeNotification?.Question ?? OpenCodeMessage;
    public IReadOnlyList<string> OpenCodeOptions => OpenCodeNotification?.Options ?? Array.Empty<string>();

    public ICommand ToggleExpandedCommand { get; }
    public ICommand TogglePlaybackCommand { get; }

    public void UpdateOpenCodeNotification(OpenCodeNotification? notification) => OpenCodeNotification = notification;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    // GSMTC and power callbacks are not guaranteed to run on the XAML thread.
    private void OnMediaSnapshotChanged(object? sender, MediaSnapshot? snapshot) =>
        _dispatcherQueue.TryEnqueue(() => MediaSnapshot = snapshot);

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

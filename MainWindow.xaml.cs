using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Graphics;
using WindowsDynamicIsland.Models;
using WindowsDynamicIsland.Services;
using WindowsDynamicIsland.ViewModels;
using WinRT.Interop;

namespace WindowsDynamicIsland;

public sealed partial class MainWindow : Window
{
    private readonly AppWindow _appWindow;
    private readonly DisplayAreaWatcher _displayAreaWatcher;
    private bool _applyingIslandBounds;
    private bool _placementQueued;
    private bool _windowClosed;
    private bool _islandShown;
    private SizeInt32 _requestedIslandSize;
    private PointInt32 _appliedIslandPosition;
    private SizeInt32 _appliedIslandSize;
    private readonly IslandViewModel _viewModel;
    private readonly MediaSessionService _mediaService;
    private readonly PowerService _powerService;
    private readonly TrayIconService _trayIconService;
    private readonly OpenCodeNotificationService _openCodeService;
    private readonly CodexNotificationService _codexService;
    private readonly AgentNotificationQueue _notifications = new();
    private readonly SystemAudioLevelService _systemAudioService;
    private readonly nint _windowHandle;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly DispatcherQueueTimer _openCodeDismissTimer;
    private readonly DispatcherQueueTimer _islandAnimationTimer;
    private readonly ScaleTransform[] _visualizerTransforms;
    private readonly DispatcherQueueTimer _visualizerTimer;
    private PointInt32 _animationStartPosition;
    private PointInt32 _animationTargetPosition;
    private SizeInt32 _animationStartSize;
    private SizeInt32 _animationTargetSize;
    private DateTimeOffset _animationStartedAt;
    private float[] _systemAudioBands = new float[5];
    private bool _openCodeNotificationVisible;
    private readonly DispatcherQueueTimer _mediaCollapseTimer;
    private readonly DispatcherQueueTimer _mediaHoverTimer;
    private bool _pointerInside;
    private MediaSnapshot? _lastMediaSnapshot;
    private bool _mediaExpandedBeforeNotification;
    private bool _temporarilyHidden;
    private bool _fullscreenHidden;
    private bool _isSuppressed;
    private readonly FullscreenAvoidanceService _avoidanceService;

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;

        _windowHandle = WindowNative.GetWindowHandle(this);
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _mediaCollapseTimer = _dispatcherQueue.CreateTimer();
        _mediaCollapseTimer.IsRepeating = false;
        _mediaCollapseTimer.Interval = TimeSpan.FromSeconds(4);
        _mediaCollapseTimer.Tick += OnMediaCollapseTick;
        _mediaHoverTimer = _dispatcherQueue.CreateTimer();
        _mediaHoverTimer.IsRepeating = false;
        _mediaHoverTimer.Interval = TimeSpan.FromSeconds(1);
        _mediaHoverTimer.Tick += OnMediaHoverTick;
        _islandAnimationTimer = _dispatcherQueue.CreateTimer();
        _islandAnimationTimer.Interval = TimeSpan.FromMilliseconds(16);
        _islandAnimationTimer.Tick += OnIslandAnimationTick;
        _visualizerTimer = _dispatcherQueue.CreateTimer();
        _visualizerTimer.Interval = TimeSpan.FromMilliseconds(16);
        _visualizerTimer.Tick += OnVisualizerTick;
        var windowId = Win32Interop.GetWindowIdFromWindow(_windowHandle);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        ConfigureWindow();
        _appWindow.Changed += OnAppWindowChanged;
        _displayAreaWatcher = DisplayArea.CreateWatcher();
        _displayAreaWatcher.Added += (_, _) => QueuePlacementRestore();
        _displayAreaWatcher.Removed += (_, _) => QueuePlacementRestore();
        _displayAreaWatcher.Updated += (_, _) => QueuePlacementRestore();
        _displayAreaWatcher.Start();
        // WinUI can restore WS_EX_WINDOWEDGE when activating the HWND.
        Activated += (_, _) => WindowChromeService.HideSystemBorder(_windowHandle);

        _mediaService = new MediaSessionService();
        _powerService = new PowerService();
        _trayIconService = new TrayIconService(this, _windowHandle);
        _trayIconService.Initialize();
        _trayIconService.ToggleVisibilityRequested += (_, _) => SetTemporarilyHidden(!_temporarilyHidden);
        _trayIconService.RestoreRequested += (_, _) => SetTemporarilyHidden(false);
        _avoidanceService = new FullscreenAvoidanceService(_windowHandle);
        _avoidanceService.SuppressionChanged += (_, hidden) => _dispatcherQueue.TryEnqueue(() =>
        {
            _fullscreenHidden = hidden;
            UpdateSuppression();
        });
        _openCodeService = new OpenCodeNotificationService();
        _openCodeService.NotificationRaised += OnAgentNotification;
        _openCodeService.QuestionResolved += OnQuestionResolved;
        _codexService = new CodexNotificationService();
        _codexService.NotificationRaised += OnAgentNotification;
        _systemAudioService = new SystemAudioLevelService();
        _systemAudioService.SpectrumChanged += OnSystemAudioSpectrumChanged;
        _openCodeDismissTimer = _dispatcherQueue.CreateTimer();
        _openCodeDismissTimer.IsRepeating = false;
        _openCodeDismissTimer.Interval = TimeSpan.FromSeconds(4);
        _openCodeDismissTimer.Tick += (_, _) => HideAgentNotification();
        _visualizerTransforms =
        [
            MediaVisualizerBar1Transform,
            MediaVisualizerBar2Transform,
            MediaVisualizerBar3Transform,
            MediaVisualizerBar4Transform,
            MediaVisualizerBar5Transform,
            CollapsedVisualizerBar1Transform,
            CollapsedVisualizerBar2Transform,
            CollapsedVisualizerBar3Transform,
            CollapsedVisualizerBar4Transform,
            CollapsedVisualizerBar5Transform
        ];
        _viewModel = new IslandViewModel(_mediaService, _powerService, _dispatcherQueue);
        _viewModel.PropertyChanged += ViewModelOnPropertyChanged;
        IslandRoot.DataContext = _viewModel;
        Closed += OnWindowClosed;
    }

    private void ConfigureWindow()
    {
        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        presenter.SetBorderAndTitleBar(false, false);
        _appWindow.SetPresenter(presenter);
        _appWindow.IsShownInSwitchers = false;
        _appWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico"));
        WindowChromeService.HideSystemBorder(_windowHandle);
        MoveIsland(244, 80, false);
    }

    private async void OnWindowLoaded(object sender, RoutedEventArgs args)
    {
        _powerService.Initialize();
        await _mediaService.InitializeAsync();
        _systemAudioService.Start();
        _openCodeService.Start();
        _codexService.Start();
        _avoidanceService.Start();
    }

    private void ViewModelOnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(IslandViewModel.MediaSnapshot))
        {
            var snapshot = _viewModel.MediaSnapshot;
            var trackChanged = snapshot is not null &&
                (_lastMediaSnapshot is null || snapshot.Title != _lastMediaSnapshot.Title ||
                 snapshot.Artist != _lastMediaSnapshot.Artist || snapshot.Source != _lastMediaSnapshot.Source);
            _lastMediaSnapshot = snapshot;
            if (_openCodeNotificationVisible)
            {
                return;
            }

            if (trackChanged)
            {
                _viewModel.IsExpanded = true;
                RestartMediaCollapseTimer();
            }
            else if (snapshot is null)
            {
                _mediaCollapseTimer.Stop();
                _mediaHoverTimer.Stop();
                _viewModel.IsExpanded = false;
            }
            if (trackChanged || snapshot is null) ApplyMediaLayout();
            UpdateVisualizer();
            return;
        }

        if (args.PropertyName == nameof(IslandViewModel.AgentNotification))
        {
            if (_viewModel.HasAgentNotification)
            {
                ShowAgentNotification();
            }

            return;
        }

        if (args.PropertyName != nameof(IslandViewModel.IsExpanded))
        {
            return;
        }

        if (_openCodeNotificationVisible) return;
        ApplyMediaLayout();
        if (_viewModel.IsExpanded) RestartMediaCollapseTimer();
        else _mediaCollapseTimer.Stop();
    }

    /// <summary>Keeps the compact media window visible and sizes its native hit area to its content.</summary>
    private void ApplyMediaLayout()
    {
        if (_viewModel.IsExpanded)
        {
            CollapsedPanel.Visibility = Visibility.Collapsed;
            ExpandedPanel.Visibility = Visibility.Visible;
            AgentPanel.Visibility = Visibility.Collapsed;
            MoveIsland(516, 96, true);
        }
        else
        {
            CollapsedPanel.Visibility = Visibility.Visible;
            ExpandedPanel.Visibility = Visibility.Collapsed;
            AgentPanel.Visibility = Visibility.Collapsed;
            var padding = IslandSurface.Padding;
            MoveIsland((int)(CollapsedPanel.Width + padding.Left + padding.Right),
                (int)(CollapsedPanel.Height + padding.Top + padding.Bottom), _viewModel.HasMedia);
        }
    }

    /// <summary>Hover owns the expanded panel until exit; media refreshes cannot extend the idle deadline.</summary>
    private void RestartMediaCollapseTimer()
    {
        _mediaCollapseTimer.Stop();
        if (!_isSuppressed && !_pointerInside && !_openCodeNotificationVisible && _viewModel.HasMedia)
            _mediaCollapseTimer.Start();
    }

    private void OnIslandPointerEntered(object sender, PointerRoutedEventArgs args)
    {
        _pointerInside = true;
        _mediaCollapseTimer.Stop();
        if (!_openCodeNotificationVisible && _viewModel.HasMedia && !_viewModel.IsExpanded)
            _mediaHoverTimer.Start();
    }

    private void OnIslandPointerPressed(object sender, PointerRoutedEventArgs args)
    {
        if (_isSuppressed || _openCodeNotificationVisible || !_viewModel.HasMedia ||
            _viewModel.IsExpanded || !args.GetCurrentPoint(IslandRoot).Properties.IsLeftButtonPressed)
            return;

        _mediaHoverTimer.Stop();
        _viewModel.IsExpanded = true;
        args.Handled = true;
    }

    private void OnMediaCollapseTick(DispatcherQueueTimer sender, object args)
    {
        if (!_openCodeNotificationVisible && !_pointerInside)
            _viewModel.IsExpanded = false;
    }

    private void OnMediaHoverTick(DispatcherQueueTimer sender, object args)
    {
        if (_pointerInside && !_openCodeNotificationVisible && _viewModel.HasMedia)
            _viewModel.IsExpanded = true;
    }

    private void OnIslandPointerExited(object sender, PointerRoutedEventArgs args)
    {
        _pointerInside = false;
        _mediaHoverTimer.Stop();
        if (_viewModel.IsExpanded) RestartMediaCollapseTimer();
    }

    private void OnAgentNotification(object? sender, AgentNotification notification)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            _notifications.Update(notification);
            RefreshAgentNotification();
        });
    }

    private void OnQuestionResolved(object? sender, string requestId)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            _notifications.ResolveQuestion("OpenCode", requestId);
            RefreshAgentNotification();
        });
    }

    private void RefreshAgentNotification()
    {
        // Repeated working hooks must not reset the current panel's dismiss timer.
        if (_viewModel.AgentNotification == _notifications.Current) return;
        if (_notifications.Current is { } current)
            _viewModel.UpdateAgentNotification(current);
        else
            HideAgentNotification();
    }

    private void ShowAgentNotification()
    {
        var keepDismissDeadline = _openCodeDismissTimer.IsRunning &&
            _viewModel.AgentNotification?.RequiresAttention != true;
        if (!_openCodeNotificationVisible)
            _mediaExpandedBeforeNotification = _viewModel.IsExpanded;
        _mediaCollapseTimer.Stop();
        _mediaHoverTimer.Stop();
        if (!keepDismissDeadline) _openCodeDismissTimer.Stop();
        _openCodeNotificationVisible = true;
        var isQuestion = _viewModel.HasAgentQuestion;
        CollapsedPanel.Visibility = Visibility.Collapsed;
        ExpandedPanel.Visibility = Visibility.Collapsed;
        AgentPanel.Visibility = Visibility.Visible;
        QuestionOptionsList.Visibility = isQuestion ? Visibility.Visible : Visibility.Collapsed;
        AgentPanel.Width = isQuestion ? 460 : 420;
        AgentPanel.MinHeight = isQuestion ? 144 : 72;
        UpdateVisualizer();
        ResizeToAgentNotification();

        if (!keepDismissDeadline && !_isSuppressed && _viewModel.AgentNotification?.RequiresAttention != true)
        {
            _openCodeDismissTimer.Start();
        }
    }

    /// <summary>Measures unconstrained content so text descenders and row gaps fit inside the native window.</summary>
    private void ResizeToAgentNotification()
    {
        // Include the surface padding in the HWND size; fixed panel heights can clip
        // the second line when font metrics or Windows text scaling increase its height.
        AgentPanel.Measure(new Windows.Foundation.Size(AgentPanel.Width, double.PositiveInfinity));
        var padding = IslandSurface.Padding;
        MoveIsland(
            (int)Math.Ceiling(AgentPanel.DesiredSize.Width + padding.Left + padding.Right),
            (int)Math.Ceiling(AgentPanel.DesiredSize.Height + padding.Top + padding.Bottom),
            true);
    }

    private void HideAgentNotification()
    {
        if (_viewModel.AgentNotification is { } notification)
            _notifications.Dismiss(notification);
        if (_notifications.Current is { } next)
        {
            _viewModel.UpdateAgentNotification(next);
            return;
        }
        _openCodeDismissTimer.Stop();
        AgentIconSurface.Visibility = Visibility.Visible;
        _openCodeNotificationVisible = false;
        _viewModel.UpdateAgentNotification(null);

        _viewModel.IsExpanded = _viewModel.HasMedia && _mediaExpandedBeforeNotification;
        ApplyMediaLayout();
        RestartMediaCollapseTimer();
        UpdateVisualizer();
    }

    private void OnDismissNotificationClick(object sender, RoutedEventArgs args) => HideAgentNotification();

    private void SetTemporarilyHidden(bool hidden)
    {
        _temporarilyHidden = hidden;
        _trayIconService.IsTemporarilyHidden = hidden;
        UpdateSuppression();
    }

    /// <summary>Hides the HWND without losing pending questions or the media layout; restoration never takes focus.</summary>
    private void UpdateSuppression()
    {
        var suppressed = _temporarilyHidden || _fullscreenHidden;
        if (_isSuppressed == suppressed) return;
        _isSuppressed = suppressed;
        if (suppressed)
        {
            _pointerInside = false;
            _mediaHoverTimer.Stop();
            _mediaCollapseTimer.Stop();
            _openCodeDismissTimer.Stop();
            _islandAnimationTimer.Stop();
            _appWindow.Hide();
        }
        else
        {
            if (_openCodeNotificationVisible) ShowAgentNotification();
            else
            {
                ApplyMediaLayout();
                if (_viewModel.IsExpanded) RestartMediaCollapseTimer();
            }
            _appWindow.Show(false);
        }
        UpdateVisualizer();
    }

    private async void OnQuestionOptionClick(object sender, RoutedEventArgs args)
    {
        if (sender is not Button button || button.Content is not string answer ||
            _viewModel.AgentNotification is not { Source: "OpenCode", RequestId: not null } notification)
        {
            return;
        }

        try
        {
            await _openCodeService.ReplyToQuestionAsync(notification, answer);
            _notifications.ResolveQuestion("OpenCode", notification.RequestId);
            RefreshAgentNotification();
        }
        catch (HttpRequestException)
        {
            // Keep the question visible so the user can retry after the server recovers.
        }
    }

    private void MoveIsland(int width, int height, bool show, bool animate = true)
    {
        _requestedIslandSize = new SizeInt32(width, height);
        _islandShown = show;
        var workArea = GetWorkArea();
        var x = workArea.X + Math.Max(0, (workArea.Width - width) / 2);
        var y = show ? workArea.Y + 12 : workArea.Y - height + 12;
        var targetPosition = new PointInt32(x, y);
        var targetSize = new SizeInt32(width, height);
        if (!animate)
        {
            _islandAnimationTimer.Stop();
            ApplyIslandBounds(targetPosition, targetSize);
            return;
        }

        _animationStartPosition = _appWindow.Position;
        _animationTargetPosition = targetPosition;
        _animationStartSize = _appWindow.Size;
        _animationTargetSize = targetSize;
        _animationStartedAt = DateTimeOffset.UtcNow;
        PlaySurfaceAnimation(show);
        _islandAnimationTimer.Start();
    }

    private void OnIslandAnimationTick(DispatcherQueueTimer sender, object args)
    {
        var progress = Math.Clamp((DateTimeOffset.UtcNow - _animationStartedAt).TotalMilliseconds / 220d, 0d, 1d);
        var eased = 1d - Math.Pow(1d - progress, 3d);
        var position = new PointInt32(
            Interpolate(_animationStartPosition.X, _animationTargetPosition.X, eased),
            Interpolate(_animationStartPosition.Y, _animationTargetPosition.Y, eased));
        var size = new SizeInt32(
            Interpolate(_animationStartSize.Width, _animationTargetSize.Width, eased),
            Interpolate(_animationStartSize.Height, _animationTargetSize.Height, eased));

        ApplyIslandBounds(position, size);
        if (progress >= 1d)
        {
            _islandAnimationTimer.Stop();
        }
    }

    /// <summary>Ignores our own moves while repairing Windows-initiated window relocation.</summary>
    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        var position = sender.Position;
        var size = sender.Size;
        // Also ignore delayed events whose bounds still match our latest animation frame.
        if (!_applyingIslandBounds && (args.DidPositionChange || args.DidSizeChange) &&
            (position.X != _appliedIslandPosition.X || position.Y != _appliedIslandPosition.Y ||
             size.Width != _appliedIslandSize.Width || size.Height != _appliedIslandSize.Height))
            QueuePlacementRestore();
    }

    /// <summary>Reanchors the current layout after display changes without changing visibility or notification state.</summary>
    private void QueuePlacementRestore()
    {
        if (_windowClosed || _placementQueued) return;
        _placementQueued = true;
        // Defer until the native change has completed. A later Windows relocation
        // raises Changed again, so recovery does not depend on a guessed delay.
        if (!_dispatcherQueue.TryEnqueue(() =>
        {
            _placementQueued = false;
            if (_windowClosed) return;
            MoveIsland(_requestedIslandSize.Width, _requestedIslandSize.Height, _islandShown, false);
        }))
            _placementQueued = false;
    }

    /// <summary>Applies bounds under a guard so synchronous change events cannot restart placement.</summary>
    private void ApplyIslandBounds(PointInt32 position, SizeInt32 size)
    {
        _applyingIslandBounds = true;
        _appliedIslandPosition = position;
        _appliedIslandSize = size;
        try
        {
            if (_appWindow.Size.Width != size.Width || _appWindow.Size.Height != size.Height)
                _appWindow.Resize(size);
            if (_appWindow.Position.X != position.X || _appWindow.Position.Y != position.Y)
                _appWindow.Move(position);
        }
        finally
        {
            _applyingIslandBounds = false;
        }
    }

    private void PlaySurfaceAnimation(bool show)
    {
        IslandSurface.Opacity = show ? 0.86 : 1;
        IslandScaleTransform.ScaleX = show ? 0.96 : 1;
        IslandScaleTransform.ScaleY = show ? 0.86 : 1;

        var storyboard = new Storyboard();
        var opacity = new DoubleAnimation
        {
            To = show ? 1 : 0.92,
            Duration = new Duration(TimeSpan.FromMilliseconds(220))
        };
        var scaleX = new DoubleAnimation
        {
            To = show ? 1 : 0.96,
            Duration = new Duration(TimeSpan.FromMilliseconds(220))
        };
        var scaleY = new DoubleAnimation
        {
            To = show ? 1 : 0.9,
            Duration = new Duration(TimeSpan.FromMilliseconds(220))
        };
        Storyboard.SetTarget(opacity, IslandSurface);
        Storyboard.SetTargetProperty(opacity, "Opacity");
        Storyboard.SetTarget(scaleX, IslandScaleTransform);
        Storyboard.SetTargetProperty(scaleX, "ScaleX");
        Storyboard.SetTarget(scaleY, IslandScaleTransform);
        Storyboard.SetTargetProperty(scaleY, "ScaleY");
        storyboard.Children.Add(opacity);
        storyboard.Children.Add(scaleX);
        storyboard.Children.Add(scaleY);
        storyboard.Begin();
    }

    private void OnSystemAudioSpectrumChanged(object? sender, float[] bands) =>
        Interlocked.Exchange(ref _systemAudioBands, bands);

    private void OnVisualizerTick(DispatcherQueueTimer sender, object args)
    {
        var bands = Volatile.Read(ref _systemAudioBands);
        for (var index = 0; index < _visualizerTransforms.Length; index++)
        {
            var band = bands[index % 5];
            var target = Math.Clamp(0.03f + band * 0.97f, 0.03f, 1f);
            var current = (float)_visualizerTransforms[index].ScaleY;
            _visualizerTransforms[index].ScaleY = current + (target - current) * 0.28f;
        }
    }

    private void UpdateVisualizer()
    {
        AgentIconSurface.Visibility = Visibility.Visible;
        var mediaActive = _viewModel.HasMedia && !_openCodeNotificationVisible && !_isSuppressed;
        MediaVisualizer.Visibility = mediaActive ? Visibility.Visible : Visibility.Collapsed;
        CollapsedMediaVisualizer.Visibility = mediaActive ? Visibility.Visible : Visibility.Collapsed;
        if (mediaActive)
            _visualizerTimer.Start();
        else
            _visualizerTimer.Stop();
    }

    private static int Interpolate(int start, int end, double progress) =>
        (int)Math.Round(start + (end - start) * progress);

    private RectInt32 GetWorkArea()
    {
        var windowId = Win32Interop.GetWindowIdFromWindow(_windowHandle);
        return DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary).WorkArea;
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _windowClosed = true;
        _displayAreaWatcher.Stop();
        _appWindow.Changed -= OnAppWindowChanged;
        _viewModel.PropertyChanged -= ViewModelOnPropertyChanged;
        _avoidanceService.Dispose();
        _viewModel.Dispose();
        _openCodeService.NotificationRaised -= OnAgentNotification;
        _openCodeService.QuestionResolved -= OnQuestionResolved;
        _openCodeService.Dispose();
        _codexService.NotificationRaised -= OnAgentNotification;
        _codexService.Dispose();
        _openCodeDismissTimer.Stop();
        _islandAnimationTimer.Stop();
        _visualizerTimer.Stop();
        _mediaCollapseTimer.Stop();
        _mediaHoverTimer.Stop();
        _systemAudioService.SpectrumChanged -= OnSystemAudioSpectrumChanged;
        _systemAudioService.Dispose();
        _trayIconService.Dispose();
    }
}

using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
    private readonly IslandViewModel _viewModel;
    private readonly MediaSessionService _mediaService;
    private readonly PowerService _powerService;
    private readonly TrayIconService _trayIconService;
    private readonly OpenCodeNotificationService _openCodeService;
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

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;

        _windowHandle = WindowNative.GetWindowHandle(this);
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _islandAnimationTimer = _dispatcherQueue.CreateTimer();
        _islandAnimationTimer.Interval = TimeSpan.FromMilliseconds(16);
        _islandAnimationTimer.Tick += OnIslandAnimationTick;
        _visualizerTimer = _dispatcherQueue.CreateTimer();
        _visualizerTimer.Interval = TimeSpan.FromMilliseconds(33);
        _visualizerTimer.Tick += OnVisualizerTick;
        var windowId = Win32Interop.GetWindowIdFromWindow(_windowHandle);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        ConfigureWindow();

        _mediaService = new MediaSessionService();
        _powerService = new PowerService();
        _trayIconService = new TrayIconService(this, _windowHandle);
        _trayIconService.Initialize();
        _openCodeService = new OpenCodeNotificationService();
        _openCodeService.NotificationRaised += OnOpenCodeNotification;
        _openCodeService.QuestionResolved += OnQuestionResolved;
        _systemAudioService = new SystemAudioLevelService();
        _systemAudioService.SpectrumChanged += OnSystemAudioSpectrumChanged;
        _openCodeDismissTimer = _dispatcherQueue.CreateTimer();
        _openCodeDismissTimer.IsRepeating = false;
        _openCodeDismissTimer.Interval = TimeSpan.FromSeconds(4);
        _openCodeDismissTimer.Tick += (_, _) => HideOpenCodeNotification();
        _visualizerTransforms =
        [
            VisualizerBar1Transform,
            VisualizerBar2Transform,
            VisualizerBar3Transform,
            VisualizerBar4Transform,
            VisualizerBar5Transform,
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
        MoveIsland(244, 80, false);
    }

    private async void OnWindowLoaded(object sender, RoutedEventArgs args)
    {
        _powerService.Initialize();
        await _mediaService.InitializeAsync();
        _systemAudioService.Start();
        _openCodeService.Start();
    }

    private void ViewModelOnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(IslandViewModel.MediaSnapshot))
        {
            if (_openCodeNotificationVisible)
            {
                return;
            }

            _viewModel.IsExpanded = _viewModel.HasMedia;
            UpdateVisualizer();
            return;
        }

        if (args.PropertyName == nameof(IslandViewModel.OpenCodeNotification))
        {
            if (_viewModel.HasOpenCodeNotification)
            {
                ShowOpenCodeNotification();
            }

            return;
        }

        if (args.PropertyName != nameof(IslandViewModel.IsExpanded))
        {
            return;
        }

        if (_viewModel.IsExpanded)
        {
            CollapsedPanel.Visibility = Visibility.Collapsed;
            ExpandedPanel.Visibility = Visibility.Visible;
            OpenCodePanel.Visibility = Visibility.Collapsed;
            MoveIsland(516, 96, true);
        }
        else
        {
            CollapsedPanel.Visibility = Visibility.Visible;
            ExpandedPanel.Visibility = Visibility.Collapsed;
            OpenCodePanel.Visibility = Visibility.Collapsed;
            MoveIsland(244, 80, false);
        }
    }

    private void OnOpenCodeNotification(object? sender, OpenCodeNotification notification)
    {
        _dispatcherQueue.TryEnqueue(() => _viewModel.UpdateOpenCodeNotification(notification));
    }

    private void OnQuestionResolved(object? sender, string requestId)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            if (_viewModel.OpenCodeNotification?.RequestId == requestId)
            {
                HideOpenCodeNotification();
            }
        });
    }

    private void ShowOpenCodeNotification()
    {
        _openCodeDismissTimer.Stop();
        _openCodeNotificationVisible = true;
        var isQuestion = _viewModel.HasOpenCodeQuestion;
        CollapsedPanel.Visibility = Visibility.Collapsed;
        ExpandedPanel.Visibility = Visibility.Collapsed;
        OpenCodePanel.Visibility = Visibility.Visible;
        QuestionOptionsList.Visibility = isQuestion ? Visibility.Visible : Visibility.Collapsed;
        OpenCodePanel.Width = isQuestion ? 460 : 420;
        OpenCodePanel.Height = isQuestion ? 144 : 72;
        UpdateVisualizer();
        MoveIsland(isQuestion ? 476 : 436, isQuestion ? 160 : 88, true);

        if (_viewModel.OpenCodeNotification?.RequiresAttention != true)
        {
            _openCodeDismissTimer.Start();
        }
    }

    private void HideOpenCodeNotification()
    {
        _openCodeDismissTimer.Stop();
        VoiceVisualizer.Visibility = Visibility.Collapsed;
        OpenCodeIconSurface.Visibility = Visibility.Visible;
        _openCodeNotificationVisible = false;
        _viewModel.UpdateOpenCodeNotification(null);

        if (_viewModel.HasMedia)
        {
            _viewModel.IsExpanded = true;
            if (_viewModel.IsExpanded)
            {
                CollapsedPanel.Visibility = Visibility.Collapsed;
                ExpandedPanel.Visibility = Visibility.Visible;
                OpenCodePanel.Visibility = Visibility.Collapsed;
                MoveIsland(516, 96, true);
            }
        }
        else
        {
            _viewModel.IsExpanded = false;
            CollapsedPanel.Visibility = Visibility.Visible;
            ExpandedPanel.Visibility = Visibility.Collapsed;
            OpenCodePanel.Visibility = Visibility.Collapsed;
            MoveIsland(244, 80, false);
        }
        UpdateVisualizer();
    }

    private async void OnQuestionOptionClick(object sender, RoutedEventArgs args)
    {
        if (sender is not Button button || button.Content is not string answer ||
            _viewModel.OpenCodeNotification is not { RequestId: not null } notification)
        {
            return;
        }

        try
        {
            await _openCodeService.ReplyToQuestionAsync(notification, answer);
            HideOpenCodeNotification();
        }
        catch (HttpRequestException)
        {
            // Keep the question visible so the user can retry after the server recovers.
        }
    }

    private void MoveIsland(int width, int height, bool show, bool animate = true)
    {
        var workArea = GetWorkArea();
        var x = workArea.X + Math.Max(0, (workArea.Width - width) / 2);
        var y = show ? workArea.Y + 12 : workArea.Y - height + 12;
        var targetPosition = new PointInt32(x, y);
        var targetSize = new SizeInt32(width, height);
        if (!animate)
        {
            _appWindow.Resize(targetSize);
            _appWindow.Move(targetPosition);
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

        _appWindow.Resize(size);
        _appWindow.Move(position);
        if (progress >= 1d)
        {
            _islandAnimationTimer.Stop();
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
        var active = _viewModel.IsOpenCodeVisualizerActive;
        VoiceVisualizer.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        OpenCodeIconSurface.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
        var mediaActive = _viewModel.HasMedia && !_openCodeNotificationVisible;
        MediaVisualizer.Visibility = mediaActive ? Visibility.Visible : Visibility.Collapsed;
        CollapsedMediaVisualizer.Visibility = mediaActive ? Visibility.Visible : Visibility.Collapsed;
        if (active || mediaActive)
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
        _viewModel.PropertyChanged -= ViewModelOnPropertyChanged;
        _openCodeService.NotificationRaised -= OnOpenCodeNotification;
        _openCodeService.QuestionResolved -= OnQuestionResolved;
        _openCodeService.Dispose();
        _openCodeDismissTimer.Stop();
        _islandAnimationTimer.Stop();
        _visualizerTimer.Stop();
        _systemAudioService.SpectrumChanged -= OnSystemAudioSpectrumChanged;
        _systemAudioService.Dispose();
        _trayIconService.Dispose();
    }
}

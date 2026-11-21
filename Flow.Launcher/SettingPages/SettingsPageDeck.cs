using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using iNKORE.UI.WPF.Modern.Media.Animation;
using ModernFrame = iNKORE.UI.WPF.Modern.Controls.Frame;

namespace Flow.Launcher.SettingPages;

// Pages stay attached for the window's lifetime, but only the selected one takes part in this panel's layout.
// The others keep the layout they were given and are brought up to date once the size has settled.
public sealed class SettingsPageDeck : Panel, IDisposable
{
    private static readonly Size Fallback = new(640, 500);
    private readonly Dictionary<Type, ModernFrame> _frames = new();
    private readonly Dictionary<Type, Task> _preparing = new();
    private readonly HashSet<Type> _visited = new();
    private readonly DispatcherTimer _settle;
    private Queue<ModernFrame> _late = new();
    private Type _requested;
    private Type _selected;
    private Size _constraint;
    private Size _size;
    private DispatcherOperation _refresh;
    private Stopwatch _switchTimer;
    private double _selectionTime;
    private bool _firstVisit;
    private bool _prerender;
    private bool _disposed;
    private int _resizes;
    private double _measureTime;
    private double _slowestResize;
    private double _slowestLate;

    public Type SelectedPageType => _selected;
    public event Action<Type, bool, double, double> SwitchMeasured;
    public event Action<string, double> Measured;
    public event Action<Exception> RefreshFailed;
    public event Action<Type, Exception> PrepareFailed;

    public SettingsPageDeck()
    {
        _settle = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(200) };
        _settle.Tick += OnSettled;
    }

    // While the window is cloaked each prepared page is shown for a few frames so that the render thread draws it once.
    public bool Prerender
    {
        get => _prerender;
        set
        {
            _prerender = value;
            if (value) return;
            foreach (var pair in _frames)
                if (pair.Key != _selected) pair.Value.Visibility = Visibility.Hidden;
        }
    }

    public bool Has(Type pageType) => _frames.ContainsKey(pageType);

    // Never faults: a failure is reported through PrepareFailed and leaves Has(pageType) false.
    public Task EnsureAsync(Type pageType, Func<Task> pause = null)
    {
        if (!_preparing.TryGetValue(pageType, out var task)) _preparing[pageType] = task = PrepareAsync(pageType, pause);
        return task;
    }

    private async Task PrepareAsync(Type type, Func<Task> pause)
    {
        try { await BuildAsync(type, pause); }
        catch (Exception exception) { PrepareFailed?.Invoke(type, exception); }
    }

    private async Task BuildAsync(Type type, Func<Task> pause)
    {
        // The page the user is waiting for only lets pending drawing through, so the loading shade appears first.
        Task Pause() => _requested == type ? Painted() : pause?.Invoke() ?? Task.CompletedTask;

        await Pause();
        if (_disposed) return;
        var clock = Stopwatch.StartNew();
        var page = (Page)Activator.CreateInstance(type);
        var lifecycle = (IPreloadableSettingsPage)page;
        lifecycle.PrepareViewModel();
        Measured?.Invoke($"prepare {type.Name} model", clock.Elapsed.TotalMilliseconds);

        await Pause();
        if (_disposed) return;
        clock.Restart();
        lifecycle.PrepareView();
        var frame = new ModernFrame { Visibility = Visibility.Hidden, IsHitTestVisible = false };
        Children.Add(frame);
        frame.Navigate(page, SettingsPageWarmup.Marker, new SuppressNavigationTransitionInfo());
        Measured?.Invoke($"prepare {type.Name} xaml", clock.Elapsed.TotalMilliseconds);

        // Frame navigation is posted at Normal priority; a lower-priority yield lets it finish.
        await System.Windows.Threading.Dispatcher.Yield(DispatcherPriority.Background);
        await Pause();
        if (_disposed) return;
        if (!ReferenceEquals(frame.Content, page)) throw new InvalidOperationException("Settings page preparation did not complete: " + type.Name);
        clock.Restart();
        Layout(frame);
        frame.UpdateLayout();
        _frames.Add(type, frame);
        Measured?.Invoke($"prepare {type.Name} layout", clock.Elapsed.TotalMilliseconds);

        if (_prerender && _selected != type)
        {
            frame.Visibility = Visibility.Visible;
            for (var drawn = 0; drawn < 3 && !_disposed; drawn++) await NextFrame();
            if (_disposed) return;
            if (_selected != type) frame.Visibility = Visibility.Hidden;
        }
        if (_requested == type && _selected != type) Show(type);
    }

    private void Layout(ModernFrame frame)
    {
        var arranged = _size.Width > 0 && _size.Height > 0;
        frame.Measure(arranged ? _constraint : Fallback);
        frame.Arrange(new Rect(arranged ? _size : Fallback));
    }

    internal static Task NextFrame()
    {
        var done = new TaskCompletionSource<bool>();
        EventHandler handler = null;
        handler = (_, _) =>
        {
            CompositionTarget.Rendering -= handler;
            done.TrySetResult(true);
        };
        CompositionTarget.Rendering += handler;
        return done.Task;
    }

    private static async Task Painted() => await System.Windows.Threading.Dispatcher.Yield(DispatcherPriority.Background);

    // A page that is not prepared yet is prepared first and shown as soon as it is ready.
    public bool Select(Type pageType)
    {
        _requested = pageType;
        if (_disposed || _selected == pageType) return false;
        if (!_frames.ContainsKey(pageType))
        {
            _ = EnsureAsync(pageType);
            return false;
        }
        Show(pageType);
        return true;
    }

    private void Show(Type pageType)
    {
        _refresh?.Abort();
        CompositionTarget.Rendering -= OnRendering;
        _switchTimer = Stopwatch.StartNew();
        if (_selected != null)
        {
            var previous = _frames[_selected];
            ((IPreloadableSettingsPage)previous.Content).Deactivate();
            previous.Visibility = Visibility.Hidden;
            previous.IsHitTestVisible = false;
        }
        _selected = pageType;
        var next = _frames[pageType];
        next.Visibility = Visibility.Visible;
        next.IsHitTestVisible = true;
        ((IPreloadableSettingsPage)next.Content).Activate();
        InvalidateMeasure();
        _firstVisit = _visited.Add(pageType);
        _selectionTime = _switchTimer.Elapsed.TotalMilliseconds;
        CompositionTarget.Rendering += OnRendering;
    }

    private void OnRendering(object sender, EventArgs e)
    {
        CompositionTarget.Rendering -= OnRendering;
        if (_disposed || _switchTimer == null) return;
        SwitchMeasured?.Invoke(_selected, _firstVisit, _selectionTime, _switchTimer.Elapsed.TotalMilliseconds);
        _switchTimer = null;
        var selected = _selected;
        _refresh = Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            if (_disposed || _selected != selected) return;
            var clock = Stopwatch.StartNew();
            try { ((IPreloadableSettingsPage)_frames[selected].Content).RefreshData(); }
            catch (Exception exception) { RefreshFailed?.Invoke(exception); }
            Measured?.Invoke("refresh " + selected.Name, clock.Elapsed.TotalMilliseconds);
        }));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _constraint = availableSize;
        if (_selected == null) return default;
        var clock = Stopwatch.GetTimestamp();
        var frame = _frames[_selected];
        frame.Measure(availableSize);
        _measureTime = Stopwatch.GetElapsedTime(clock).TotalMilliseconds;
        return frame.DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var clock = Stopwatch.GetTimestamp();
        if (_selected != null) _frames[_selected].Arrange(new Rect(finalSize));
        if (finalSize != _size)
        {
            _size = finalSize;
            _resizes++;
            _slowestResize = Math.Max(_slowestResize, _measureTime + Stopwatch.GetElapsedTime(clock).TotalMilliseconds);
            _settle.Stop();
            _settle.Start();
        }
        _measureTime = 0;
        return finalSize;
    }

    private void OnSettled(object sender, EventArgs e)
    {
        _settle.Stop();
        if (_disposed) return;
        Measured?.Invoke($"resize x{_resizes} slowest layout pass of the visible page", _slowestResize);
        _resizes = 0;
        _slowestResize = 0;
        _slowestLate = 0;
        _late = new Queue<ModernFrame>(_frames.Where(pair => pair.Key != _selected).Select(pair => pair.Value));
        LayoutNextLatePage();
    }

    private void LayoutNextLatePage()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            if (_disposed || _settle.IsEnabled) return;
            if (_late.Count == 0)
            {
                Measured?.Invoke("resize slowest relayout of a hidden page", _slowestLate);
                return;
            }
            var clock = Stopwatch.StartNew();
            Layout(_late.Dequeue());
            _slowestLate = Math.Max(_slowestLate, clock.Elapsed.TotalMilliseconds);
            LayoutNextLatePage();
        }));
    }

    internal Page GetPage(Type type) => (Page)_frames[type].Content;

    // Called when the window is closed: the next open starts like a newly created window, with every page at its top.
    internal void Suspend()
    {
        _refresh?.Abort();
        CompositionTarget.Rendering -= OnRendering;
        _switchTimer = null;
        _visited.Clear();
        _requested = null;
        foreach (var page in _frames.Values) ScrollHome(page);
        if (_selected == null) return;
        var frame = _frames[_selected];
        ((IPreloadableSettingsPage)frame.Content).Deactivate();
        frame.Visibility = Visibility.Hidden;
        frame.IsHitTestVisible = false;
        _selected = null;
    }

    private static void ScrollHome(DependencyObject root)
    {
        if (root is ScrollViewer viewer && (viewer.VerticalOffset != 0 || viewer.HorizontalOffset != 0)) viewer.ScrollToHome();
        for (var index = VisualTreeHelper.GetChildrenCount(root) - 1; index >= 0; index--) ScrollHome(VisualTreeHelper.GetChild(root, index));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _settle.Stop();
        _refresh?.Abort();
        CompositionTarget.Rendering -= OnRendering;
        foreach (ModernFrame frame in Children)
        {
            if (frame.Content is IPreloadableSettingsPage page) page.Deactivate();
            frame.StopLoading();
            frame.Content = null;
            while (frame.RemoveBackEntry() != null) { }
        }
        Children.Clear();
        _frames.Clear();
        _selected = null;
    }
}

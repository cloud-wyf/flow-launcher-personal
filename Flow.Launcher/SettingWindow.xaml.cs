using System;
using System.ComponentModel;
using System.Linq;
using Stopwatch = System.Diagnostics.Stopwatch;
using System.Threading.Tasks;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CommunityToolkit.Mvvm.DependencyInjection;
using Flow.Launcher.Infrastructure;
using Flow.Launcher.Infrastructure.UserSettings;
using Flow.Launcher.Plugin.SharedModels;
using Flow.Launcher.SettingPages;
using Flow.Launcher.SettingPages.Views;
using Flow.Launcher.SettingPages.ViewModels;
using Flow.Launcher.ViewModel;
using iNKORE.UI.WPF.Modern.Controls;

namespace Flow.Launcher;

public partial class SettingWindow
{
    #region Private Fields

    private static readonly Type[] Pages =
    {
        typeof(SettingsPaneGeneral), typeof(SettingsPanePlugins), typeof(SettingsPanePluginStore),
        typeof(SettingsPaneTheme), typeof(SettingsPaneHotkey), typeof(SettingsPaneProxy), typeof(SettingsPaneAbout)
    };
    private readonly Settings _settings;
    private readonly SettingWindowViewModel _viewModel;
    private bool _closed;
    private bool _allowClose;
    private bool _resetForNextOpen;
    private bool _everShown;
    private bool _broken;
    private bool _staged;
    private Task _pages;
    private UiStallMonitor _stalls;
    internal bool IsOpen { get; private set; }
    internal bool IsUsable => !_closed && !_broken;

    #endregion

    #region Constructor

    public SettingWindow()
    {
        _settings = Ioc.Default.GetRequiredService<Settings>();
        _viewModel = Ioc.Default.GetRequiredService<SettingWindowViewModel>();
        DataContext = _viewModel;
        // Since WindowStartupLocation is set to Manual, initialize the window position before calling InitializeComponent
        UpdatePosition();
        InitializeComponent();
        Icon = BitmapFrame.Create(new Uri(System.IO.Path.Combine(Constant.ProgramDirectory, "Images", "app.ico")));
        Closing += OnSettingsClosing;
        PreviewKeyDown += Settings_PreviewKeyDown;
        PageDeck.SwitchMeasured += OnSwitchMeasured;
        PageDeck.Measured += (what, ms) => App.API.LogInfo("SettingsTabs", $"{what} {ms:F2}ms");
        PageDeck.RefreshFailed += exception => App.API.LogError("SettingsTabs", $"Refresh failed: {exception}");
        PageDeck.PrepareFailed += OnPrepareFailed;
    }

    #endregion

    #region Window Events

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshMaximizeRestoreButton();
        UpdatePosition();
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    // Sometimes the navigation is not triggered by button click,
    // so we need to update the selected item here
    private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SettingWindowViewModel.PageType):
                var selectedIndex = _viewModel.PageType.Name switch
                {
                    nameof(SettingsPaneGeneral) => 0,
                    nameof(SettingsPanePlugins) => 1,
                    nameof(SettingsPanePluginStore) => 2,
                    nameof(SettingsPaneTheme) => 3,
                    nameof(SettingsPaneHotkey) => 4,
                    nameof(SettingsPaneProxy) => 5,
                    nameof(SettingsPaneAbout) => 6,
                    _ => 0
                };
                NavView.SelectedItem = NavView.MenuItems[selectedIndex];
                break;
        }
    }

    private void OnClosed(object sender, EventArgs e)
    {
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        _closed = true;
        IsOpen = false;
        _stalls?.Dispose();
        PageDeck.Dispose();

        // If app is exiting, settings save is not needed because main window closing event will handle this
        if (App.LoadingOrExiting || !_everShown) return;
        // Save settings when window is closed
        _settings.Save();
        App.API.SavePluginSettings();
    }

    // Closing keeps the prepared pages: the window is cloaked and hidden, and shown again on the next open.
    private void OnSettingsClosing(object sender, CancelEventArgs e)
    {
        if (_allowClose || _broken || App.LoadingOrExiting) return;
        e.Cancel = true;
        _settings.Save();
        App.API.SavePluginSettings();
        IsOpen = false;
        _staged = false;
        _stalls?.Dispose();
        _stalls = null;
        Win32Helper.DWMSetCloakForWindow(this, true);
        Hide();
        ShowInTaskbar = false;
        PageDeck.Suspend();
        _resetForNextOpen = true;
    }

    internal void ClosePermanently()
    {
        if (_closed) return;
        _allowClose = true;
        Close();
    }

    // Prepares every page inside the real window while DWM keeps it off screen,
    // so layout and drawing go through the same pipeline the user will see.
    internal async Task WarmAsync(Func<Task> pause)
    {
        if (!IsVisible)
        {
            ShowInTaskbar = false;
            ShowActivated = false;
            new WindowInteropHelper(this).EnsureHandle();
            if (!Win32Helper.DWMSetCloakForWindow(this, true)) throw new InvalidOperationException("DWM cloak is unavailable");
            Show();
        }
        PageDeck.Prerender = !_staged;
        await (_pages ??= PreparePagesAsync(pause));
        PageDeck.Prerender = false;
        if (!_staged && !_closed) Hide();
    }

    private async Task PreparePagesAsync(Func<Task> pause)
    {
        foreach (var type in Pages)
        {
            if (_closed) return;
            await PageDeck.EnsureAsync(type, pause);
        }
    }

    internal async Task OpenAsync(Func<Task> pause)
    {
        if (IsOpen)
        {
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
            return;
        }
        var clock = Stopwatch.StartNew();
        var ready = Pages.Count(PageDeck.Has);
        await StageAsync();
        var staged = clock.Elapsed.TotalMilliseconds;
        Reveal();
        App.API.LogInfo("SettingsWindow", $"Opened: first frame drawn after {staged:F1}ms, revealed after {clock.Elapsed.TotalMilliseconds:F1}ms, pages ready beforehand {ready}/{Pages.Length}");
        _ = _pages ??= PreparePagesAsync(pause);
    }

    // Everything up to the first drawn frames happens under the cloak, so no stale or half-built content is ever visible.
    private async Task StageAsync()
    {
        _staged = true;
        ResetForOpen();
        if (!IsVisible)
        {
            // WPF refuses to show a maximized window without activating it.
            ShowActivated = WindowState == WindowState.Maximized;
            ShowInTaskbar = false;
            new WindowInteropHelper(this).EnsureHandle();
            Win32Helper.DWMSetCloakForWindow(this, true);
            Show();
        }
        await PageDeck.EnsureAsync(typeof(SettingsPaneGeneral));
        PageDeck.Prerender = false;
        await SettingsPageDeck.NextFrame();
        await SettingsPageDeck.NextFrame();
        // The render thread presents asynchronously; one composition pass gives the drawn frame time to reach the window surface.
        DwmFlush();
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmFlush();

    private void Reveal()
    {
        IsOpen = true;
        _everShown = true;
        ShowInTaskbar = true;
        Win32Helper.DWMSetCloakForWindow(this, false);
        Activate();
        Focus();
        _stalls = new UiStallMonitor(Dispatcher, OnStall);
    }

    private void ResetForOpen()
    {
        UpdatePositionAndState();
        RefreshMaximizeRestoreButton();
        if (_resetForNextOpen)
        {
            if (PageDeck.Has(typeof(SettingsPanePlugins)))
                ((SettingsPanePluginsViewModel)PageDeck.GetPage(typeof(SettingsPanePlugins)).DataContext).ResetClosedWindowDrafts();
            if (PageDeck.Has(typeof(SettingsPanePluginStore)))
            {
                var store = (SettingsPanePluginStoreViewModel)PageDeck.GetPage(typeof(SettingsPanePluginStore)).DataContext;
                store.FilterText = "";
                store.ShowDotNet = store.ShowPython = store.ShowNodeJs = store.ShowExecutable = true;
            }
            _resetForNextOpen = false;
        }
        NavView.SelectedItem = NavView.MenuItems[0];
        ShowPage(typeof(SettingsPaneGeneral));
    }

    // Menu selection changes made while the window is only being prepared must not pick a page.
    private void ShowPage(Type pageType)
    {
        if (!_staged || PageDeck.SelectedPageType == pageType || PageDeck.Select(pageType)) return;
        LoadingText.Text = "正在加载设置…";
        LoadingShade.Visibility = Visibility.Visible;
    }

    private void OnSwitchMeasured(Type type, bool first, double selectionMs, double frameMs)
    {
        if (!_broken) LoadingShade.Visibility = Visibility.Collapsed;
        App.API.LogInfo("SettingsTabs", $"{type.Name} first={first} select={selectionMs:F2}ms next-render={frameMs:F2}ms");
    }

    private void OnPrepareFailed(Type type, Exception exception)
    {
        _broken = true;
        LoadingText.Text = "设置加载失败，请关闭后重试。";
        LoadingShade.Visibility = Visibility.Visible;
        App.API.LogError("SettingsTabs", $"Preparing {type.Name} failed: {exception}");
    }

    private void OnStall(double ms)
    {
        var over = Mouse.DirectlyOver as DependencyObject;
        var control = over;
        while (control != null && control is not Control) control = control is Visual ? VisualTreeHelper.GetParent(control) : LogicalTreeHelper.GetParent(control);
        App.API.LogInfo("SettingsTabs", $"stall {ms:F0}ms tab={PageDeck.SelectedPageType?.Name} over={control?.GetType().Name ?? over?.GetType().Name}" +
            $" data={(over as FrameworkElement)?.DataContext?.GetType().Name} focus={Keyboard.FocusedElement?.GetType().Name}");
    }

    private void OnCloseExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        Close();
    }

    private void window_MouseDown(object sender, MouseButtonEventArgs e) /* for close hotkey popup */
    {
        if (Keyboard.FocusedElement is not TextBox textBox) return;
        var tRequest = new TraversalRequest(FocusNavigationDirection.Next);
        textBox.MoveFocus(tRequest);
    }

    private void Window_StateChanged(object sender, EventArgs e)
    {
        RefreshMaximizeRestoreButton();
        if (IsLoaded && WindowState != WindowState.Minimized)
        {
            _settings.SettingWindowState = WindowState;
        }
    }
    private void Window_Activated(object sender, EventArgs e)
    {

        // Band-aid fix: Rare edge case where Alt+Tab activates the window but doesn't trigger StateChanged
        // So we need to restore/maximize it here if it's still minimized
        if (WindowState == WindowState.Minimized && _settings.SettingWindowState != WindowState.Minimized)
        {
            WindowState = _settings.SettingWindowState;
        }
    }

    private void Window_LocationChanged(object sender, EventArgs e)
    {
        if (IsLoaded)
        {
            _settings.SettingWindowTop = Top;
            _settings.SettingWindowLeft = Left;
        }
    }

    #endregion

    #region Window Custom TitleBar

    private void OnMinimizeButtonClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnMaximizeRestoreButtonClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState switch
        {
            WindowState.Maximized => WindowState.Normal,
            _ => WindowState.Maximized
        };
    }

    private void OnCloseButtonClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void RefreshMaximizeRestoreButton()
    {
        if (WindowState == WindowState.Maximized)
        {
            MaximizeButton.Visibility = Visibility.Hidden;
            RestoreButton.Visibility = Visibility.Visible;
        }
        else
        {
            MaximizeButton.Visibility = Visibility.Visible;
            RestoreButton.Visibility = Visibility.Hidden;
        }
    }

    #endregion

    #region Window Position

    public void UpdatePositionAndState()
    {
        UpdatePosition();
        WindowState = _settings.SettingWindowState == WindowState.Minimized
            ? WindowState.Normal
            : _settings.SettingWindowState;
    }

    private void UpdatePosition()
    {
        var previousTop = _settings.SettingWindowTop;
        var previousLeft = _settings.SettingWindowLeft;

        if (previousTop == null || previousLeft == null || !IsPositionValid(previousTop.Value, previousLeft.Value))
        {
            SetWindowPosition(WindowTop(), WindowLeft());
        }
        else
        {
            var left = _settings.SettingWindowLeft.Value;
            var top = _settings.SettingWindowTop.Value;
            AdjustWindowPosition(ref top, ref left);
            SetWindowPosition(top, left);
        }
    }

    private void SetWindowPosition(double top, double left)
    {
        // Ensure window does not exceed screen boundaries
        top = Math.Max(top, SystemParameters.VirtualScreenTop);
        left = Math.Max(left, SystemParameters.VirtualScreenLeft);
        top = Math.Min(top, SystemParameters.VirtualScreenHeight - ActualHeight);
        left = Math.Min(left, SystemParameters.VirtualScreenWidth - ActualWidth);

        Top = top;
        Left = left;
    }

    private void AdjustWindowPosition(ref double top, ref double left)
    {
        // Adjust window position if it exceeds screen boundaries
        top = Math.Max(top, SystemParameters.VirtualScreenTop);
        left = Math.Max(left, SystemParameters.VirtualScreenLeft);
        top = Math.Min(top, SystemParameters.VirtualScreenHeight - ActualHeight);
        left = Math.Min(left, SystemParameters.VirtualScreenWidth - ActualWidth);
    }

    private static bool IsPositionValid(double top, double left)
    {
        foreach (var screen in MonitorInfo.GetDisplayMonitors())
        {
            var workingArea = screen.WorkingArea;

            if (left >= workingArea.Left && left < workingArea.Right &&
                top >= workingArea.Top && top < workingArea.Bottom)
            {
                return true;
            }
        }
        return false;
    }

    private double WindowLeft()
    {
        var screen = MonitorInfo.GetCursorDisplayMonitor();
        var dip1 = Win32Helper.TransformPixelsToDIP(this, screen.WorkingArea.X, 0);
        var dip2 = Win32Helper.TransformPixelsToDIP(this, screen.WorkingArea.Width, 0);
        var left = (dip2.X - ActualWidth) / 2 + dip1.X;
        return left;
    }

    private double WindowTop()
    {
        var screen = MonitorInfo.GetCursorDisplayMonitor();
        var dip1 = Win32Helper.TransformPixelsToDIP(this, 0, screen.WorkingArea.Y);
        var dip2 = Win32Helper.TransformPixelsToDIP(this, 0, screen.WorkingArea.Height);
        var top = (dip2.Y - ActualHeight) / 2 + dip1.Y - 20;
        return top;
    }

    #endregion

    #region Navigation View Events

    private void Settings_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.F || Keyboard.Modifiers != ModifierKeys.Control) return;
        var type = PageDeck.SelectedPageType;
        if (type == typeof(SettingsPanePlugins))
            ((SettingsPanePlugins)PageDeck.GetPage(type)).FocusFilter();
        else if (type == typeof(SettingsPanePluginStore))
            ((SettingsPanePluginStore)PageDeck.GetPage(type)).FocusFilter();
        else return;
        e.Handled = true;
    }

    private void NavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            _viewModel.SetPageType(typeof(SettingsPaneGeneral));
            ShowPage(typeof(SettingsPaneGeneral));
        }
        else
        {
            var selectedItem = (NavigationViewItem)args.SelectedItem;
            if (selectedItem == null)
            {
                NavView_Loaded(sender, null); /* Reset First Page */
                return;
            }

            var pageType = selectedItem.Name switch
            {
                nameof(General) => typeof(SettingsPaneGeneral),
                nameof(Plugins) => typeof(SettingsPanePlugins),
                nameof(PluginStore) => typeof(SettingsPanePluginStore),
                nameof(Theme) => typeof(SettingsPaneTheme),
                nameof(Hotkey) => typeof(SettingsPaneHotkey),
                nameof(Proxy) => typeof(SettingsPaneProxy),
                nameof(About) => typeof(SettingsPaneAbout),
                _ => typeof(SettingsPaneGeneral)
            };
            // Only navigate if the page type changes to fix navigation forward/back issue
            _viewModel.SetPageType(pageType);
            ShowPage(pageType);
        }
    }

    private void NavView_Loaded(object sender, RoutedEventArgs e)
    {
        NavView.SelectedItem ??= NavView.MenuItems[0];
    }

    #endregion
}

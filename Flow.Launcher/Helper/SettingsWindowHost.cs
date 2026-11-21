using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Flow.Launcher.Helper;

// Owns the one settings window. It is prepared after startup while cloaked, then only shown and hidden.
internal static class SettingsWindowHost
{
    // Someone who has just started Flow or used the search window is likely to call it again within moments.
    private static readonly TimeSpan AfterStartup = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan AfterSearch = TimeSpan.FromSeconds(1.5);
    private static SettingWindow _window;
    private static TaskCompletionSource<bool> _hurry = NewSignal();
    private static bool _opening;

    internal static void PreloadWhenIdle()
    {
        Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(async () =>
        {
            try
            {
                await HurriedOr(Task.Delay(AfterStartup));
                await PauseAsync();
                if (App.LoadingOrExiting) return;
                var clock = Stopwatch.StartNew();
                var before = PrivateMegabytes();
                await Window().WarmAsync(PauseAsync);
                App.API.LogInfo("SettingsWindow", $"Warmup finished after {clock.Elapsed.TotalMilliseconds:F0}ms including pauses, private memory {before:F0} -> {PrivateMegabytes():F0} MB");
            }
            catch (Exception exception)
            {
                Discard();
                App.API.LogException(nameof(SettingsWindowHost), "Settings warmup failed", exception);
            }
        }));
    }

    internal static async Task OpenAsync()
    {
        if (_opening) return;
        _opening = true;
        _hurry.TrySetResult(true);
        try
        {
            if (App.LoadingOrExiting) return;
            await Window().OpenAsync(PauseAsync);
        }
        catch (Exception exception)
        {
            Discard();
            App.API.LogException(nameof(SettingsWindowHost), "Unable to open settings", exception);
            App.API.ShowMsgError("无法打开设置", "设置准备失败，请查看日志后重试。");
        }
        finally
        {
            _opening = false;
            _hurry = NewSignal();
        }
    }

    private static SettingWindow Window()
    {
        if (_window is { IsUsable: false }) Discard();
        return _window ??= new SettingWindow();
    }

    private static void Discard()
    {
        var window = _window;
        _window = null;
        window?.ClosePermanently();
    }

    // Background preparation waits for an idle dispatcher and stays out of the way of the search window.
    // Once the user has asked for the settings window it only yields to pending input.
    private static async Task PauseAsync()
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var main = Application.Current.MainWindow;
        while (main?.IsVisible == true && !_opening && _window?.IsOpen != true && !App.LoadingOrExiting)
        {
            await HurriedOr(WhenHidden(main));
            await HurriedOr(Task.Delay(AfterSearch));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        }
    }

    private static Task WhenHidden(Window main)
    {
        var hidden = NewSignal();
        void Changed(object sender, DependencyPropertyChangedEventArgs args)
        {
            if (main.IsVisible) return;
            main.IsVisibleChanged -= Changed;
            hidden.TrySetResult(true);
        }
        main.IsVisibleChanged += Changed;
        return hidden.Task;
    }

    private static Task HurriedOr(Task task) => Task.WhenAny(task, _hurry.Task);

    private static TaskCompletionSource<bool> NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static double PrivateMegabytes()
    {
        using var process = Process.GetCurrentProcess();
        return process.PrivateMemorySize64 / 1048576.0;
    }
}

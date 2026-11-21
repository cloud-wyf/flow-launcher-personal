using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Threading;

namespace Flow.Launcher.SettingPages;

// Measures how long the UI thread takes to reach an input-priority callback, which is what a click would have waited.
internal sealed class UiStallMonitor : IDisposable
{
    private const int Interval = 20;
    private const int Threshold = 50;
    private readonly Dispatcher _dispatcher;
    private readonly Action<double> _report;
    private readonly Timer _timer;
    private int _waiting;

    public UiStallMonitor(Dispatcher dispatcher, Action<double> report)
    {
        _dispatcher = dispatcher;
        _report = report;
        _timer = new Timer(_ => Probe(), null, Interval, Interval);
    }

    private void Probe()
    {
        if (Interlocked.Exchange(ref _waiting, 1) == 1) return;
        var posted = Stopwatch.GetTimestamp();
        _dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            var delay = Stopwatch.GetElapsedTime(posted).TotalMilliseconds;
            Volatile.Write(ref _waiting, 0);
            if (delay >= Threshold) _report(delay);
        }));
    }

    public void Dispose() => _timer.Dispose();
}

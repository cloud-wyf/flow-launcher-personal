namespace Flow.Launcher.SettingPages;

// Preparing a page must not change navigation state or subscribe navigation-only events.
internal interface IPreloadableSettingsPage
{
    void PrepareViewModel();
    void PrepareView();
    void Activate();
    void Deactivate();
    void RefreshData();
}

internal static class SettingsPageWarmup
{
    public static readonly object Marker = new();
}

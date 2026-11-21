using System.Windows.Navigation;
using CommunityToolkit.Mvvm.DependencyInjection;
using Flow.Launcher.SettingPages.ViewModels;
using Flow.Launcher.ViewModel;

namespace Flow.Launcher.SettingPages.Views;

public partial class SettingsPaneProxy : IPreloadableSettingsPage
{
    private SettingsPaneProxyViewModel _viewModel = null!;
    private readonly SettingWindowViewModel _settingViewModel = Ioc.Default.GetRequiredService<SettingWindowViewModel>();

    void IPreloadableSettingsPage.PrepareViewModel()
    {
        if (_viewModel != null) return;
        _viewModel = Ioc.Default.GetRequiredService<SettingsPaneProxyViewModel>();
        DataContext = _viewModel;
    }

    void IPreloadableSettingsPage.PrepareView()
    {
        if (!IsInitialized) InitializeComponent();
    }

    void IPreloadableSettingsPage.Activate()
    {
        _settingViewModel.PageType = typeof(SettingsPaneProxy);
    }

    void IPreloadableSettingsPage.Deactivate()
    {
    }

    void IPreloadableSettingsPage.RefreshData()
    {

    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        var page = (IPreloadableSettingsPage)this;
        page.PrepareViewModel();
        page.PrepareView();
        if (!ReferenceEquals(e.ExtraData, SettingsPageWarmup.Marker))
        {
            page.Activate();
            page.RefreshData();
        }
        base.OnNavigatedTo(e);
    }
}

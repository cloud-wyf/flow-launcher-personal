using System.Windows.Navigation;
using CommunityToolkit.Mvvm.DependencyInjection;
using Flow.Launcher.SettingPages.ViewModels;
using Flow.Launcher.ViewModel;

namespace Flow.Launcher.SettingPages.Views;

public partial class SettingsPaneTheme : IPreloadableSettingsPage
{
    private SettingsPaneThemeViewModel _viewModel = null!;
    private readonly SettingWindowViewModel _settingViewModel = Ioc.Default.GetRequiredService<SettingWindowViewModel>();

    void IPreloadableSettingsPage.PrepareViewModel()
    {
        if (_viewModel != null) return;
        _viewModel = Ioc.Default.GetRequiredService<SettingsPaneThemeViewModel>();
        DataContext = _viewModel;
    }

    void IPreloadableSettingsPage.PrepareView()
    {
        if (!IsInitialized) InitializeComponent();
    }

    void IPreloadableSettingsPage.Activate()
    {
        _settingViewModel.PageType = typeof(SettingsPaneTheme);
    }

    void IPreloadableSettingsPage.Deactivate()
    {
    }

    void IPreloadableSettingsPage.RefreshData()
    {
        _viewModel.RefreshNavigationData();
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

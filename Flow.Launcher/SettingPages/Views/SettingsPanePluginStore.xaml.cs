using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Navigation;
using CommunityToolkit.Mvvm.DependencyInjection;
using Flow.Launcher.SettingPages.ViewModels;
using Flow.Launcher.ViewModel;

namespace Flow.Launcher.SettingPages.Views;

public partial class SettingsPanePluginStore : IPreloadableSettingsPage
{
    private SettingsPanePluginStoreViewModel _viewModel = null!;
    private readonly SettingWindowViewModel _settingViewModel = Ioc.Default.GetRequiredService<SettingWindowViewModel>();

    void IPreloadableSettingsPage.PrepareViewModel()
    {
        if (_viewModel != null) return;
        _viewModel = Ioc.Default.GetRequiredService<SettingsPanePluginStoreViewModel>();
        DataContext = _viewModel;
    }

    void IPreloadableSettingsPage.PrepareView()
    {
        if (!IsInitialized) InitializeComponent();
    }

    void IPreloadableSettingsPage.Activate()
    {
        _settingViewModel.PageType = typeof(SettingsPanePluginStore);
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    void IPreloadableSettingsPage.Deactivate()
    {
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
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

    private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SettingsPanePluginStoreViewModel.FilterText):
            case nameof(SettingsPanePluginStoreViewModel.ShowDotNet):
            case nameof(SettingsPanePluginStoreViewModel.ShowPython):
            case nameof(SettingsPanePluginStoreViewModel.ShowNodeJs):
            case nameof(SettingsPanePluginStoreViewModel.ShowExecutable):
                ((CollectionViewSource)FindResource("PluginStoreCollectionView")).View.Refresh();
                break;
        }
    }

    protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
    {
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        base.OnNavigatingFrom(e);
    }

    private void SettingsPanePlugins_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not Key.F || Keyboard.Modifiers is not ModifierKeys.Control) return;
        PluginStoreFilterTextbox.Focus();
    }

    internal void FocusFilter() => PluginStoreFilterTextbox.Focus();

    private void Hyperlink_OnRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        App.API.OpenUrl(e.Uri.AbsoluteUri);
        e.Handled = true;
    }

    private void PluginStoreCollectionView_OnFilter(object sender, FilterEventArgs e)
    {
        if (e.Item is not PluginStoreItemViewModel plugin)
        {
            e.Accepted = false;
            return;
        }

        e.Accepted = _viewModel.SatisfiesFilter(plugin);
    }
}

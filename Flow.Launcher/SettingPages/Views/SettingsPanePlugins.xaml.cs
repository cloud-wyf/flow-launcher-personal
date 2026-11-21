using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Navigation;
using CommunityToolkit.Mvvm.DependencyInjection;
using Flow.Launcher.SettingPages.ViewModels;
using Flow.Launcher.ViewModel;

namespace Flow.Launcher.SettingPages.Views;

public partial class SettingsPanePlugins : IPreloadableSettingsPage
{
    private SettingsPanePluginsViewModel _viewModel = null!;
    private readonly SettingWindowViewModel _settingViewModel = Ioc.Default.GetRequiredService<SettingWindowViewModel>();

    void IPreloadableSettingsPage.PrepareViewModel()
    {
        if (_viewModel != null) return;
        _viewModel = Ioc.Default.GetRequiredService<SettingsPanePluginsViewModel>();
        DataContext = _viewModel;
    }

    void IPreloadableSettingsPage.PrepareView()
    {
        if (!IsInitialized) InitializeComponent();
    }

    void IPreloadableSettingsPage.Activate()
    {
        _settingViewModel.PageType = typeof(SettingsPanePlugins);
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

    private void PluginListBox_Loaded(object sender, RoutedEventArgs e)
    {
        // After list is loaded, we need to clear selection to make sure all items can be mouse hovered
        // because the selected item cannot be hovered.
        if (sender is not ListBox listBox) return;
        listBox.SelectedIndex = -1;
    }

    private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsPanePluginsViewModel.FilterText))
        {
            ((CollectionViewSource)FindResource("PluginCollectionView")).View.Refresh();
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
        PluginFilterTextbox.Focus();
    }

    internal void FocusFilter() => PluginFilterTextbox.Focus();

    private void PluginCollectionView_OnFilter(object sender, FilterEventArgs e)
    {
        if (e.Item is not PluginViewModel plugin)
        {
            e.Accepted = false;
            return;
        }

        e.Accepted = _viewModel.SatisfiesFilter(plugin);
    }
}

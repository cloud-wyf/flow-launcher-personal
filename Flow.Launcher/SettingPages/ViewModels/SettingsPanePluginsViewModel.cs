using System.Collections.Generic;
using System.Linq;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Flow.Launcher.Core.Plugin;
using Flow.Launcher.Infrastructure.UserSettings;
using Flow.Launcher.Plugin;
using Flow.Launcher.ViewModel;
using iNKORE.UI.WPF.Modern.Controls;

#nullable enable

namespace Flow.Launcher.SettingPages.ViewModels;

public partial class SettingsPanePluginsViewModel : BaseModel
{
    private readonly Settings _settings;

    public class DisplayModeData : DropdownDataGeneric<DisplayMode> { }

    public List<DisplayModeData> DisplayModes { get; } =
        DropdownDataGeneric<DisplayMode>.GetValues<DisplayModeData>("DisplayMode");

    private DisplayMode _selectedDisplayMode = DisplayMode.OnOff;
    public DisplayMode SelectedDisplayMode
    {
        get => _selectedDisplayMode;
        set
        {
            if (_selectedDisplayMode != value)
            {
                _selectedDisplayMode = value;
                OnPropertyChanged();
                UpdateDisplayModeFromSelection();
            }
        }
    }

    private bool _isOnOffSelected = true;
    public bool IsOnOffSelected
    {
        get => _isOnOffSelected;
        set
        {
            if (_isOnOffSelected != value)
            {
                _isOnOffSelected = value;
                OnPropertyChanged();
            }
        }
    }

    private bool _isPrioritySelected;
    public bool IsPrioritySelected
    {
        get => _isPrioritySelected;
        set
        {
            if (_isPrioritySelected != value)
            {
                _isPrioritySelected = value;
                OnPropertyChanged();
            }
        }
    }

    private bool _isSearchDelaySelected;
    public bool IsSearchDelaySelected
    {
        get => _isSearchDelaySelected;
        set
        {
            if (_isSearchDelaySelected != value)
            {
                _isSearchDelaySelected = value;
                OnPropertyChanged();
            }
        }
    }

    private bool _isHomeOnOffSelected;
    public bool IsHomeOnOffSelected
    {
        get => _isHomeOnOffSelected;
        set
        {
            if (_isHomeOnOffSelected != value)
            {
                _isHomeOnOffSelected = value;
                OnPropertyChanged();
            }
        }
    }

    public SettingsPanePluginsViewModel(Settings settings)
    {
        _settings = settings;
        UpdateEnumDropdownLocalizations();
        SetSourceOrder(SearchSourceOrderStore.Current.Sources);
    }

    public ObservableCollection<SearchSourceItem> SearchSources { get; } = new();

    public string SourceOrderStatus { get; private set; } = "保存后下一次搜索生效；各类内部按得分排序。";

    internal void ResetClosedWindowDrafts()
    {
        FilterText = "";
        SetSourceOrder(SearchSourceOrderStore.Current.Sources);
        SourceOrderStatus = "保存后下一次搜索生效；各类内部按得分排序。";
        foreach (var plugin in PluginViewModels) plugin.IsExpanded = false;
    }

    private void SetSourceOrder(IEnumerable<string> sources)
    {
        SearchSources.Clear();
        foreach (var key in sources) SearchSources.Add(new SearchSourceItem(key));
        RefreshSourcePositions();
    }

    private void RefreshSourcePositions()
    {
        for (var index = 0; index < SearchSources.Count; index++)
        {
            SearchSources[index].Position = index + 1;
            SearchSources[index].CanMoveUp = index > 0;
            SearchSources[index].CanMoveDown = index < SearchSources.Count - 1;
        }
    }

    private void MoveSource(SearchSourceItem item, int direction)
    {
        var index = SearchSources.IndexOf(item);
        var destination = index + direction;
        if (index < 0 || destination < 0 || destination >= SearchSources.Count) return;
        SearchSources.Move(index, destination);
        RefreshSourcePositions();
        SourceOrderStatus = "顺序已调整，点击“保存顺序”生效。";
        OnPropertyChanged(nameof(SourceOrderStatus));
    }

    [RelayCommand]
    private void MoveSourceUp(SearchSourceItem item) => MoveSource(item, -1);

    [RelayCommand]
    private void MoveSourceDown(SearchSourceItem item) => MoveSource(item, 1);

    [RelayCommand]
    private void ResetSourceOrder()
    {
        SetSourceOrder(SearchSourceOrder.DefaultSources);
        SourceOrderStatus = "已恢复默认顺序，点击“保存顺序”生效。";
        OnPropertyChanged(nameof(SourceOrderStatus));
    }

    [RelayCommand]
    private void SaveSourceOrder()
    {
        try
        {
            SearchSourceOrderStore.Current.Save(SearchSources.Select(item => item.Key));
            SourceOrderStatus = "已保存，下一次搜索生效，无需重启。";
        }
        catch (Exception exception)
        {
            SourceOrderStatus = "保存失败，原有顺序继续生效：" + exception.Message;
        }
        OnPropertyChanged(nameof(SourceOrderStatus));
    }

    private string filterText = string.Empty;
    public string FilterText
    {
        get => filterText;
        set
        {
            if (filterText != value)
            {
                filterText = value;
                OnPropertyChanged();
            }
        }
    }

    private List<PluginViewModel>? _pluginViewModels;
    internal void RefreshNavigationData()
    {
        UpdateEnumDropdownLocalizations();
        if (_pluginViewModels == null) return;

        var plugins = App.API.GetAllPlugins()
            .OrderBy(plugin => plugin.Metadata.Disabled)
            .ThenBy(plugin => plugin.Metadata.Name)
            .Where(plugin => _settings.PluginSettings.GetPluginSettings(plugin.Metadata.ID) != null)
            .ToList();
        if (_pluginViewModels.Select(plugin => plugin.PluginPair).SequenceEqual(plugins, ReferenceEqualityComparer.Instance)) return;

        // Preserve existing plugin controls and their edits when only order or membership changed.
        var existing = _pluginViewModels.ToDictionary(plugin => plugin.PluginPair, ReferenceEqualityComparer.Instance);
        _pluginViewModels = plugins.Select(plugin => existing.TryGetValue(plugin, out var viewModel)
            ? viewModel
            : new PluginViewModel
            {
                PluginPair = plugin,
                PluginSettingsObject = _settings.PluginSettings.GetPluginSettings(plugin.Metadata.ID)
            }).ToList();
        OnPropertyChanged(nameof(PluginViewModels));
    }

    // Get all plugins: Initializing & Initialized & Init failed plugins
    // Include init failed ones so that we can uninstall them
    // Include initializing ones so that we can change related settings like action keywords, etc.
    public List<PluginViewModel> PluginViewModels => _pluginViewModels ??= App.API.GetAllPlugins()
        .OrderBy(plugin => plugin.Metadata.Disabled)
        .ThenBy(plugin => plugin.Metadata.Name)
        .Select(plugin => new PluginViewModel
        {
            PluginPair = plugin,
            PluginSettingsObject = _settings.PluginSettings.GetPluginSettings(plugin.Metadata.ID)
        })
        .Where(plugin => plugin.PluginSettingsObject != null)
        .ToList();

    public bool SatisfiesFilter(PluginViewModel plugin)
    {
        return string.IsNullOrEmpty(FilterText) ||
            App.API.FuzzySearch(FilterText, plugin.PluginPair.Metadata.Name).IsSearchPrecisionScoreMet() ||
            App.API.FuzzySearch(FilterText, plugin.PluginPair.Metadata.Description).IsSearchPrecisionScoreMet();
    }

    [RelayCommand]
    private async Task OpenHelperAsync(Button button)
    {
        var helpDialog = new ContentDialog()
        {
            Owner = Window.GetWindow(button),
            Content = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = (string)Application.Current.Resources["priority"],
                        FontSize = 18,
                        Margin = new Thickness(0, 0, 0, 10),
                        TextWrapping = TextWrapping.Wrap
                    },
                    new TextBlock
                    {
                        Text = (string)Application.Current.Resources["priority_tips"],
                        TextWrapping = TextWrapping.Wrap
                    },
                    new TextBlock
                    {
                        Text = (string)Application.Current.Resources["searchDelay"],
                        FontSize = 18,
                        Margin = new Thickness(0, 24, 0, 10),
                        TextWrapping = TextWrapping.Wrap
                    },
                    new TextBlock
                    {
                        Text = (string)Application.Current.Resources["searchDelayTimeTips"],
                        TextWrapping = TextWrapping.Wrap
                    },
                    new TextBlock
                    {
                        Text = (string)Application.Current.Resources["homeTitle"],
                        FontSize = 18,
                        Margin = new Thickness(0, 24, 0, 10),
                        TextWrapping = TextWrapping.Wrap
                    },
                    new TextBlock
                    {
                        Text = (string)Application.Current.Resources["homeTips"],
                        TextWrapping = TextWrapping.Wrap
                    }
                }
            },
            PrimaryButtonText = (string)Application.Current.Resources["commonOK"],
            CornerRadius = new CornerRadius(8),
            Style = (Style)Application.Current.Resources["ContentDialog"]
        };

        await helpDialog.ShowAsync();
    }

    private void UpdateEnumDropdownLocalizations()
    {
        DropdownDataGeneric<DisplayMode>.UpdateLabels(DisplayModes);
    }

    private void UpdateDisplayModeFromSelection()
    {
        switch (SelectedDisplayMode)
        {
            case DisplayMode.Priority:
                IsOnOffSelected = false;
                IsPrioritySelected = true;
                IsSearchDelaySelected = false;
                IsHomeOnOffSelected = false;
                break;
            case DisplayMode.SearchDelay:
                IsOnOffSelected = false;
                IsPrioritySelected = false;
                IsSearchDelaySelected = true;
                IsHomeOnOffSelected = false;
                break;
            case DisplayMode.HomeOnOff:
                IsOnOffSelected = false;
                IsPrioritySelected = false;
                IsSearchDelaySelected = false;
                IsHomeOnOffSelected = true;
                break;
            default:
                IsOnOffSelected = true;
                IsPrioritySelected = false;
                IsSearchDelaySelected = false;
                IsHomeOnOffSelected = false;
                break;
        }
    }
}

public enum DisplayMode
{
    OnOff,
    Priority,
    SearchDelay,
    HomeOnOff
}

public class SearchSourceItem : BaseModel
{
    public string Key { get; }
    public string Name => Key switch
    {
        "Application" => "应用",
        "Google" => "谷歌",
        "Bookmarks" => "书签",
        "FileName" => "文件名",
        "Content" => "正文",
        _ => "其他插件"
    };
    public int Position { get; set; }
    public bool CanMoveUp { get; set; }
    public bool CanMoveDown { get; set; }
    public string UpAutomationId => "SourceOrderUp" + Key;
    public string DownAutomationId => "SourceOrderDown" + Key;

    public SearchSourceItem(string key) => Key = key;
}

using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Flow.Launcher.Plugin.NoteContent;

public partial class SettingsControl : UserControl
{
    private readonly Action<SearchSettings> _save;

    internal SettingsControl(SearchSettings settings, Action<SearchSettings> save)
    {
        InitializeComponent();
        _save = save;
        DirectoriesBox.Text = string.Join(Environment.NewLine, settings.SearchDirectories ?? []);
        ExcludedDirectoriesBox.Text = string.Join(Environment.NewLine, settings.ExcludedDirectories ?? []);
        FileTypesBox.Text = string.Join(";", settings.FileGlobs ?? []);
        MaxResultsBox.Text = settings.MaxResults.ToString(CultureInfo.InvariantCulture);
        TimeoutBox.Text = settings.TimeoutSeconds.ToString(CultureInfo.InvariantCulture);
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e) => AddFolders(DirectoriesBox, "添加正文搜索目录");

    private void AddExcludedFolder_Click(object sender, RoutedEventArgs e) => AddFolders(ExcludedDirectoriesBox, "添加正文排除目录");

    private void AddFolders(TextBox target, string title)
    {
        var dialog = new OpenFolderDialog { Title = title, Multiselect = true };
        var owner = Window.GetWindow(this);
        if ((owner == null ? dialog.ShowDialog() : dialog.ShowDialog(owner)) != true) return;
        target.Text = string.Join(Environment.NewLine,
            target.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Concat(dialog.FolderNames).Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!int.TryParse(MaxResultsBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var maximum) ||
                !int.TryParse(TimeoutBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var timeout))
                throw new InvalidDataException("结果上限和超时时间必须填写整数。");
            var settings = new SearchSettings
            {
                SearchDirectories = DirectoriesBox.Text.Split(['\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                ExcludedDirectories = ExcludedDirectoriesBox.Text.Split(['\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                FileGlobs = FileTypesBox.Text.Split([';', '\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                MaxResults = maximum,
                TimeoutSeconds = timeout
            };
            _save(settings);
            StatusText.Text = "已保存，下一次搜索立即生效。";
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            StatusText.Text = "未保存：" + exception.Message;
        }
    }
}

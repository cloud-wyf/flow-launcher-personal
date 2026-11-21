using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Flow.Launcher.Plugin.PersonalBookmarks.Models;

namespace Flow.Launcher.Plugin.PersonalBookmarks.Views;

public partial class BookmarkTagsWindow : Window
{
    private readonly List<BookmarkTagRow> _bookmarks;

    public BookmarkTagsWindow(string selectedUrl = null)
    {
        _bookmarks = Main.GetBookmarks().GroupBy(bookmark => bookmark.Url, StringComparer.Ordinal)
            .Select(group => new BookmarkTagRow(group.First() with
            {
                FolderTags = group.SelectMany(bookmark => bookmark.FolderTags).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            }))
            .OrderBy(bookmark => bookmark.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        InitializeComponent();
        RefreshList(selectedUrl);
        if (Main.Tags.LoadError != null) StatusText.Text = "标签文件读取失败：" + Main.Tags.LoadError;
    }

    private void Filter_Changed(object sender, TextChangedEventArgs e)
    {
        if (BookmarkList != null) RefreshList();
    }

    private void RefreshList(string selectedUrl = null)
    {
        var filter = FilterBox.Text.Trim();
        BookmarkList.ItemsSource = _bookmarks.Where(bookmark =>
            bookmark.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            bookmark.Url.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            bookmark.Tags.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        if (selectedUrl != null)
        {
            BookmarkList.SelectedItem = _bookmarks.FirstOrDefault(bookmark => bookmark.Url == selectedUrl);
            if (BookmarkList.SelectedItem != null) BookmarkList.ScrollIntoView(BookmarkList.SelectedItem);
        }
    }

    private void Bookmark_Changed(object sender, SelectionChangedEventArgs e)
    {
        var bookmark = BookmarkList.SelectedItem as BookmarkTagRow;
        TagsBox.IsEnabled = SaveButton.IsEnabled = bookmark != null;
        SelectedUrl.Text = bookmark?.Url ?? "请选择一个书签";
        AutomaticTags.Text = bookmark == null ? "" : "目录标签：" +
            (bookmark.Bookmark.FolderTags.Length == 0 ? "无" : string.Join("、", bookmark.Bookmark.FolderTags));
        TagsBox.Text = bookmark == null ? "" : string.Join(",", Main.Tags.GetTags(bookmark.Url));
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (BookmarkList.SelectedItem is not BookmarkTagRow bookmark) return;
        try
        {
            Main.Tags.Save(bookmark.Url, TagsBox.Text);
            RefreshList(bookmark.Url);
            StatusText.Text = "已保存，下一次搜索可按标签匹配。";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
        {
            StatusText.Text = "未保存：" + exception.Message;
        }
    }

    public sealed record BookmarkTagRow(Bookmark Bookmark)
    {
        public string Name => Bookmark.Name;
        public string Url => Bookmark.Url;
        public string Tags => string.Join("、", Main.Tags.GetTags(Bookmark));
    }
}

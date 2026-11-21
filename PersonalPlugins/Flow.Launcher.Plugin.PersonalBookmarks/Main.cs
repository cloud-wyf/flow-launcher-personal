using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Controls;
using System.Windows;
using Flow.Launcher.Plugin.PersonalBookmarks.Commands;
using Flow.Launcher.Plugin.PersonalBookmarks.Models;
using Flow.Launcher.Plugin.PersonalBookmarks.Views;
using Flow.Launcher.Plugin.SharedCommands;

namespace Flow.Launcher.Plugin.PersonalBookmarks;

public class Main : ISettingProvider, IPlugin, IReloadable, IPluginI18n, IContextMenu, IDisposable
{
    private static readonly string ClassName = nameof(Main);

    internal static string _faviconCacheDir;

    internal static PluginInitContext Context { get; set; }

    internal static Settings _settings;
    internal static BookmarkTagStore Tags;

    private static List<Bookmark> _cachedBookmarks = new();

    private static bool _initialized = false;
    private static readonly object ReloadLock = new();

    public void Init(PluginInitContext context)
    {
        Context = context;

        _settings = context.API.LoadSettingJsonStorage<Settings>();
        Tags = new BookmarkTagStore(context.CurrentPluginMetadata.PluginSettingsDirectoryPath ?? context.CurrentPluginMetadata.PluginCacheDirectoryPath);
        if (Tags.LoadError != null) context.API.LogError(ClassName, "Failed to read bookmark tags: " + Tags.LoadError);

        _faviconCacheDir = Path.Combine(
            context.CurrentPluginMetadata.PluginCacheDirectoryPath,
            "FaviconCache");

        try
        {
            if (Directory.Exists(_faviconCacheDir))
            {
                var files = Directory.GetFiles(_faviconCacheDir);
                foreach (var file in files)
                {
                    var extension = Path.GetExtension(file);
                    if (extension is ".db-shm" or ".db-wal" or ".sqlite-shm" or ".sqlite-wal")
                    {
                        File.Delete(file);
                    }
                }
            }
        }
        catch (Exception e)
        {
            Context.API.LogException(ClassName, "Failed to clean up orphaned cache files.", e);
        }

        LoadBookmarksIfEnabled();
    }

    private static void LoadBookmarksIfEnabled()
    {
        if (Context.CurrentPluginMetadata.Disabled)
        {
            // Don't load or monitor files if disabled
            return;
        }

        // Validate the cache directory before loading all bookmarks because Flow needs this directory to storage favicons
        FilesFolders.ValidateDirectory(_faviconCacheDir);

        Volatile.Write(ref _cachedBookmarks, BookmarkLoader.LoadAllBookmarks(_settings));
        _ = MonitorRefreshQueueAsync();
        _initialized = true;
    }

    public List<Result> Query(Query query)
    {
        // For when the plugin being previously disabled and is now re-enabled
        if (!_initialized)
        {
            LoadBookmarksIfEnabled();
        }

        string param = query.Search.TrimStart();

        // Should top results be returned? (true if no search parameters have been passed)
        var topResults = string.IsNullOrEmpty(param);

        var onlyTags = param.StartsWith('#');
        return Volatile.Read(ref _cachedBookmarks).Select(bookmark =>
        {
            var score = topResults ? 5 : Tags.MatchScore(bookmark, param);
            if (!topResults && !onlyTags) score = Math.Max(score, BookmarkLoader.MatchProgram(bookmark, param).Score);
            return CreateResult(bookmark, score);
        }).Where(result => result.Score > 0).ToList();
    }

    internal static List<Bookmark> GetBookmarks() => Volatile.Read(ref _cachedBookmarks).ToList();

    private static Result CreateResult(Bookmark bookmark, int score)
    {
        var tags = Tags.GetTags(bookmark);
        var subtitle = bookmark.Url + (tags.Length > 0 ? " · 标签：" + string.Join("、", tags) : "");
        return new Result
        {
            Title = bookmark.Name,
            SubTitle = subtitle,
            SubTitleToolTip = subtitle,
            IcoPath = _settings.EnableFavicons && !string.IsNullOrEmpty(bookmark.FaviconPath) && File.Exists(bookmark.FaviconPath)
                ? bookmark.FaviconPath : @"Images\bookmark.png",
            Score = score,
            CopyText = bookmark.Url,
            RecordKey = bookmark.Url,
            Action = _ => { Context.API.OpenUrl(bookmark.Url); return true; },
            ContextData = new BookmarkAttributes { Url = bookmark.Url }
        };
    }

    private static readonly Channel<byte> _refreshQueue = Channel.CreateBounded<byte>(1);

    private static readonly SemaphoreSlim _fileMonitorSemaphore = new(1, 1);

    private static async Task MonitorRefreshQueueAsync()
    {
        if (_fileMonitorSemaphore.CurrentCount < 1)
        {
            return;
        }
        await _fileMonitorSemaphore.WaitAsync();
        var reader = _refreshQueue.Reader;
        while (await reader.WaitToReadAsync())
        {
            if (reader.TryRead(out _))
            {
                ReloadAllBookmarks(false);
            }
        }
        _fileMonitorSemaphore.Release();
    }

    private static readonly List<FileSystemWatcher> Watchers = new();

    internal static void RegisterBookmarkFile(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!Directory.Exists(directory))
        {
            return;
        }
        var fileName = Path.GetFileName(path);
        if (Watchers.Any(x => x.Path.Equals(directory, StringComparison.OrdinalIgnoreCase) &&
                              x.Filter.Equals(fileName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var watcher = new FileSystemWatcher(directory!)
        {
            Filter = fileName,
            NotifyFilter = NotifyFilters.FileName |
                                   NotifyFilters.LastWrite |
                                   NotifyFilters.Size
        };

        FileSystemEventHandler refresh = static (_, _) =>
        {
            _refreshQueue.Writer.TryWrite(default);
        };
        watcher.Changed += refresh;
        watcher.Created += refresh;
        watcher.Deleted += refresh;

        watcher.Renamed += static (_, _) =>
        {
            _refreshQueue.Writer.TryWrite(default);
        };

        watcher.EnableRaisingEvents = true;

        Watchers.Add(watcher);
    }

    public void ReloadData()
    {
        ReloadAllBookmarks();
    }

    public static void ReloadAllBookmarks(bool disposeFileWatchers = true)
    {
        lock (ReloadLock)
        {
            if (disposeFileWatchers) DisposeFileWatchers();
            LoadBookmarksIfEnabled();
        }
    }

    public string GetTranslatedPluginTitle()
    {
        return Localize.flowlauncher_plugin_browserbookmark_plugin_name();
    }

    public string GetTranslatedPluginDescription()
    {
        return Localize.flowlauncher_plugin_browserbookmark_plugin_description();
    }

    public Control CreateSettingPanel()
    {
        return new SettingsControl(_settings);
    }

    public List<Result> LoadContextMenus(Result selectedResult)
    {
        return new List<Result>()
        {
            new()
            {
                Title = "编辑书签标签",
                SubTitle = "为这个书签添加便于记忆的分类，例如开发、文档、云服务",
                IcoPath = @"Images\bookmark.png",
                Action = _ =>
                {
                    var window = new BookmarkTagsWindow(((BookmarkAttributes)selectedResult.ContextData).Url);
                    var owner = Application.Current?.Windows.Cast<Window>().FirstOrDefault(candidate => candidate.IsActive && candidate != Application.Current.MainWindow);
                    if (owner != null) window.Owner = owner;
                    else window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                    window.ShowDialog();
                    return false;
                }
            },
            new()
            {
                Title = Localize.flowlauncher_plugin_browserbookmark_copyurl_title(),
                SubTitle = Localize.flowlauncher_plugin_browserbookmark_copyurl_subtitle(),
                Action = _ =>
                {
                    try
                    {
                        Context.API.CopyToClipboard(((BookmarkAttributes)selectedResult.ContextData).Url);

                        return true;
                    }
                    catch (Exception e)
                    {
                        Context.API.LogException(ClassName, "Failed to set url in clipboard", e);
                        Context.API.ShowMsgError(Localize.flowlauncher_plugin_browserbookmark_copy_failed());
                        return false;
                    }
                },
                IcoPath = @"Images\copylink.png",
                Glyph = new GlyphInfo(FontFamily: "/Resources/#Segoe Fluent Icons", Glyph: "\ue8c8")
            }
        };
    }

    internal class BookmarkAttributes
    {
        internal string Url { get; set; }
    }

    public void Dispose()
    {
        DisposeFileWatchers();
    }

    private static void DisposeFileWatchers()
    {
        foreach (var watcher in Watchers)
        {
            watcher.Dispose();
        }
        Watchers.Clear();
    }
}

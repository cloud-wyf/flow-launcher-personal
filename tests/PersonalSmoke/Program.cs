using System.IO;
using System.Reflection;
using System.Text.Json;
using Flow.Launcher.Plugin;
using Flow.Launcher.Plugin.PersonalBookmarks;
using Flow.Launcher.Plugin.PersonalBookmarks.Models;
using NotePlugin = Flow.Launcher.Plugin.NoteContent.Main;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var fixture = Path.Combine(Environment.CurrentDirectory, ".artifacts", "smoke-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        var host = typeof(Flow.Launcher.ViewModel.ResultsViewModel).Assembly;
        var presentation = host.GetType("Flow.Launcher.ViewModel.ResultPresentation", true)!;
        var getGroup = presentation.GetMethod("GetGroup")!;
        var label = presentation.GetMethod("Label")!;
        var query = new Query { ActionKeyword = "", SearchTerms = ["PROBE"] };
        typeof(Query).GetProperty("Search")!.SetValue(query, "PROBE");
        typeof(Query).GetProperty("TrimmedQuery")!.SetValue(query, "PROBE");
        var order = new[] { "Application", "Google", "Bookmarks", "FileName", "Content", "Other" };
        foreach (var id in new[] { "0ECADE17459B49F587BF81DC3A125110", "948d4e4b11de51519ea9ff8a4bb8b4e4" })
        {
            var result = new Result { Title = "Alpha" };
            typeof(Result).GetProperty("PluginID")!.SetValue(result, id);
            typeof(Result).GetProperty("OriginQuery", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(result, query);
            Check((int)getGroup.Invoke(null, [result, order])! == 2, "新旧收藏夹应属于同一来源组");
            Check((string)label.Invoke(null, [result])! == "书签匹配", "新收藏夹来源标签应保留");
        }
        var bookmarkPath = Path.Combine(fixture, "AccountBookmarks");
        File.WriteAllText(bookmarkPath, JsonSerializer.Serialize(new
        {
            roots = new { bookmark_bar = new { type = "folder", name = "书签栏", children = new object[]
            {
                new { type = "folder", name = "工作", children = new[] {
                    new { type = "url", name = "Alpha", url = "https://example.invalid" } } },
                new { type = "url", name = "Beta", url = "https://example.invalid/beta" }
            } } }
        }));
        var parser = typeof(ChromiumBookmarkLoader).GetMethod("LoadBookmarksFromFile", BindingFlags.NonPublic | BindingFlags.Static)!;
        var bookmarks = (List<Bookmark>)parser.Invoke(null, [bookmarkPath, "Chrome"] )!;
        Check(bookmarks.Count == 2 && bookmarks[0].FolderTags.SequenceEqual(["工作"]), "账号收藏夹及子目录标签应保留");
        Check(bookmarks[1].FolderTags.Length == 0, "书签栏根目录不应自动生成标签");

        var pluginDirectory = Path.Combine(fixture, "plugin");
        var settingsDirectory = Path.Combine(fixture, "settings");
        var searchDirectory = Path.Combine(fixture, "search");
        var excluded = Path.Combine(searchDirectory, "exclude[1]");
        Directory.CreateDirectory(pluginDirectory);
        Directory.CreateDirectory(excluded);
        File.Copy(Path.GetFullPath(args[0]), Path.Combine(pluginDirectory, "rg.exe"));
        File.WriteAllText(Path.Combine(searchDirectory, "keep.md"), "PROBE PROBE");
        File.WriteAllText(Path.Combine(excluded, "skip.md"), "PROBE");
        var legacySettings = Path.Combine(pluginDirectory, "Settings.json");
        File.WriteAllText(legacySettings, JsonSerializer.Serialize(new
        {
            SearchDirectories = new[] { searchDirectory, excluded },
            ExcludedDirectories = new[] { excluded }, FileGlobs = new[] { "*.md" },
            MaxResults = 200, TimeoutSeconds = 10
        }));
        var metadata = new PluginMetadata { ExecuteFileName = "Flow.Launcher.Plugin.NoteContent.dll", IcoPath = "icon.png" };
        typeof(PluginMetadata).GetProperty("PluginDirectory")!.SetValue(metadata, pluginDirectory);
        typeof(PluginMetadata).GetProperty("PluginSettingsDirectoryPath")!.SetValue(metadata, settingsDirectory);
        var note = new NotePlugin();
        note.InitAsync(new PluginInitContext(metadata, null!)).GetAwaiter().GetResult();
        var persistentSettings = Path.Combine(settingsDirectory, "Settings.json");
        Check(File.ReadAllText(persistentSettings) == File.ReadAllText(legacySettings), "正文设置应迁移到稳定目录");
        var results = note.QueryAsync(query, CancellationToken.None).GetAwaiter().GetResult();
        Check(results.Count == 1 && results[0].CopyText.EndsWith("keep.md"), "正文查询、重叠根和字面目录排除应保留");
        var saved = File.ReadAllText(persistentSettings);
        File.WriteAllText(legacySettings, "{}");
        var next = new NotePlugin();
        next.InitAsync(new PluginInitContext(metadata, null!)).GetAwaiter().GetResult();
        Check(File.ReadAllText(persistentSettings) == saved, "重新初始化不能覆盖迁移后配置");
        Check(next.QueryAsync(query, CancellationToken.None).GetAwaiter().GetResult().Count == 1, "重新初始化后应使用稳定配置");
        Console.WriteLine("PASS: 新旧插件来源兼容、账号收藏夹解析、目录标签、正文查询、配置持久化和目录排除。");
    }

    private static void Check(bool passed, string message)
    {
        if (!passed) throw new InvalidOperationException(message);
    }
}

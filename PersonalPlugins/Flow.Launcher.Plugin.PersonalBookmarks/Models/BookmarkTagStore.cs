using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace Flow.Launcher.Plugin.PersonalBookmarks.Models;

internal sealed class BookmarkTagStore
{
    private readonly string _path;
    private readonly object _saveLock = new();
    private Dictionary<string, string[]> _tags = new(StringComparer.Ordinal);
    public string LoadError { get; private set; }

    public BookmarkTagStore(string directory)
    {
        _path = Path.Combine(directory, "BookmarkTags.json");
        try
        {
            if (!File.Exists(_path)) return;
            var loaded = JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(_path));
            if (loaded == null) throw new InvalidDataException("标签文件为空。");
            _tags = loaded.ToDictionary(pair => pair.Key,
                pair => (pair.Value ?? Array.Empty<string>()).Where(tag => !string.IsNullOrWhiteSpace(tag))
                    .Select(tag => tag.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), StringComparer.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            LoadError = exception.Message;
        }
    }

    public string[] GetTags(string url) => url != null && Volatile.Read(ref _tags).TryGetValue(url, out var tags)
        ? tags : Array.Empty<string>();

    public string[] GetTags(Bookmark bookmark) => bookmark.FolderTags.Concat(GetTags(bookmark.Url))
        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public void Save(string url, string text)
    {
        if (!string.IsNullOrEmpty(LoadError)) throw new InvalidDataException("原标签文件无法读取，未覆盖：" + LoadError);
        if (string.IsNullOrWhiteSpace(url)) throw new ArgumentException("请选择书签。");
        var tags = text.Split(new[] { ',', '，', ';', '；', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        lock (_saveLock)
        {
            var next = new Dictionary<string, string[]>(_tags, StringComparer.Ordinal);
            if (tags.Length == 0) next.Remove(url);
            else next[url] = tags;
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(next, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            File.Move(temporary, _path, overwrite: true);
            Volatile.Write(ref _tags, next);
        }
    }

    public int MatchScore(Bookmark bookmark, string search)
    {
        var tags = GetTags(bookmark);
        var terms = search.TrimStart('#').Split(new[] { ' ', '\t', ',', '，', ';', '；' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length == 0 || !terms.All(term => tags.Any(tag => tag.Contains(term, StringComparison.OrdinalIgnoreCase)))) return 0;
        return terms.All(term => tags.Any(tag => tag.Equals(term, StringComparison.OrdinalIgnoreCase))) ? 1000 : 700;
    }
}

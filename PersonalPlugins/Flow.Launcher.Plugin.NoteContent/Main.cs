using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using Flow.Launcher.Plugin;

namespace Flow.Launcher.Plugin.NoteContent;

public sealed class Main : IAsyncPlugin, ISettingProvider
{
    private PluginInitContext _context = null!;
    private SearchSettings _settings = new();
    private string _directory = "";
    private string _settingsDirectory = "";
    private string? _initializationError;

    public async Task InitAsync(PluginInitContext context)
    {
        _context = context;
        _directory = context.CurrentPluginMetadata.PluginDirectory;
        try
        {
            _settingsDirectory = context.CurrentPluginMetadata.PluginSettingsDirectoryPath;
            Directory.CreateDirectory(_settingsDirectory);
            var path = Path.Combine(_settingsDirectory, "Settings.json");
            var legacyPath = Path.Combine(_directory, "Settings.json");
            if (!File.Exists(path))
            {
                if (File.Exists(legacyPath)) File.Copy(legacyPath, path);
                else await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new SearchSettings()));
            }
            var json = await File.ReadAllTextAsync(path);
            _settings = JsonSerializer.Deserialize<SearchSettings>(json) ?? throw new InvalidDataException("配置为空");
            _settings.Validate();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            _initializationError = exception.Message;
        }
    }

    public System.Windows.Controls.Control CreateSettingPanel() => new SettingsControl(_settings, SaveSettings);

    private void SaveSettings(SearchSettings settings)
    {
        settings.Validate();
        foreach (var directory in settings.SearchDirectories)
            if (!Directory.Exists(directory))
                throw new DirectoryNotFoundException($"搜索目录不存在或无法访问：{directory}");
        var path = Path.Combine(_settingsDirectory, "Settings.json");
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings,
            new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine, new UTF8Encoding(false));
        File.Move(temporaryPath, path, overwrite: true);
        Volatile.Write(ref _settings, settings);
        _initializationError = null;
    }

    public async Task<List<Result>> QueryAsync(Query query, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_initializationError != null) return [Message("内容搜索配置失败", _initializationError)];
        var text = query.Search;
        if (string.IsNullOrWhiteSpace(text))
            return [Message("输入要搜索的正文，例如：内容 额度", "在设置 → 插件 → 笔记内容搜索中配置目录和文件类型")];
        try
        {
            var settings = Volatile.Read(ref _settings);
            var missingDirectory = settings.GetSearchDirectories().FirstOrDefault(path => !Directory.Exists(path));
            if (missingDirectory != null) throw new DirectoryNotFoundException($"搜索目录不存在或无法访问：{missingDirectory}");
            return await SearchAsync(text, settings, token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            return [Message("内容搜索超时", "请在插件设置中缩小搜索范围，或调整超时时间")];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception or JsonException or ArgumentException)
        {
            return [Message("内容搜索失败", exception.Message)];
        }
    }

    private async Task<List<Result>> SearchAsync(string text, SearchSettings settings, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
        var files = new Dictionary<string, FileMatch>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();
        var truncated = false;
        foreach (var directory in settings.GetSearchDirectories())
        {
            timeout.Token.ThrowIfCancellationRequested();
            var startInfo = CreateSearchStartInfo(text, directory, settings);
            using var process = new Process { StartInfo = startInfo };
            process.Start();
            using var cancellation = timeout.Token.Register(() => Stop(process));
            var errorsTask = process.StandardError.ReadToEndAsync();
            try
            {
                while (await process.StandardOutput.ReadLineAsync(timeout.Token) is { } line)
                {
                    using var json = JsonDocument.Parse(line);
                    var root = json.RootElement;
                    var type = root.GetProperty("type").GetString();
                    if (type is not ("match" or "end")) continue;
                    var data = root.GetProperty("data");
                    var path = Path.GetFullPath(JsonText(data.GetProperty("path")), directory);
                    if (type == "end")
                    {
                        if (files.TryGetValue(path, out var completed) && !completed.Complete)
                        {
                            var stats = data.GetProperty("stats");
                            completed.Matches = stats.GetProperty("matches").GetInt64();
                            completed.MatchedLines = stats.GetProperty("matched_lines").GetInt64();
                            completed.Complete = true;
                        }
                        continue;
                    }
                    if (!files.TryGetValue(path, out var file))
                    {
                        if (files.Count >= settings.MaxResults)
                        {
                            truncated = true;
                            continue;
                        }
                        file = new FileMatch(path);
                        files.Add(path, file);
                    }
                    // A repeated or overlapping search root may emit the same file again.
                    if (file.Complete) continue;
                    file.MatchedLines++;
                    file.Matches += data.GetProperty("submatches").GetArrayLength();
                    if (file.Snippets.Count < 3)
                    {
                        var number = data.GetProperty("line_number").GetInt32();
                        var content = JsonText(data.GetProperty("lines")).TrimEnd('\r', '\n');
                        file.Snippets.Add($"第 {number} 行：{Snippet(content, text)}");
                    }
                }
                await process.WaitForExitAsync(timeout.Token);
                timeout.Token.ThrowIfCancellationRequested();
                var error = await errorsTask;
                if (process.ExitCode > 1)
                    errors.Add(string.IsNullOrWhiteSpace(error) ? $"请检查搜索目录和访问权限：{directory}" : error.Trim());
            }
            finally
            {
                Stop(process);
                await process.WaitForExitAsync();
                await errorsTask;
            }
        }
        var results = files.Values.OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
            .Select(CreateFileResult).ToList();
        if (errors.Count > 0) results.Add(Message("部分内容搜索失败", string.Join(Environment.NewLine, errors)));
        if (truncated) results.Add(Message($"已达到 {settings.MaxResults} 个文件的结果上限", "请输入更具体的文字，或在插件设置中调整上限"));
        return results;
    }

    private ProcessStartInfo CreateSearchStartInfo(string text, string directory, SearchSettings settings)
    {
        var startInfo = new ProcessStartInfo(Path.Combine(_directory, "rg.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = directory
        };
        foreach (var argument in new[] { "--no-config", "--json", "--fixed-strings", "--ignore-case", "--hidden", "--no-ignore" })
            startInfo.ArgumentList.Add(argument);
        foreach (var glob in settings.FileGlobs)
        {
            startInfo.ArgumentList.Add("--glob");
            startInfo.ArgumentList.Add(glob);
        }
        startInfo.ArgumentList.Add("--glob");
        startInfo.ArgumentList.Add("!**/.git/**");
        foreach (var excluded in settings.ExcludedDirectories.Where(path => SearchSettings.ContainsDirectory(directory, path)))
        {
            var relative = Path.GetRelativePath(directory, Path.GetFullPath(excluded)).Replace('\\', '/');
            var literal = new StringBuilder();
            foreach (var character in relative)
            {
                if ("\\*?[]{}".Contains(character)) literal.Append('\\');
                literal.Append(character);
            }
            // Anchor to this search root so an identically named folder elsewhere remains searchable.
            foreach (var pattern in new[] { "!/" + literal, "!/" + literal + "/**" })
            {
                startInfo.ArgumentList.Add("--iglob");
                startInfo.ArgumentList.Add(pattern);
            }
        }
        startInfo.ArgumentList.Add("-e");
        startInfo.ArgumentList.Add(text);
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add(".");
        return startInfo;
    }

    private Result CreateFileResult(FileMatch file)
    {
        var details = $"{file.Path}\n{file.Matches} 处匹配，分布在 {file.MatchedLines} 行\n" + string.Join('\n', file.Snippets);
        return new Result
        {
            Title = $"{Path.GetFileName(file.Path)} · {file.Matches} 处匹配",
            SubTitle = file.Snippets[0],
            TitleToolTip = details,
            SubTitleToolTip = details,
            IcoPath = file.Path,
            CopyText = file.Path,
            RecordKey = file.Path,
            Score = 100,
            AddSelectedCount = false,
            Preview = new Result.PreviewInfo { FilePath = file.Path, Description = details },
            Action = _ => Open(file.Path)
        };
    }

    private sealed class FileMatch(string path)
    {
        public string Path { get; } = path;
        public long Matches { get; set; }
        public long MatchedLines { get; set; }
        public bool Complete { get; set; }
        public List<string> Snippets { get; } = [];
    }

    private static void Stop(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (Win32Exception) { }
    }

    private static string JsonText(JsonElement value) => value.TryGetProperty("text", out var text)
        ? text.GetString() ?? ""
        : Encoding.UTF8.GetString(Convert.FromBase64String(value.GetProperty("bytes").GetString()!));

    private static string Snippet(string line, string search)
    {
        var match = line.IndexOf(search, StringComparison.OrdinalIgnoreCase);
        var start = Math.Max(0, match - 45);
        var length = Math.Min(Math.Max(180, search.Length), line.Length - start);
        return (start > 0 ? "…" : "") + line.Substring(start, length).Replace('\t', ' ') +
            (start + length < line.Length ? "…" : "");
    }

    private Result Message(string title, string detail) => new()
    {
        Title = title,
        SubTitle = detail,
        SubTitleToolTip = detail,
        IcoPath = "icon.png",
        Score = 0,
        AddSelectedCount = false,
        Action = _ =>
        {
            _context.API?.OpenSettingDialog();
            return false;
        }
    };

    private bool Open(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            _context.API?.ShowMsgError("无法打开文件", exception.Message);
            return false;
        }
    }
}

internal sealed class SearchSettings
{
    public string[] SearchDirectories { get; set; } = [Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)];
    public string[] ExcludedDirectories { get; set; } = [];
    public string[] FileGlobs { get; set; } = ["*.md", "*.markdown", "*.txt"];
    public int MaxResults { get; set; } = 200;
    public int TimeoutSeconds { get; set; } = 10;

    public void Validate()
    {
        if (SearchDirectories is not { Length: > 0 } ||
            SearchDirectories.Any(path => string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) ||
            ExcludedDirectories == null || ExcludedDirectories.Any(path => string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) ||
            FileGlobs is not { Length: > 0 } || FileGlobs.Any(string.IsNullOrWhiteSpace) ||
            MaxResults is < 1 or > 2000 || TimeoutSeconds is < 1 or > 60)
            throw new InvalidDataException("请填写绝对目录、至少一个文件类型、结果上限（1–2000）和超时秒数（1–60）");
    }

    public string[] GetSearchDirectories() => SearchDirectories.Select(Path.GetFullPath)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Where(path => !ExcludedDirectories.Any(excluded => ContainsDirectory(excluded, path))).ToArray();

    public static bool ContainsDirectory(string parent, string child)
    {
        parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        child = Path.TrimEndingDirectorySeparator(Path.GetFullPath(child));
        return child.Equals(parent, StringComparison.OrdinalIgnoreCase) ||
            child.StartsWith(Path.EndsInDirectorySeparator(parent) ? parent : parent + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
    }
}

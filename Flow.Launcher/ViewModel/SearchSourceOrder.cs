using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Flow.Launcher.Infrastructure.Storage;
using Flow.Launcher.Infrastructure.UserSettings;

namespace Flow.Launcher.ViewModel
{
    public class SearchSourceOrder
    {
        public static readonly string[] DefaultSources = { "Application", "Google", "Bookmarks", "FileName", "Content", "Other" };
        public List<string> Sources { get; set; } = DefaultSources.ToList();

        public static List<string> Normalize(IEnumerable<string> sources)
        {
            return (sources ?? Enumerable.Empty<string>()).Where(DefaultSources.Contains)
                .Concat(DefaultSources).Distinct().ToList();
        }
    }

    internal sealed class SearchSourceOrderStore
    {
        private static readonly Lazy<SearchSourceOrderStore> Instance = new(() => new SearchSourceOrderStore(
            Path.Combine(DataLocation.DataDirectory(), "Settings", "SearchSourceOrder.json")));
        public static SearchSourceOrderStore Current => Instance.Value;

        private readonly JsonStorage<SearchSourceOrder> _storage;
        private readonly SearchSourceOrder _data;
        private volatile string[] _sources;
        public IReadOnlyList<string> Sources => _sources;

        public SearchSourceOrderStore(string path)
        {
            _storage = new JsonStorage<SearchSourceOrder>(path);
            _data = _storage.Load();
            _sources = SearchSourceOrder.Normalize(_data.Sources).ToArray();
        }

        public void Save(IEnumerable<string> sources)
        {
            var normalized = SearchSourceOrder.Normalize(sources);
            var previous = _data.Sources;
            _data.Sources = normalized;
            try
            {
                _storage.Save();
                _sources = normalized.ToArray();
            }
            catch
            {
                _data.Sources = previous;
                throw;
            }
        }
    }
}

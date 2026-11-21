using System;
using System.Collections.Generic;
using Flow.Launcher.Plugin;

namespace Flow.Launcher.ViewModel
{
    internal static class ResultPresentation
    {
        public static int GetGroup(Result result, IReadOnlyList<string> order)
        {
            var query = result?.OriginQuery;
            if (query == null || query.IsHomeQuery || !string.IsNullOrEmpty(query.ActionKeyword))
                return 0;

            var source = GetSource(result);
            for (var index = 0; index < order.Count; index++)
                if (order[index] == source) return index;
            return order.Count;
        }

        public static string Label(Result result)
        {
            if (result?.OriginQuery == null || result.OriginQuery.IsHomeQuery)
                return string.Empty;

            return GetSource(result) switch
            {
                "Application" => "应用匹配",
                "Google" => "谷歌搜索",
                "Bookmarks" => "书签匹配",
                "FileName" => "文件名匹配",
                "Content" => "文件内容匹配",
                _ => string.Empty
            };
        }

        private static string GetSource(Result result)
        {
            if (string.Equals(result.PluginID, "791FC278BA414111B8D1886DFE447410", StringComparison.OrdinalIgnoreCase))
                return "Application";
            if (string.Equals(result.PluginID, "565B73353DBF4806919830B9202EE3BF", StringComparison.OrdinalIgnoreCase)
                && result.ContextData is string url && Uri.TryCreate(url, UriKind.Absolute, out var uri)
                && (uri.Host.Equals("www.google.com", StringComparison.OrdinalIgnoreCase)
                    || uri.Host.Equals("google.com", StringComparison.OrdinalIgnoreCase)))
                return "Google";
            if (string.Equals(result.PluginID, "572be03c74c642baae319fc283e561a8", StringComparison.OrdinalIgnoreCase))
                return "FileName";
            if (string.Equals(result.PluginID, "0ECADE17459B49F587BF81DC3A125110", StringComparison.OrdinalIgnoreCase)
                || string.Equals(result.PluginID, "948d4e4b11de51519ea9ff8a4bb8b4e4", StringComparison.OrdinalIgnoreCase))
                return "Bookmarks";
            if (string.Equals(result.PluginID, "c307b936798d447a9218ed8987fa2734", StringComparison.OrdinalIgnoreCase))
                return "Content";
            return "Other";
        }
    }
}

using System.Collections.Generic;
using System.IO;
using Flow.Launcher.Plugin.PersonalBookmarks.Models;

namespace Flow.Launcher.Plugin.PersonalBookmarks;

public class CustomFirefoxBookmarkLoader : FirefoxBookmarkLoaderBase
{
    public CustomFirefoxBookmarkLoader(CustomBrowser browser)
    {
        BrowserName = browser.Name;
        BrowserDataPath = browser.DataDirectoryPath;
    }

    /// <summary>
    /// Path to places.sqlite
    /// </summary>
    public string BrowserDataPath { get; init; }

    public string BrowserName { get; init; }

    public override List<Bookmark> GetBookmarks()
    {
        return GetBookmarksFromPath(Path.Combine(BrowserDataPath, "places.sqlite"));
    }
}

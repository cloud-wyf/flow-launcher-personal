using Flow.Launcher.Plugin.PersonalBookmarks.Models;
using System.Collections.Generic;

namespace Flow.Launcher.Plugin.PersonalBookmarks;

public interface IBookmarkLoader
{
    public List<Bookmark> GetBookmarks();
}

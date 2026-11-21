# 个人维护版

本仓库基于官方 `v2.1.4`，保留完整上游 Git 历史。主程序、官方插件源码和个人插件放在同一仓库，使用 `main` 维护。

## 已导入的累积功能

- Chrome `AccountBookmarks` 兼容、目录标签、手动标签、标签搜索、收藏夹内容匹配、同目录多文件监听。
- 全局来源标签与排序；应用、谷歌、收藏夹、文件名、正文、其他六组顺序可保存、复位；专用关键词和首页不分组。
- 笔记正文搜索、原生设置界面、目录排除、特殊字符转义、重叠目录去重、共享超时和结果上限。
- 设置页面缓存、启动后真窗口预热、窗口复用、按页就绪、只布局当前页、滚动和草稿复位、卡顿日志。

导入来源和原始文件哈希见 `import-provenance.json`。源码来自已经部署的最后一版主程序和收藏夹/正文插件快照。

## 插件身份

| 插件 | 位置 | 维护方式 |
|---|---|---|
| 官方收藏夹 | `Plugins/Flow.Launcher.Plugin.BrowserBookmark` | 保留官方源码，个人版本默认禁用 |
| 个人收藏夹 | `PersonalPlugins/Flow.Launcher.Plugin.PersonalBookmarks` | 独立 ID 和程序集；配置单独存储 |
| 笔记内容搜索 | `PersonalPlugins/Flow.Launcher.Plugin.NoteContent` | 保留已有独立 ID，配置从插件目录迁到标准配置目录 |

个人插件清单在 `PersonalPlugins/plugins.json`。来源分类同时识别官方和个人收藏夹 ID，便于兼容旧记录。官方插件更新列表按 ID 匹配，个人收藏夹不会被当作官方收藏夹更新。

## 构建与验证

安装 .NET 9 SDK、Python 3 和 Git。仓库不需要本机已有 Flow 或历史 scratch 目录。

```powershell
./Scripts/build-personal.ps1
python ./Scripts/verify-personal.py
python ./Scripts/test-migration.py
python ./Scripts/package-personal.py
```

首次生成包会从 ripgrep 官方 GitHub Release 下载固定的 15.2.0 Windows 版本，并核对固定 SHA-256；插件包附带对应许可文件。

依赖版本沿用 v2.1.4；第一次会恢复 NuGet 包。构建产物在 `Output/Release`，包在 `.artifacts/packages`。重复打包使用新的 bundle 目录，最新路径记录在 `.artifacts/latest-bundle.json`，不会删除旧产物。GitHub Actions 使用 Windows 构建，提供便携包和两个独立插件包，构建产物保留 3 天以减少私有仓库存储占用。原始上游工作流保留在历史中，个人分支不触发上游网站或包管理器发布。

## 安装和迁移

退出 Flow 后运行 `python Scripts/install-personal.py --app "你的程序目录" --bundle "解压后的便携包目录"`。

- 先保存程序和配置备份，检查进程已退出、程序版本兼容和包哈希。
- 替换个人主程序及依赖，复制独立插件到 `UserData/Plugins`；保留原版收藏夹插件文件，将其禁用。
- 克隆原版收藏夹的关键词、优先级等设置到新 ID，迁移收藏夹设置及手动标签；已有新配置不覆盖。
- 将置顶和历史记录里的旧收藏夹 `PluginID` 映射到新 ID。
- 笔记内容插件的搜索设置迁移到 `UserData/Settings/Plugins/Flow.Launcher.Plugin.NoteContent/Settings.json`。搜索内容和用户配置不会进入 Git。
- 安装失败恢复修改过的文件。安装不会强制结束或自动操作界面；验收由用户实际搜索和打开设置完成。

## 官方升级

`upstream` 指向 `https://github.com/Flow-Launcher/Flow.Launcher.git`；`origin` 指向个人 GitHub 仓库。

准备升级时先 fetch 上游，比较版本差异，再在单独升级分支合并对应 tag，保留本文件列出的累积功能。解决冲突后构建并验证收藏夹、正文、来源顺序和设置窗口，最后发布个人版本。不要把旧版 DLL 直接复制到官方新版。

应用内“检查更新”打开个人仓库的 Releases，后台更新不会下载官方版本。当前仓库为私有，下载资产需要 GitHub 登录；可以从 Actions 获取构建结果，或用 GitHub CLI 下载个人发布包。普通插件继续使用官方插件源，个人插件由本仓库单独构建和安装。

## 发布

推送 `main` 只触发构建，不发布。确认验证后，在 GitHub 运行个人构建工作流并填写 `release_tag`（如 `personal-v2.1.4.1`），工作流会构建后创建草稿 Release，附便携包及插件 zip。检查草稿后再发布。

## 验收限制

以前设置窗口测试中的离屏/隐形数字不能代替实际操作体验。安装后由用户检查：设置首开、七页切换、缩放、重开、插件展开、下拉弹出；对照真实日志。保留启动预热期间短暂占用 UI 线程的已知边界。

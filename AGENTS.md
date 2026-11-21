# Flow Launcher 个人维护版

- 使用简体中文沟通。主程序和个人插件在同一仓库维护，保留官方 v2.1.4 的 Git 历史。
- `upstream` 是 Flow-Launcher/Flow.Launcher；`origin` 是 cloud-wyf/flow-launcher-personal。
- 个人功能清单和迁移规则见 `docs/personal/maintenance.md`。修改前检查当前累积功能，避免遗漏。
- 个人收藏夹和笔记内容插件位于 `PersonalPlugins/`。不要改回官方 ID、程序集或更新来源。
- 不提交用户配置、搜索内容、缓存、日志、凭据和安装备份。
- 构建用 `Scripts/build-personal.ps1`；不依赖某台电脑的已安装 DLL，不在构建中结束运行中的 Flow。
- 部署须检查进程已退出，保存备份，校验文件，失败回滚。用户负责托盘退出和界面操作；不得自动点击、输入或截图。
- 合并上游时保留个人修改，明确报告无法兼容的功能，再按维护文档验证。
- 未经用户主动要求，不提交、推送或发布。当前初始化仓库和 GitHub 同步属于已授权任务。

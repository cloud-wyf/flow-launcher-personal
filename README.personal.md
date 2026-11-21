# Flow Launcher 个人维护版

基于官方 v2.1.4，保留此前所有已部署的主程序、收藏夹和正文搜索修改。

- 主程序保持官方 Git 历史，通过 `upstream` 合并更新，个人版本通过 `origin` 维护。
- 个人收藏夹使用独立插件 ID 和程序集，不覆盖官方插件。
- 笔记内容搜索沿用已有独立 ID，配置采用标准用户目录。
- 不上传用户配置、缓存、日志或搜索内容。

构建、安装迁移、升级和发布步骤见 [维护文档](docs/personal/maintenance.md)。

```powershell
./Scripts/build-personal.ps1
python ./Scripts/verify-personal.py
python ./Scripts/test-migration.py
python ./Scripts/package-personal.py
```

上游 README 和 MIT 许可保留。本仓库是个人维护版本。

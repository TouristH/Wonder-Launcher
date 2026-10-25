# 0.4.1 实例分层与启动修正

日期：2026-10-23。依据用户对实例名称、目录分层和 26.2 离线启动失败的反馈实施。

## 行为与实施范围

1. 保留内部 `VersionId` 作为版本目录/启动标识；增加实际 `GameVersion` 与显示用 `VersionLabel`。列表展示可编辑名称、Minecraft 版本及路径，无法识别时明确显示“版本待识别”。从已有索引加载时只补充版本显示信息，保留自定义名称与启动覆盖值。
2. 管理器首页左侧列出游戏根目录，右侧仅显示选中目录内的实例。根目录切换不改变桌面启动组件的当前实例；选择子实例才切换启动目标并进入详情页。返回首页保留目录位置及当前窗口中的未保存草稿。
3. 详情页提供名称和启动设置、重新读取来源、移除登记、离线身份、启动及日志。移除登记不删除游戏文件。设置仍需保存，关闭管理窗口前应保存草稿。
4. Minecraft 的日志 XML 为可选配置，不能与客户端 JAR、库和资产等必需文件一起作强制缺失检查。原日志配置存在则使用原文件；已知 Log4j2 配置缺失时在 Wonder 缓存创建本地控制台配置，并记录提示。未知日志格式缺失则使用游戏默认日志设置。

## 26.2 的实际原因与验证

用于验证的本地 26.2 实例具有完整的游戏依赖，元数据声明的 `assets/log_configs/client-1.21.2.xml` 没有被原启动器缓存。0.4.0 将它当作必需文件，导致启动计划在运行 Java 前失败。这是 Wonder 启动检查的问题。公开文档使用示例路径，不包含用户实际游戏路径。

使用新增 `tests/Wonder.InstanceProbe` 通过正式的 `InstanceDiscovery` 和 `LocalGameLauncher` 读取该实例并实际启动 Java 25。验证使用独立的测试游戏目录，客户端、库和资产来自用户实例；不加载或改写原实例存档和游戏设置。

实测结果：窗口标题 `Minecraft 26.2`，日志出现声音引擎初始化完成，窗口正常关闭，退出码 0。证据位于 `artifacts/instances-0.4.1/real-26.2/game-result.json`、`game-log.txt` 和 `plan.json`。这是实际游戏启动验证，不代表所有整合包组合均通过，也不包含进入世界的验收。

## 验证命令

```powershell
dotnet build Wonder.slnx -c Release --nologo
dotnet test Wonder.slnx -c Release --logger "trx;LogFilePrefix=final-0.4.1" --results-directory artifacts/instances-0.4.1/test-results
dotnet run --project tests/Wonder.InstanceProbe/Wonder.InstanceProbe.csproj -c Release -- "D:\ExampleGames\.minecraft\versions\26.2" "artifacts/instances-0.4.1/real-26.2" --run
dotnet publish src/Wonder.Desktop/Wonder.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o artifacts/Wonder-win-x64
```

回归覆盖可选日志缺失、版本识别和旧索引读取、两层目录切换、详情导航、未保存草稿、移除登记及组件联动。发布前保留现有 `WonderData`，验证发布不改变用户数据。

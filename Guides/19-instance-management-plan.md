# 实例管理与启动实施计划

执行：原拟并行拆分模型、来源识别和启动基础设施。子任务触发额度限制后，保留已产出的模型、接口与测试，由当前聊天完成应用服务、导入器、启动器、桌面集成和代码检查。本轮未获得独立代理的最终审查。

目标：导入既有实例、读取并覆盖启动设置，通过独立组件离线启动。设计见 [18-instance-management-and-launch.md](18-instance-management-and-launch.md)。技术为 .NET 10、Avalonia 12.1.1、System.Text.Json、System.Diagnostics.Process 和现有 xUnit；不恢复前身工程实现。

## 任务与接口

- [x] 模型和应用服务：新增 Wonder.Core、Wonder.Application；测试设置优先级、目录持久化失败、重复导入、并发启动、快照与隐藏独立性。公开 GameInstance、InstanceSettings、InstanceCatalog、DiscoveryResult、LaunchRequest、LaunchPlan；应用接口 IInstanceDiscovery、IInstanceStore、IGameLauncher，服务 InstanceService。
- [x] 导入与存储：Wonder.Infrastructure/Instances；标准、HMCL 新旧、PCL、Prism/MultiMC 合成夹具，来源诊断、稳定身份与原子存储。未知自动策略显示诊断，不默认为已还原。
- [x] 启动：Wonder.Infrastructure/Launching；继承循环、平台规则、库覆盖、参数转义、Java/依赖缺失与 native 归档边界测试，本地 LaunchPlan 与进程执行。不执行外部启动脚本。
- [x] 桌面集成：MainWindow 装配共享服务，InstanceManagerWindow 与两个注册组件；异步目录选择、设置保存、共享选择、启动状态、隐藏后恢复和五组件目录测试。
- [x] Headless 截图检查与实际 Java 合成进程验证。
- [x] 完整回归最终结果与 Release 发布记录。
- [x] 更新 Guides/Wonder.md，Release 构建和 Windows x64 单文件发布，记录实际支持边界与验证结果。

## 审查重点

1. 原目录的 launcher 配置和账号数据不被写入、迁移或用于执行脚本。
2. 数据读取失败不会自动覆盖原索引；刷新不覆盖 Wonder 细调值。
3. 导入多版本、多实例与多个组件不相互覆盖身份；更换选择不影响在途启动快照。
4. 实例启动依赖缺失、未知 Prism 组件、继承循环、非法路径明确失败。
5. 关闭窗口或隐藏组件不误取消游戏；日志和异步回调不持有已销毁控件。

## 验证命令

```powershell
dotnet test Wonder.slnx -c Release --logger "trx;LogFilePrefix=final-0.4.0" --results-directory artifacts/instances-0.4.0/test-results
dotnet build Wonder.slnx -c Release
dotnet run --project tests/Wonder.JavaSmoke/Wonder.JavaSmoke.csproj -c Release -- "<JDK 的 bin 目录>" "artifacts/instances-0.4.0"
dotnet publish src/Wonder.Desktop/Wonder.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o artifacts/Wonder-win-x64
```

`Wonder.Game.Tests` 使用注入的 Java 探测结果，普通单元测试不依赖机器安装 Java。`Wonder.JavaSmoke` 单独使用本地 JDK 编译最小测试类，并经完整发现、计划构造和进程路径运行；验证中文/空格路径、离线参数、参数空格保留、UTF-8 stdout/stderr 与退出码。

界面截图位于 `artifacts/instances-0.4.0/instance-manager.png`、`game-components.png`、`library.png`。已检查表单、固定保存区、两个组件及五组件目录。Headless 不等于真实系统目录选择器和真实游戏画面的人工验收。

初次真实 Java 检查暴露 stdout/stderr 使用 GBK 而接收端按 UTF-8 解码；按实际 Java 属性定位后统一标准流编码，重新运行通过。组件目录的“设置”搜索现也应匹配描述含“设置”的实例管理，测试已按公开搜索语义更新。

## 最终结果 · 2026-10-22

- `dotnet test Wonder.slnx -c Release`：**516 通过，0 失败**（Workspace 208、Desktop 256、Game 52）。最终 TRX 文件以 `final-0.4.0` 开头。
- Release 构建：**0 警告、0 错误**；Windows x64 自包含单文件发布成功，未添加新的 NuGet 依赖。
- 本地 JDK 25.0.4 编译并运行合成 Java 程序，中文/空格路径、引号参数、离线身份、双输出流与正常退出验证通过。结果为 `java-smoke.json`；**没有使用真实 Minecraft 实例验证游戏画面**。
- 将发布 EXE 和已有 Wonder 布局/设置复制到隔离临时目录，从另一工作目录启动。主窗口出现、五种组件登记、正常关闭、退出码 0；记录为 `exe-smoke.json`。
- 发布产物：`artifacts/Wonder-win-x64/Wonder.Desktop.exe`，101,822,874 字节；SHA256 `7DDF550B4420B1218A38A9AFC81AC4D551D854C9DD9F0FE73AE8E334016B06BF`。
- 发布前后的实际 `WonderData/settings.json`、`workspace.json` 校验值一致；没有写入测试实例到用户的实例索引。上一版 EXE 保留在既有交接备份目录 `wonder-handoff-20261021-013347/previous-builds`。

机器可读汇总、校验值与现有数据哈希见 `artifacts/instances-0.4.0/verification.json`，构建和发布日志为同目录的 `build.log`、`publish.log`。这些结论仅覆盖本次 Windows x64 环境及列出的合成配置，不等同于所有启动器版本和所有模组组合均可启动。

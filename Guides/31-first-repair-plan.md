# Wonder 第一阶段修复计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** 修复审查确认的 M1、M2、M3，保护工作区编辑、已轮换凭据和官方 profile 的稳定身份。

**Architecture:** 在桌面入口读取任何用户数据前取得按数据目录隔离的跨进程写入锁；将已验证的凭据轮换提交与可取消的外观查询分离；使用来源目录与官方 profile ID 匹配更新实例。保持现有分层、数据格式和组件功能。

**Tech Stack:** C#、.NET 10、Avalonia、xUnit。

**Spec:** `artifacts/Wonder-review-repair-index-2026-10-26.md` 的 M1–M3 与第一阶段路线图；基线 `e5246c8`。

## Global Constraints

- 直接修改当前主工作目录，不新建 worktree；不提交或推送。
- 保留全部现有功能和真实 WonderData；测试只使用合成账户、布局与实例。
- 不迁移数据格式、不恢复前身兼容层、不改变项目许可。
- 完整构建和测试当前 Wonder.slnx，发布 artifacts/Wonder-win-x64；核对发布前后用户数据哈希。

## Review Focus

- 同一目录的大小写、相对路径和目录别名不能产生两个写入者。
- 主进程异常终止后允许重新启动；启动错误不能绕过保护。
- 取消操作不能丢失已通过身份校验的新凭据，也不能继续启动游戏。
- 轮换后保存失败必须明确报告，并保留可重试的有效凭据。
- 同版本的不同 profile 和独立版本目录不能误合并；已有重复登记不自动删除。

## Task 1: M1 数据目录单写者

**Files:** `src/Wonder.Desktop/Program.cs`、新增 `src/Wonder.Desktop/LauncherInstanceGuard.cs`、新增 `tests/Wonder.Desktop.Tests/LauncherInstanceGuardTests.cs`。

**Interfaces:** 入口在创建 Avalonia 应用前调用 `LauncherInstanceGuard.TryAcquire`；所有者持有可释放锁直到桌面生命周期结束。第二进程请求激活已有窗口后退出，不读取或保存布局。

- [x] 写同目录阻止第二写者、不同目录独立、释放和崩溃后重开、路径别名与失败关闭测试，并观察失败。
- [x] 使用数据目录内持有的文件句柄实施排他写入；第二次启动激活既有进程，权限或 I/O 错误显示说明并退出。
- [x] 运行定向测试及独立双进程合成目录验收，确认旧会话不能覆盖新内容。

## Task 2: M2 凭据轮换提交

**Files:** `src/Wonder.Infrastructure/Accounts/YggdrasilAuthenticator.cs`、`YggdrasilAccountAppearance.cs`、`src/Wonder.Application/AccountService*.cs`、对应账户测试。

**Interfaces:** 认证层返回经身份校验的刷新结果；服务先提交轮换凭据，再允许取消后续可选操作。保持现有登录和启动接口语义。

- [x] 写响应前取消、轮换后取消、外观查询取消、重建服务重试、身份不匹配、保存失败的回归测试并观察失败。
- [x] 最小分离刷新和外观流程；保存失败保留可恢复的新凭据并报告，新增登录取消仍不登记。
- [x] 运行账户及启动相关回归，确认取消不启动游戏。

## Task 3: M3 官方 profile 稳定身份

**Files:** `src/Wonder.Application/InstanceFileOperations.cs`、`InstanceService.cs`、必要的发现代码及 `tests/Wonder.Game.Tests/InstanceServiceTests.cs` / `InstanceDiscoveryTests.cs`。

**Interfaces:** `SameSource` 对带 profile ID 的官方来源按规范化来源目录和 profile ID 匹配；版本是可更新属性。

- [x] 写同 profile 换版刷新与重导入保留 ID/名称/覆盖/选择的失败测试；覆盖不同 profile、独立版本目录和其他来源。
- [x] 最小修复匹配与更新逻辑；不自动合并已有重复登记。
- [x] 运行实例发现、刷新与目录操作回归。

## Task 4: 交付验证

- [x] 已尝试独立审查；审查代理因额度限制未完成，由主代理重新检查全部生产改动、关键测试和文档，未发现需阻止交付的重要问题。本次不计作独立审查通过。
- [x] 更新 README、Wonder.md 和对应 Guides 使用说明及修复记录。
- [x] `dotnet build Wonder.slnx -c Release` 与 `dotnet test Wonder.slnx -c Release`，保存日志与 TRX。
- [x] 保留发布目录 WonderData，按 README 的命令发布 Windows x64 单文件，核对数据哈希和产物。
- [x] 在 `artifacts/Wonder-first-repair-2026-10-27/` 记录完成证据与未覆盖边界；M4、M5 和其他后续项继续保留在索引中。

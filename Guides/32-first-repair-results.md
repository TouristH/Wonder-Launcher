# 第一阶段修复与验收记录

日期：2026-10-27。基于 0.7.2 / `e5246c8` 的本地修复，版本号未改动，尚未提交或推送。范围来自审查聊天和 `artifacts/Wonder-review-repair-index-2026-10-26.md` 的第一阶段：M1、M2、M3。

## 已修复行为

| 编号 | 现在的行为 | 主要代码 |
| --- | --- | --- |
| M1 | 在读取用户数据前持有数据目录内 `.instance.lock` 的独占写句柄；第二进程激活已有窗口后退出，无法激活时提示已有实例。不同目录独立；目录别名不能产生第二个写入者；异常终止后系统释放锁。 | `Program.cs`、`LauncherInstanceGuard.cs` |
| M2 | Yggdrasil 只返回已校验的刷新结果；服务验证身份和有效期后先提交凭据，再进行可取消的外观查询。取消不启动游戏。保存失败保留内存新凭据，下次操作先重试保存。 | `AccountService.cs`、`AccountService.Management.cs`、`AccountServices.cs`、`YggdrasilAuthenticator.cs`、`YggdrasilAccountAppearance.cs` |
| M3 | 官方 profile 按规范化来源目录与 profile ID 匹配，版本作为可更新属性；保留 Wonder ID、名称、覆盖设置和选择。显式刷新优先指定条目，导入优先精确 ID，来源匹配多义时拒绝写入。 | `InstanceFileOperations.cs`、`InstanceService.cs` |

没有变更账户或工作区的数据格式，没有重置或迁移现有数据，没有删除原有组件、功能区、实例管理和启动功能。官方实例候选 ID 的生成算法保持，旧登记由来源匹配更新。

如果凭据保存失败，请保持启动器打开，恢复数据目录写入权限或磁盘空间后重试刷新/启动。尚未成功保存就退出时，下次可能需要重新登录。取消时尚未完整收到并验证的远端响应不在本地可恢复范围内；本次修复不声称解决服务器已轮换而响应在网络中丢失的情况。

## 验证证据

所有新测试只用合成文件、账户和 HTTP handler，未使用真实账号或游戏数据作为夹具。日志和 TRX 位于 `artifacts/Wonder-first-repair-2026-10-27/`。

| 验证 | 结果 |
| --- | --- |
| M1 真实子进程回归 | 无锁桩 8 失败/2 通过；修复后 10/10。实际观察到旧快照覆盖新标题；覆盖同目录、独立目录、正常退出、强制终止、大小写/相对路径/junction、无法写锁与无法读取元信息。 |
| M2 回归 | 修复前 4 失败/4 通过；修复后 8/8。覆盖响应前取消、完整响应后取消、外观取消、重建服务、身份/客户端不一致、保存失败重试及新增登录取消。 |
| M2 启动串联 | `InstanceService + AccountService + YggdrasilAuthenticator` 取消串联通过，凭据已保存且未调用游戏启动计划。账户相关 124/124 通过。 |
| M3 回归 | 修复前 8 失败/3 通过；修复后相关 61/61。覆盖换版本、旧 profile 字段、重复登记、不同 profile 共用版本、独立版本、Prism/MultiMC 及目录移动。 |
| Release 构建 | `dotnet build Wonder.slnx -c Release --nologo`：0 警告、0 错误。 |
| 完整测试 | `dotnet test Wonder.slnx -c Release --no-build --logger "trx;LogFilePrefix=first-repair" --results-directory artifacts/Wonder-first-repair-2026-10-27/test-results --nologo`：**833 通过、0 失败、0 跳过**；Workspace 210、Game 314、Desktop 309。包含 Windows 回收站测试。 |
| 单文件发布 | 使用 README 中的 Windows x64 自包含单文件命令成功发布。 |
| 发布 EXE 冒烟 | 把新 EXE 复制到独立空便携目录：主窗口建立，第二进程退出码 0、无需关闭提示框，第二次启动未改数据；主进程正常关闭后锁可重新获取。结果见 `published-smoke.json`。 |
| 用户数据 | 发布前后原有 WonderData 的 32 个文件路径、长度及 SHA-256 一致。测试数据仅位于独立测试目录。 |

三项共新增 30 个测试。定向测试与完整测试包含重叠，不能将两者数字相加。发布冒烟确认进程/文件行为，没有执行桌面截图或原生前台焦点人工验收。

## 本地成果

- 程序：`artifacts/Wonder-win-x64/Wonder.Desktop.exe`。
- 文件大小：102,146,970 字节。
- SHA-256：`007DA3654539B7628DDA26C747268D9AFA77460324257DECF6C6027082E56186`。
- 旧 EXE 恢复副本：`artifacts/Wonder-first-repair-2026-10-27/before-publish/Wonder.Desktop.exe`，不包含复制的用户数据。
- README、Wonder.md、15/18/26 号帮助文档已同步；发布包同步保留项目许可、第三方原始许可与通知。

## 复核与下一步

独立审查代理因额度限制未能完成；本次由主代理重新检查全部生产改动、关键测试和文档，未发现需要阻止本地交付的重要问题，**不计作独立审查通过**。

M1 的窗口激活属于尽力操作：Windows 拒绝前台切换或窗口尚未就绪时显示提示并退出，仍保持单写者。该锁只能约束使用本次保护机制的版本；更新前应关闭旧版本进程。

M2 发生磁盘持久性故障时无法保证进程退出后的新凭据恢复，故明确提示保持程序开启并重试；没有将未验证身份保存以绕过此限制。M3 对已有重复登记不自动删除，避免丢失不同覆盖设置。

仍待后续修复：M5 第三方纹理访问边界、R1 实例索引持久性加固、M4 便笺写盘合并，以及审查索引中的低优先级与工程条目。本轮未进行真实 Microsoft/Yggdrasil 登录、真实游戏启动、内网访问、断电注入或原生读屏/DPI 验收，不代表 AppID 审核通过。

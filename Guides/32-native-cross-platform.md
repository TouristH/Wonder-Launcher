# 同源原生三端发行

适用版本：Wonder Launcher 0.7.2。Windows、Linux、macOS 共用 `Wonder.slnx` 和 `src/Wonder.*`，桌面、组件、账户业务、实例管理及启动计划均由同一份代码实现。系统凭据保护、回收站、路径规则和 Java/游戏原生库选择按宿主系统处理。

组织方式参考 HMCL 的[平台检测与 Java 选择](https://github.com/HMCL-dev/HMCL/blob/main/HMCLCore/src/main/java/org/jackhuang/hmcl/util/platform/OperatingSystem.java)及[平台支持表](https://github.com/HMCL-dev/HMCL/blob/main/docs/PLATFORM.md)，没有复制 HMCL 代码。Wonder 继续使用 .NET 10 与 Avalonia，不改为 Java/JavaFX，也不借用 HMCL 的已验证平台范围。

## 应用与源码

| 目标 | 应用产物 | 启动入口 |
| --- | --- | --- |
| Windows x64 | `Wonder-0.7.2-win-x64.exe` | 双击该 EXE |
| Linux x64 | `Wonder-0.7.2-linux-x64` | `chmod +x Wonder-0.7.2-linux-x64` 后执行 `./Wonder-0.7.2-linux-x64` |
| macOS Intel | `Wonder-0.7.2-osx-x64-preview.tar.gz` | `Wonder Launcher.app` |
| macOS Apple Silicon | `Wonder-0.7.2-osx-arm64-preview.tar.gz` | `Wonder Launcher.app` |

本次 Release 只上传 Windows 与 Linux 的单文件应用，下载后无需解压，也无需相邻文档或启动脚本。macOS 两种架构的 `.app` 与原命名的试发行压缩包保留本地构建，本次不上传。

各目标共用 **`Wonder-0.7.2-source.tar.gz`**，其中包含对应源码、构建与发布材料、公开文档及全部适用的项目与原始第三方许可、版权和通知；运行应用无需下载该源码包。统一校验文件为 **`Wonder-0.7.2-SHA256SUMS`**，按本次实际生成或上传的产物列出 SHA-256。一次发布从同一份源码快照生成所选目标；分发时仍须履行项目与第三方许可的源码和通知义务。

各平台使用本机格式的入口程序和对应 .NET/图形原生库：Windows PE、Linux ELF、macOS Mach-O。应用自带 .NET，不要求 Wine 或运行 Windows EXE；托管业务仍由 .NET 执行，这不是 Native AOT 声明。Linux 当前采用 X11/XWayland，这是 Linux 的显示服务，详见[Linux 安装说明](31-linux-trial.md)。

## macOS 本地试发行

试发行目标为 macOS 14 或更新系统；请使用仍受 Apple 和 .NET 支持的版本。Intel Mac 选 `osx-x64`，M 系列 Mac 选 `osx-arm64`，并选择相同架构的游戏 Java 和原生依赖。包内没有游戏或 Java。

把完整发行目录解压到当前用户可写的位置。以下以 Apple Silicon 为例：

```bash
shasum -a 256 -c Wonder-0.7.2-SHA256SUMS
tar -xzf Wonder-0.7.2-osx-arm64-preview.tar.gz
cd Wonder-osx-arm64
./sign-Wonder-macos.sh
open "Wonder Launcher.app"
```

这里使用本地构建生成、包含对应 macOS 产物的校验清单；本次 Release 的清单不含未上传的 macOS 包。只取得部分产物时，请只核对所取得文件对应的 SHA-256。macOS 解压包保留可执行权限。

当前 Windows 构建机不能完成 Mac 上的签名验收。`sign-Wonder-macos.sh` 在本机调用 Apple `codesign` 为试用应用添加临时签名及 .NET JIT 权限，并验证签名结构；需要系统具备该工具。它不使用 Developer ID 证书，也不提交 Apple 公证。**本次 macOS 包尚未进行 Developer ID 签名、公证或 Mac 实机验收**；本地签名不能替代这些步骤。可信来源的应用若被 Gatekeeper 阻止，可按 [Apple 官方说明](https://support.apple.com/zh-cn/102445)在“系统设置 → 隐私与安全性”查看并决定是否仍要打开。不要关闭系统整体安全检查。

保持 `.app` 与 `WonderData/` 一起管理。数据放在 `.app` 旁边，避免修改应用包内部；只把 `.app` 拖到 `/Applications` 会改变数据位置，并可能缺少写入权限。应用无法打开时可从终端执行 `"Wonder Launcher.app/Contents/MacOS/Wonder.Desktop"` 查看启动错误。下载应用可能受到 macOS App Translocation 限制；请在 Finder 中把整个解压目录移到确定的可写位置后再打开。

macOS 账户文件为 `WonderData/accounts.macos.dat`，随机加密密钥存入当前用户的系统钥匙串。系统可能要求解锁或允许应用访问。取消授权或密钥丢失时保留账户文件，不保存明文。临时签名或应用更新可能需要重新授权钥匙串访问。删除数据目录不会自动清理钥匙串条目。

回收实例调用 macOS Finder 的废纸篓操作；首次可能需要允许自动化控制 Finder。拒绝权限、超时或操作失败会保留登记并提示，不转为永久删除；重试前核对目录和废纸篓。游戏启动使用 macOS Java；使用 LWJGL 3 或版本元数据声明需要首线程的实例会加入 `-XstartOnFirstThread`，旧版 LWJGL 2 不强制添加。依赖应由原启动器在当前系统补全。

## 数据互通边界

- 主题、工作区、组件状态与实例设置使用同一套 JSON 格式及版本规则，没有三套界面或配置协议。
- 平台绝对路径、Java 可执行文件和 Minecraft 原生库随系统变化；迁移时须重新指定路径并补全本机依赖。
- Windows `accounts.dat` 使用 DPAPI；Linux `accounts.linux.dat` 和 macOS `accounts.macos.dat` 共用 AES-GCM 库格式，分别使用 Secret Service 和钥匙串。凭据受本机用户保护；复制文件不复制系统密钥，跨系统需要重新登录。
- 不实现云同步，也不支持多个系统同时写入同一数据目录。迁移前关闭应用并备份 `WonderData/`。

## 构建与验收

使用 .NET 10 SDK、PowerShell 7.4+：

```powershell
pwsh -File packaging/Wonder.Publish.ps1
# 单独制作一个目标（仍使用同一份源码）
pwsh -File packaging/Wonder.Publish.ps1 -Runtime osx-arm64
```

默认 `all` 在 `artifacts/` 生成 `Wonder-0.7.2-win-x64.exe`、`Wonder-0.7.2-linux-x64`、两个 macOS 架构的本地 `.app` 试发行压缩包、统一源码包和校验文件。Windows/Linux 的发布工作目录分别为 `artifacts/Wonder-win-x64/` 与 `artifacts/Wonder-linux-x64/`；面向用户的应用是 `artifacts/` 根目录内的单文件。单目标发布的校验文件只列出该次应用产物和源码包；本次 Release 仅上传 Windows/Linux 应用、源码包及对应校验清单。macOS 包结构参考 [Avalonia 官方发布说明](https://docs.avaloniaui.net/docs/deployment/macos)，运行时与签名要求见 [.NET macOS 发布说明](https://learn.microsoft.com/en-us/dotnet/core/deploying/macos)。

当前验证环境为 Windows。交叉构建、Mach-O/ELF 架构检查、归档权限和自动化测试不等于 Linux/macOS 实机通过。两端还需验证中文界面、布局保存、同时启动锁、文件选择器、默认浏览器、系统密钥服务、临时实例回收及真实游戏启动。在线认证与 Minecraft AppID 审核状态不因增加平台而改变。

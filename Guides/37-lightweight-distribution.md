# 标准版与轻量版

适用版本：**0.8.0**。Windows x64 和 Linux x64 同时提供两种发行方式，功能和应用依赖一致。

| 平台 | 标准版 | 轻量版 |
| --- | --- | --- |
| Windows x64 | `Wonder-0.8.0-win-x64.exe`，自带 .NET | `Wonder-0.8.0-win-x64-lightweight.zip`，解压后运行 `Wonder.cmd` |
| Linux x64 | `Wonder-0.8.0-linux-x64`，自带 .NET | `Wonder-0.8.0-linux-x64-lightweight.tar.gz`，解压后运行 `Wonder.sh` |

标准版适合离线首次启动。轻量版不包含 .NET 运行库，但 Avalonia、SkiaSharp、HarfBuzz 等应用依赖仍完整打包在 `Wonder.Desktop.exe` 或 `Wonder.Desktop` 中，不需要自行寻找 DLL。它与旧的 `win-x64-minimal` 开发产物不同。

## Windows 首次启动

1. 将 ZIP **完整解压**到可写目录，不要直接在压缩包里运行。
2. 双击 `Wonder.cmd`。它使用 Windows 自带的 64 位 Windows PowerShell 5.1；不要求安装 PowerShell 7 或 .NET SDK。
3. 引导程序优先复用系统已有的 .NET 10 x64 正式版运行库。SDK、Desktop Runtime 等已包含相应基础运行库时，也可以复用。
4. 缺少运行库时，窗口说明版本、微软下载来源与用户级安装目录。输入 `y` 后才下载安装；按 Enter 或输入其他内容取消，不启动 Wonder。
5. 安装成功后自动启动应用。以后运行同一个 `Wonder.cmd` 会复用运行库，不重复下载。

该版本在需要安装时下载微软 **.NET Runtime 10.0.15 x64**，不下载 SDK。运行库保存在 `%LOCALAPPDATA%\Wonder\dotnet\x64\10.0.15\`，先核对预置的官方 SHA-512，再解压并检查运行库。不会请求管理员权限，不会更改系统或用户的永久 PATH。脚本只对当前子进程设置运行库位置；进程级 `ExecutionPolicy Bypass` 不会更改电脑的永久 PowerShell 执行策略。组织策略禁止本地脚本时，请使用标准版或按组织规定安装运行环境。

## Linux 首次启动

```sh
tar -xzf Wonder-0.8.0-linux-x64-lightweight.tar.gz
cd Wonder-linux-x64-lightweight
./Wonder.sh
```

面向 x86-64、glibc Linux 桌面。引导优先复用系统注册、发行版目录或 PATH 中的 .NET 10 x64 正式版。缺失时，在终端显示确认；从图形文件管理器运行且有 `xmessage` 时显示安装/取消对话框。无法显示确认时会停止，并提示从终端执行 `./Wonder.sh`。

用户同意后，使用 `curl` 下载微软 .NET Runtime 10.0.15 Linux x64 压缩包，使用 `sha512sum` 核对官方 SHA-512，然后由 `tar` 解包到 `${XDG_DATA_HOME:-$HOME/.local/share}/Wonder/dotnet/x64/10.0.15/`。不调用 `sudo`，不修改 shell 配置或永久 PATH。下载前须具备 `curl`、`tar`、`sha512sum`；常见桌面发行版还须具备运行库的 glibc、ICU、OpenSSL 等原生依赖。

运行库不替代 Linux 图形库、X11/XWayland、账户密钥服务或 Java。系统依赖与实际验收范围见 [Linux 说明](31-linux-trial.md)。轻量版同样不支持 Alpine/musl 和 ARM64。

## 数据、网络与失败重试

- 引导始终运行同一解压目录内的应用，不把应用放到临时目录执行。`WonderData/` 仍位于 `Wonder.Desktop.exe` 或 `Wonder.Desktop` 旁；更新前关闭应用并备份、保留原有数据。
- 首次缺少运行库且用户同意时，才连接 `builds.dotnet.microsoft.com` 的固定 HTTPS 下载地址。微软/CDN 可收到来源 IP 和所请求的文件；请求不携带 Minecraft 账户令牌或 WonderData 内容。
- 下载、校验和解包失败会保留错误提示，并移除本次未完成的临时安装。重新运行入口可重试；不会在校验失败后执行下载内容。已有但损坏的最终运行库目录不会被自动删除，会提示重命名该运行库目录后重试。
- 没有网络时，已有兼容系统/用户级运行库可正常启动；完全没有运行库时请使用标准版，或先按微软官方说明安装 .NET 10 x64 Runtime。
- 安装的运行库只在首次需要时补齐，不是系统 .NET 自动更新服务。后续安全更新可安装系统最新 .NET 10 正式版供引导优先复用，或更新 Wonder 轻量发行包。
- 卸载用户级运行库只需在相关应用全部退出后移除上述 `Wonder/dotnet/` 目录；不要误删 `WonderData/`。其他同用户 Wonder 轻量副本可能共用该运行库。

## 从同一源码快照发布

构建需要 .NET 10 SDK 与 PowerShell 7.4+。在 PowerShell 中运行：

```powershell
# 标准 Windows/Linux、轻量 Windows/Linux 与源码包使用同一快照。
& ./packaging/Wonder.Publish.ps1 -Runtime @('win-x64','linux-x64') -IncludeLightweight
```

`artifacts/Wonder-0.8.0-SHA256SUMS` 包含这四个应用发行包与对应源码包的五条 SHA-256。标准版固定打包 .NET 10.0.15；轻量应用维持 .NET 10 的兼容要求，由引导复用合适的已安装版本，缺失时才安装固定的 10.0.15。

只重建轻量包时，使用先前生成的同版本源码归档，不会悄悄改用后来编辑的源码：

```powershell
pwsh -File packaging/Wonder.PublishLightweight.ps1
# 或显式指定对应的源码包：
pwsh -File packaging/Wonder.PublishLightweight.ps1 -SourceArchive artifacts/Wonder-0.8.0-source.tar.gz
```

单独重建会写入 `Wonder-0.8.0-lightweight-SHA256SUMS`；最终 Release 应通过统一发布命令重新生成完整五文件清单。旧 `Wonder.PublishMinimal.ps1` 和其产物保留为开发用途，不属于本次推荐的轻量发行。

## 官方机制与下载依据

- [微软 framework-dependent 发布与 apphost 搜索规则](https://learn.microsoft.com/en-us/dotnet/core/deploying/)
- [单文件发布与应用依赖](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)
- [Windows 运行库安装与校验](https://learn.microsoft.com/en-us/dotnet/core/install/windows)
- [Linux 手动安装与 SHA-512 校验](https://learn.microsoft.com/en-us/dotnet/core/install/linux-scripted-manual)
- [.NET 10 官方发布元数据](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json)

普通 apphost 只会在缺少运行库时提供错误和下载链接；这里的入口脚本才负责用户同意后的下载、校验、安装与启动。

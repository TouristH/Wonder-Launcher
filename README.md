# 奇想启动器 · Wonder Launcher

**Wonder Launcher 是面向 Minecraft Java Edition 的独立第三方桌面启动器，与 Mojang 和 Microsoft 无关联，也未获其背书。** 自由组合功能区和组件，在自己的工作区管理实例、启动游戏和使用日常工具。

**当前版本：0.7.2 开发预览。** 项目原创代码按 **[GNU GPLv3（仅第 3 版）](LICENSE)** 开源，并适用第 7 条允许的[署名、修改标识与商标附加条款](ADDITIONAL_TERMS.md)。分发修改版须保留相应的源码开放义务；Wonder 品牌使用另见[品牌政策](BRANDING.md)。

维护者：[TouristH](https://github.com/TouristH) · 项目联系邮箱：[radiafidw@gmail.com](mailto:radiafidw@gmail.com)

[使用说明](Wonder.md) · [帮助目录](Guides/README.md) · [隐私说明](PRIVACY.md) · [第三方声明](THIRD-PARTY-NOTICES.md)

## 已实现的功能

- 桌面式工作区：左右/上下拆分，拖到目标功能区四边可重新摆放、拖到中央可交换；支持分隔边调整、四边侧栏和布局保存，右键可拖动坐标原点，十字轴与离屏方向箭头辅助定位。
- 个性化设置统一管理外观、长按与侧栏交互。组件和功能区的长按拖动时间可在 40–250 ms 间调整，默认 150 ms。桌面组件整体大小支持 50%–200% 调整；网格开启后只做 20 DIP 对齐，不显示网格线。
- 内置组件采用紧凑尺寸。功能区收起为侧边栏后，组件显示为单行页面入口，展开成普通功能区后恢复完整卡片。
- 通用组件与组件库：应用状态和快捷方式位置分开，支持便笺、设置和启动器状态。Windows 与 Linux（X11/XWayland）上的实时预览会跟随指针跨窗口移动，松开时按落点放置或移除快捷方式。
- 下载组件与下载中心：整张组件可点击，卡片显示当前下载步骤与进度。安装 Minecraft 和加载器时默认按两者的版本命名实例，可选兼容的 OptiFine、Fabric API 或 QFAPI/QSL。可分组浏览 Modrinth 的模组、整合包、资源包和光影，安装到实例或保存原文件。任务支持暂停、重试及重启后恢复。见[下载帮助](Guides/33-download-center.md)。
- Wonder 原生实例配置：下载实例自带 `.minecraft/wonder.instance.json`，保存 Wonder 创建署名、版本和独立启动参数；修改实例设置同步保存，移动实例后可重新导入。
- 实例管理：启动和相关操作后自动读取游戏目录，组件可用带图标的下拉框切换实例。游戏安装默认在所选目录中隔离存档、模组和配置，开关位于游戏全局设置。原版使用 Java 稳定版草方块，加载器显示官方图标。
- 官方 `latest-release` 和 `latest-snapshot` profile 可导入。Wonder 按本地已安装的对应版本更新登记，并保留 profile 的游戏目录和启动设置。见[官方 profile 导入](Guides/18-instance-management-and-launch.md#官方-profile-导入)。
- 账户中心与桌面组件：微软正版、自定义 Yggdrasil 和离线账户，支持搜索、账户详情、切换、刷新、重新登录、离线改名和删除；Windows 使用 DPAPI，Linux 使用 Secret Service，macOS 使用系统钥匙串保护加密账户库。
- 账户库和实例索引在后台读写。等待磁盘或系统密钥服务时，界面仍可浏览已加载的内容。
- 版本清单和校验值从官方端点读取，镜像传输的文件在写入最终路径前须通过校验。单个 Wonder 实例配置损坏时，其详情会显示诊断，其余实例仍可读取和保存。启动器异常写入本地轮转日志，关闭时遇到保存失败可重试或强制退出。见[便携数据与错误处理](Guides/15-portable-data-and-motion.md#自动保存与错误处理)。
- 角色外观与认证服务器：正版皮肤预览、上传、恢复和披风切换；离线默认皮肤选择（确认 UUID 变化后应用）；第三方选肤自动打开认证网站。自定义服务器发现与确认、重命名和移除，启动时自动准备经过校验的 authlib-injector。
- 双层皮肤与头像：正背面预览叠加头部、上衣、袖子和裤腿外层；正版和第三方均显示已选披风。第三方支持 HD 皮肤、披风及头像预览，按原图分辨率取样。原有账户组件左侧显示当前皮肤头像，账户中心搜索和切页会保留仍显示的头像，第三方网站改肤后刷新资料即可更新。尺寸范围见[外观预览说明](Guides/30-layered-skins-capes-and-avatars.md)。
- 独立的启动游戏与启动日志组件：整个启动组件都是按钮，显示当前选中实例、版本与账户；日志组件显示任务状态、错误和最近脱敏日志，支持取消启动准备与打开状态窗口。两个组件可分别从组件库拖入工作区。正版和第三方账户启动前重新认证，失败不自动降级；隐藏组件或关闭状态窗口不结束后台任务。
- Java 路径留空且没有继承路径时，Wonder 按实例需要的 Java 主版本查找本机运行时。找不到匹配版本时会提示所需版本。见[启动与 Java 设置](Guides/18-instance-management-and-launch.md#启动与-java)。
- 导入的旧实例可以使用经 SHA‑1 核对的旧名资源索引。见[资源索引兼容](Guides/18-instance-management-and-launch.md#资源索引兼容)。
- 游戏崩溃时自动询问是否保存诊断包。确认后在 `WonderData/CrashReports/` 生成 ZIP，汇总启动器与游戏输出、崩溃报告及本次运行信息。见[崩溃诊断包](Guides/18-instance-management-and-launch.md#保存崩溃诊断包)。
- 同一份源码发布 Windows x64、Linux x64、macOS Intel/Apple Silicon 原生应用，自带各平台 .NET 运行时；Linux 和 macOS 当前为试发行。设置、布局和实例登记保存在应用旁的 `WonderData/`，macOS 放在 `.app` 外部。
- 同一数据目录只允许一个启动器进程写入；Windows 再次打开会尝试激活已有窗口，Linux/macOS 将提示写入终端并退出。不同便携目录可独立运行。

已有实例的一键修复、第三方代码插件加载及权限沙箱仍待实现。没有将 `AssemblyLoadContext` 或普通子进程描述为安全沙箱。账户使用、认证适配器和安全边界见[账户管理说明](Guides/26-account-management.md)。

## 认证与隐私

微软正版账户使用 Wonder 内置的公开应用 ID，由用户在微软官方网页主动授权；启动器不收集微软密码。正版启动路径验证游戏权限和角色，认证失败不会自动切换离线身份。离线和自定义 Yggdrasil 模式不能证明微软游戏所有权。

第三方密码只发送到用户选择的 HTTPS 认证服务且不保存；账户库在 Windows 使用当前用户 DPAPI，在 Linux/macOS 使用共用的 AES-GCM 存储实现，密钥分别交给 Secret Service 和系统钥匙串，不回退为明文。第三方纹理默认阻止内网、回环和其他受限目标，可在单个账户详情中明确允许准确的 HTTPS 来源；仍执行证书验证，纹理不经过系统 HTTP 代理。配置与撤销方式见[账户帮助](Guides/26-account-management.md)。不随源码分发游戏文件、Java、存档或账户数据，详细说明见[隐私政策](PRIVACY.md)。

## 三端原生发行

Windows、Linux 与 macOS 由同一份源码生成原生应用，共用一个 `Wonder-0.7.2-source.tar.gz`。本次 Release 提供可直接运行的 Windows 和 Linux 应用：

- Windows x64：下载 `Wonder-0.7.2-win-x64.exe`，放到可写目录后双击运行。
- Linux x64：下载 `Wonder-0.7.2-linux-x64`，放到可写目录后执行 `chmod +x Wonder-0.7.2-linux-x64`，再执行 `./Wonder-0.7.2-linux-x64`。

两者均为自带 .NET 运行时的单文件应用，运行无需另行下载源码包或在旁边放置文档、启动脚本。独立源码包包含对应源码、构建与发布材料、公开文档及项目和第三方许可与通知。macOS Intel 与 Apple Silicon `.app` 试发行仍可在本地构建，本次 Release 不上传；本地签名、运行和互通边界见[三端原生发行说明](Guides/32-native-cross-platform.md)。

Linux 面向 Ubuntu 24.04 x64 等 glibc 桌面环境，仍需系统图形库及 X11 或 Wayland 桌面的 XWayland；账户保存需要 `secret-tool` 和已解锁的 Secret Service 密钥环。系统依赖、校验、数据迁移与实机验收步骤见 [Linux 试发行说明](Guides/31-linux-trial.md)。

Linux 与 macOS 图形桌面、系统密钥服务和真实游戏启动仍待实机验收。macOS 包尚未进行 Developer ID 签名和 Apple 公证，试用需按说明在 Mac 上执行本地临时签名。Linux 未提供 ARM64、Alpine/musl、AppImage、deb 或 rpm 包，也不声明原生 Wayland 支持。

## 从源码构建

需要 .NET 10 SDK。以下命令可在 Windows、Linux 或 macOS 的源码根目录执行，构建并运行当前系统的开发版本：

```powershell
dotnet build Wonder.slnx -c Release
dotnet run --project src/Wonder.Desktop/Wonder.Desktop.csproj
```

制作发行产物还需要 PowerShell 7.4 或更新版本。Windows、Linux 和 macOS 均使用同一个发布脚本，可按目标平台分别构建：

```powershell
# Windows x64
pwsh -File packaging/Wonder.Publish.ps1 -Runtime win-x64

# Linux x64
pwsh -File packaging/Wonder.Publish.ps1 -Runtime linux-x64

# macOS Intel
pwsh -File packaging/Wonder.Publish.ps1 -Runtime osx-x64

# macOS Apple Silicon（M 系列）
pwsh -File packaging/Wonder.Publish.ps1 -Runtime osx-arm64
```

也可以一次从同一份源码快照构建全部四个目标，共用一个源码包和校验文件：

```powershell
pwsh -File packaging/Wonder.Publish.ps1
```

| 目标 | 应用产物 |
| --- | --- |
| Windows x64 | `artifacts/Wonder-0.7.2-win-x64.exe` |
| Linux x64 | `artifacts/Wonder-0.7.2-linux-x64` |
| macOS Intel | `artifacts/Wonder-osx-x64/Wonder Launcher.app` |
| macOS Apple Silicon | `artifacts/Wonder-osx-arm64/Wonder Launcher.app` |

可以交叉构建其他系统的产物；运行验收仍须在相应系统进行。Linux 的系统依赖见[Linux 试发行说明](Guides/31-linux-trial.md)；macOS 应用构建后须在 Mac 上按[三端发行说明](Guides/32-native-cross-platform.md)完成本地签名与试用，交叉构建不等于已经签名、公证或实机验证。

`artifacts/Wonder-win-x64/`、`artifacts/Wonder-linux-x64/` 为发布工作目录；macOS 同时生成原命名的试发行压缩包。`Wonder-0.7.2-source.tar.gz` 与 `Wonder-0.7.2-SHA256SUMS` 同样位于 `artifacts/`；校验文件按本次实际生成的应用产物和源码包列出 SHA-256，单独指定目标时只包含该次产物。上传本次 Release 时使用 Windows、Linux 应用及独立源码包，并提供与实际上传文件一致的校验清单。

### Windows x64 可选最小版

本地可构建仅内置六个 Wonder 自身程序集的最小版 EXE，保留完整功能；所有第三方依赖由用户准备，当前仅支持 Windows x64。使用 .NET 10 SDK 和 PowerShell 7.4+ 执行：

```powershell
pwsh -File packaging/Wonder.PublishMinimal.ps1
# 同时从源码发布结果准备第三方 DLL
pwsh -File packaging/Wonder.PublishMinimal.ps1 -PrepareDependencies
```

产物为 `artifacts/Wonder-0.7.2-win-x64-minimal.exe` 和依赖清单 `artifacts/Wonder-0.7.2-win-x64-minimal.dependencies.json`。运行前须在系统安装 .NET 10 x64 运行时，并按 JSON 清单将 Avalonia、SkiaSharp、HarfBuzz 等第三方 DLL 放在 EXE 同目录；.NET 运行时通过系统安装，无需放在 EXE 旁。`-PrepareDependencies` 额外生成 `artifacts/Wonder-minimal-dependencies-win-x64/`，可将其中 DLL 复制到最小版 EXE 同目录；这些 DLL 不内置于最小版 EXE。依赖不齐时最小版无法运行，普通用户使用原自包含版更省步骤，默认 Release 仍为自包含版。详见[三端发行说明](Guides/32-native-cross-platform.md#windows-x64-可选最小版)。

`Wonder.slnx` 包含六个生产工程，版本统一在 `Directory.Build.props` 中维护。三端使用相同设置与工作区格式，平台账户文件和绝对路径仍有差异。数据位于应用旁的 `WonderData/`，macOS 为 `.app` 的同级目录。更新前关闭启动器并保留数据目录；跨系统或更换桌面用户后，账户通常需要重新登录，游戏路径和 Java 也需重新设置。

公开仓库保留 `src/`、`packaging/`、解决方案与构建配置、README、Wonder 使用说明、用户 Guides，以及许可、隐私和第三方原始通知。测试工程、探针、AI 开发约定、实施计划、内部验收记录和本地工具配置不纳入公开提交；游戏文件、用户数据、编译缓存及发布产物也不提交。

分发程序时须保留适用的项目与第三方许可、版权和通知，并按许可要求提供对应源码和必要构建材料。独立源码包保留 README、Wonder.md、公开 Guides、PRIVACY.md、LICENSE、LICENSE.zh-CN.md、ADDITIONAL_TERMS.md、BRANDING.md、NOTICE、THIRD-PARTY-NOTICES.md 和 ThirdPartyLicenses；这些文件无需放在 Windows/Linux 应用旁才能运行。

## 许可与转载

原创代码与原创文档采用 **GPL-3.0-only**，并附有 [GPLv3 第 7 条附加条款](ADDITIONAL_TERMS.md)。[中文许可说明](LICENSE.zh-CN.md)解释适用范围，完整英文协议以 [LICENSE](LICENSE) 为准。

- **允许使用、研究、修改、分发及商业使用**，无需为 GPL 已授予的权利另行申请作者批准。
- **分发基于本项目的修改版时，须按 GPLv3 授权受覆盖的整个作品，并向接收者提供相应源码**；分发 EXE 也须按第 6 条提供对应版本源码及必要构建材料。仅公开旧版本、删去实际修改或只给一个无法构建的片段均不能替代相应源码。
- 保留适用的版权、许可、无担保及合理署名声明；明确标注修改和日期，并让第三方修改版与官方版本明显区分。
- **源码许可不授予商标使用权**。可以如实说明“基于 Wonder Launcher”，但不得冒充官方、暗示官方背书或把修改版包装成未修改的官方版本。名称、命名空间和署名的具体边界见 [BRANDING.md](BRANDING.md)。
- 第三方库、运行时、游戏内容与商标保留各自权利。项目许可不改变它们的独立许可，详见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。

GPL 不要求公开没有向他人分发的私人修改，也不要求把修改提交给原作者。公开 Fork 和回馈上游是欢迎的社区实践。独立作品的简单聚合不会仅因放在同一个发行包中就自动改用 GPL；具体范围以协议为准。

欢迎通过 [Issues](https://github.com/TouristH/Wonder-Launcher/issues) 和 Pull Request 参与，贡献规则见 [CONTRIBUTING.md](CONTRIBUTING.md)。安全问题请发至联系邮箱，避免公开令牌、个人数据或游戏路径。

# Linux x64 试发行

适用版本：**Wonder Launcher 0.7.2 Linux x64 preview**。本应用与 Windows、macOS 共用源码及业务实现，保留桌面、功能区、组件库、实例管理、账户与启动功能，面向 Ubuntu 24.04 x64 等 glibc 桌面系统。`Wonder-0.7.2-linux-x64` 是 Linux ELF 单文件应用，包含 Linux .NET 运行时，不通过 Wine 运行 Windows EXE；系统图形库和游戏 Java 仍须由系统提供。完整平台清单见[三端发行说明](32-native-cross-platform.md)。

**当前为交叉构建试发行，Linux 桌面实机验收尚未完成。** Windows 上的构建、自动化测试和产物检查不能代替 Linux 窗口、密钥环、回收站、在线登录和真实游戏启动验证。本应用不适用于 ARM64 或 Alpine/musl，也未提供 AppImage、deb、rpm 安装器。

## 准备系统

使用普通桌面用户登录。当前采用 Avalonia 默认 X11 后端；Wayland 会话需启用 XWayland，并具有有效的 `DISPLAY`。纯终端、SSH 无图形会话和未启用 XWayland 的原生 Wayland 环境不在本试发行运行范围内。[Avalonia Linux 平台说明](https://docs.avaloniaui.net/docs/platform-specific-guides/linux)

Ubuntu 24.04 可安装以下依赖（其他发行版请使用其对应包名）：

```bash
sudo apt update
sudo apt install ca-certificates libc6 libgcc-s1 libgssapi-krb5-2 \
  libicu74 libssl3t64 libstdc++6 tzdata zlib1g \
  libx11-6 libice6 libsm6 libfontconfig1 fonts-noto-cjk \
  libsecret-tools libglib2.0-bin xdg-utils
```

前两行依赖包来自 [.NET Ubuntu 24.04 运行依赖](https://learn.microsoft.com/en-us/dotnet/core/install/linux-ubuntu-install#ubuntu-2404)，X11 与字体依赖依据 [Avalonia 原生依赖说明](https://docs.avaloniaui.net/docs/deployment/docker#required-packages)。中文界面需系统中文字体；Wayland 桌面缺少 XWayland 时还需安装并启用 `xwayland`。

账户保存依赖 `secret-tool`（[Ubuntu 的 libsecret-tools 包](https://packages.ubuntu.com/en/noble/libsecret-tools)）以及正常运行、已解锁的 Secret Service 密钥环，例如 GNOME Keyring。仅安装命令行工具不代表密钥环服务已经就绪；在系统“密码与密钥”等管理器中确认。GNOME 桌面通常已配置该服务，其他桌面需配置兼容服务。不要用 `sudo` 启动 Wonder。目录回收使用 `gio`，由 [libglib2.0-bin](https://packages.ubuntu.com/noble/libglib2.0-bin) 提供；默认浏览器与目录打开还需正常的桌面关联。

## 校验与运行

同一版本包含：

| 文件 | 用途 |
| --- | --- |
| `Wonder-0.7.2-linux-x64` | 自包含 ELF 单文件应用 |
| `Wonder-0.7.2-source.tar.gz` | 各平台共用的对应源码、构建与发布材料、公开文档及项目和第三方许可与通知；运行应用无需下载 |
| `Wonder-0.7.2-SHA256SUMS` | 本次实际发布的应用和源码包的 SHA-256 清单 |

下载 Linux 应用与校验文件到同一个可写目录，先校验，再赋予执行权限并启动。以下命令跳过未下载的其他平台文件；确认 `Wonder-0.7.2-linux-x64` 显示 `OK`。如另外下载了源码包，也应确认它显示 `OK`：

```bash
sha256sum --ignore-missing -c Wonder-0.7.2-SHA256SUMS
chmod +x Wonder-0.7.2-linux-x64
./Wonder-0.7.2-linux-x64
```

应用无需解压，也无需 `.sh` 启动脚本、相邻文档或源码包；复制或重新下载后若失去执行权限，再执行上述 `chmod` 命令。仍须满足前文的 CPU 架构、图形会话、系统依赖和目录写入要求。

数据在应用旁边的 `WonderData/`，与从哪个工作目录运行无关。首次启动 .NET 可能解包原生库；“单文件”不代表运行中不会使用系统临时/缓存目录。

## 账户、实例与更新

Linux 将账户资料与令牌加密保存在 `WonderData/accounts.linux.dat`；AES-GCM 随机密钥存入当前桌面用户的 Secret Service，不在数据目录保存明文密钥。新建离线账户同样需要密钥环。密钥环不可用、被锁定或原密钥缺失时，账户操作会报错，原文件保留，不回退为明文或覆盖损坏库。解锁后重新读取或重试。详情见[账户帮助](26-account-management.md)与[隐私说明](../PRIVACY.md)。

Windows 的 `accounts.dat` 保留原样，不在 Linux 自动转换或解密。复制 `WonderData/` 不会复制系统密钥；更换系统或桌面用户后需重新登录。不要发布自己的数据目录。Linux 的密钥环条目也不会随删除数据目录自动移除。

游戏需要适合其版本的 **Linux Java**。在全局或实例设置中填写实际 `java` 可执行文件，或使用 `JAVA_HOME`/`PATH` 中的 Java。通过来源启动器在 Linux 补全实例原生库、资源和加载器；Windows 的 `java.exe`、DLL 与绝对路径不能直接使用。Wonder 当前不下载或修复游戏依赖，缺失时会停止并提示。迁移时重新选择游戏目录，核对全局与实例覆盖设置；原存档不作为测试材料。

删除实例或目录仍须确认范围并输入 `sure`。Linux 调用 `gio trash`；工具缺失、回收站不受支持或操作失败时提示错误，不改为永久删除。操作失败或超时时，应核对文件管理器和回收站中的实际状态后再重试。

更新前关闭旧版 Wonder 并备份 `WonderData/`，将新版本应用放到同一目录，赋予执行权限后启动，保留数据目录。不要覆盖已有数据。同一数据目录只能有一个写入进程，Linux 第二次启动在终端提示并退出，不自动激活原窗口；请手动切换到原窗口。不同便携目录可独立运行。

## 从源码制作

需要 .NET 10 SDK 与 PowerShell 7.4 或更新版本。在源码根目录运行：

```powershell
dotnet build Wonder.slnx -c Release
pwsh -File packaging/Wonder.Publish.ps1 -Runtime linux-x64
```

脚本先生成统一源码快照，再构建解决方案并发布所选目标；上面的 `-Runtime linux-x64` 直接生成 `artifacts/Wonder-0.7.2-linux-x64`，`artifacts/Wonder-linux-x64/` 为发布工作目录。省略参数时默认 `all`，制作 Windows x64、Linux x64 应用和两个 macOS 架构的本地 `.app` 试发行包，共用 `Wonder-0.7.2-source.tar.gz` 与 `Wonder-0.7.2-SHA256SUMS`。校验文件只列出本次实际生成的应用产物和源码包；本次 Release 仅上传 Windows、Linux 应用与源码包，并提供对应校验清单。所有产物在 `artifacts/`。

对应源码包纳入同一份源码、构建与发布材料、公开文档，以及全部适用的项目和原始第三方许可、版权与通知，排除游戏、账户、测试数据、探针、编译缓存与 Git 历史。主题和工作区使用共用格式；平台凭据保护、游戏路径和 Java 的差异见[三端发行说明](32-native-cross-platform.md)。

分发应用时须按 [GPL-3.0-only](../LICENSE)、[附加条款](../ADDITIONAL_TERMS.md)及适用的第三方许可提供对应源码与必要声明；本次发行通过独立源码包提供这些材料，无需与应用放在同一目录。不要以另一版本的源码替代实际分发版本。

## Linux 实机验收清单

以下项目尚待在目标 Linux 桌面逐项执行，请使用独立试用目录和临时游戏副本：

1. 校验应用文件，赋予执行权限；从含空格的路径启动，确认中文、缩放、窗口、菜单与文件选择器正常。
2. 移动功能区和组件、编辑便笺、切换主题，关闭重开后确认保存；从另一工作目录启动仍读取同一数据目录。
3. 同一数据目录再次启动时只有原窗口运行；退出后能够重启，不手动删除锁文件。
4. 在已解锁密钥环下添加合成离线玩家并重启；锁定密钥环后确认读取或保存失败会提示，原账户文件保留。恢复原密钥环后可重试。
5. 在自愿授权的测试账户上检查微软/第三方网页登录、取消、刷新与皮肤预览。当前不据此声称 Minecraft AppID 获批。
6. 导入已经补全 Linux 依赖的临时实例，指定 Linux Java，核对启动日志、原生库加载和游戏退出状态。
7. 对临时副本执行移动和回收，确认回收站可恢复；记录桌面环境、发行版、错误和已脱敏日志，勿提交令牌、账号库或个人路径。

Windows 自动化测试通过也不等于以上项目已通过；Linux 实机问题在试发行阶段仍可能出现。

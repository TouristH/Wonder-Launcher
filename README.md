# 奇想启动器 · Wonder Launcher

面向 Minecraft Java Edition 的桌面式启动器：自由组合功能区和组件，让实例管理、启动与日常工具融入用户自己的工作区。

**当前版本：0.7.2 开发预览。** 项目原创代码按 **[GNU GPLv3（仅第 3 版）](LICENSE)** 开源，并适用第 7 条允许的[署名、修改标识与商标附加条款](ADDITIONAL_TERMS.md)。分发修改版须保留相应的源码开放义务；Wonder 品牌使用另见[品牌政策](BRANDING.md)。

> NOT AN OFFICIAL MINECRAFT PRODUCT. NOT APPROVED BY OR ASSOCIATED WITH MOJANG OR MICROSOFT.
>
> 本项目不是 Minecraft 官方产品，未获 Mojang 或 Microsoft 认可，亦与其无关联。Minecraft 相关商标和游戏内容属于各自权利人。

维护者：[TouristH](https://github.com/TouristH) · 项目联系邮箱：[radiafidw@gmail.com](mailto:radiafidw@gmail.com)

[使用说明](Wonder.md) · [开发文档](Guides/README.md) · [隐私说明](PRIVACY.md) · [Minecraft AppID 审核说明](Guides/24-minecraft-app-review.md) · [第三方声明](THIRD-PARTY-NOTICES.md)

## 已实现的功能

- 桌面式工作区：功能区拖动换位、分隔边调整、四边侧栏和布局保存。
- 通用组件与组件库：应用状态和快捷方式位置分开，支持便笺、设置和启动器状态。
- 实例管理：读取已有游戏目录，切换列表/网格、选择实例、编辑启动设置及目录操作。
- 账户中心与桌面组件：微软正版、自定义 Yggdrasil 和离线账户，支持搜索、账户详情、切换、刷新、重新登录、离线改名和删除；Windows 当前用户加密保存账户库。
- 角色外观与认证服务器：正版皮肤预览、上传、恢复和披风切换；离线默认皮肤选择（确认 UUID 变化后应用）；第三方选肤自动打开认证网站。自定义服务器发现与确认、重命名和移除，启动时自动准备经过校验的 authlib-injector。
- 双层皮肤与头像：正背面预览叠加头部、上衣、袖子和裤腿外层；正版和第三方均显示已选披风。第三方支持 HD 皮肤、披风及头像预览，按原图分辨率取样。原有账户组件左侧显示当前皮肤头像，第三方网站改肤后刷新资料即可更新。尺寸范围见[外观预览说明](Guides/30-layered-skins-capes-and-avatars.md)。
- 独立启动组件与任务状态：使用选中账户启动已有本地实例，正版和第三方账户启动前重新认证，失败不自动降级；关闭状态窗口不结束后台任务。
- Windows x64 自包含单文件分发；设置、布局和实例登记保存在程序旁的 `WonderData/`。
- 同一数据目录只允许一个启动器进程写入；再次打开会尝试激活已有窗口。不同便携目录可独立运行。

游戏安装/修复、第三方代码插件加载及权限沙箱仍待实现。没有将 `AssemblyLoadContext` 或普通子进程描述为安全沙箱。账户使用、认证适配器和安全边界见[账户管理说明](Guides/26-account-management.md)。

## Minecraft Services / AppID 审核信息

申请使用的应用显示名为 **Wonder Launcher**，不在注册应用名中加入 Minecraft、Mojang、Microsoft、Live、Xbox、Discord 或 Hypixel。Minecraft 只用于说明兼容的游戏。

| 项目 | 当前状态 |
| --- | --- |
| 应用用途 | 可自定义桌面式启动器、本地实例与启动设置管理 |
| 平台与技术 | Windows x64，.NET 10，Avalonia；核心与组件契约分层 |
| 申请目标 | 计划通过用户主动发起的官方登录，验证游戏所有权并获取本人游戏资料和启动会话 |
| Application / Client ID、Tenant ID | 启动器内置维护者的公开 Client ID `fab70daf-af0d-423d-9468-6b42edce3c81`，玩家无需填写；注册配置、Tenant ID 与服务授权仍需维护者核实 |
| 微软登录、Xbox/XSTS、Minecraft API | 已实现设备码、刷新、服务认证链及角色查询；尚未通过真实账户完成线上验收 |
| 所有权与启动门禁 | 正版路径校验游戏 entitlement 与角色，失败拒绝启动；独立离线和第三方模式仍存在，不能声称满足所有路径的审核要求 |
| 审核状态 | **未声明提交或通过审核**；此 README 不能替代实际认证和所有权校验 |

官方 [AppID Review 表单](https://aka.ms/mce-reviewappid) 要求在允许游戏前完成身份认证和所有权验证，且不因服务器配置不同而豁免。本项目会如实保留当前实现和待办，不将离线用户名或本地游戏文件的存在当作正版所有权证明。完整的计划、代码入口与提交前条件见[审核说明](Guides/24-minecraft-app-review.md)。

当前版本不收集微软密码，不实现账号令牌导入或云端账号服务。第三方密码只发送到用户指定的 HTTPS 服务且不保存；账户库由 Windows DPAPI CurrentUser 加密。不随源码分发 Minecraft 客户端、资产、Java 或用户存档。离线和第三方身份不能证明微软正版所有权。

## For Minecraft application reviewers

Wonder Launcher is an independent, Windows desktop launcher and local instance manager maintained by TouristH. Contact: **radiafidw@gmail.com**.

Version 0.7.2 provides an account center with Microsoft device-code sign-in, Xbox/XSTS authentication, Minecraft Services entitlement/profile checks, encrypted credential storage and account-aware launching. It adds account refresh and reauthentication, skin and owned-cape management, and saved authentication servers. The public Wonder application ID is built in; players do not configure an application registration. Synthetic protocol and UI tests do not establish successful live authentication or API authorization. Offline and custom Yggdrasil modes remain separate and do **not** satisfy the universal authentication/ownership requirement in the AppID review form. No approval is claimed.

The integration uses the maintainer-supplied public application ID and user-initiated authentication. No Microsoft passwords, another launcher's Client ID, game binaries, or account tokens are included in this repository. See the [review notes](Guides/24-minecraft-app-review.md) and [privacy statement](PRIVACY.md) for behavior, security boundaries, and remaining live verification.

## 从源码构建

需要 .NET 10 SDK。在本目录执行：

构建版本统一在根目录 `Directory.Build.props` 的 `Version` 中维护；窗口显示和游戏启动参数使用同一构建元数据。当前版本仍为 0.7.2。

```powershell
dotnet build Wonder.slnx -c Release
dotnet test Wonder.slnx -c Release
dotnet publish src/Wonder.Desktop/Wonder.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o artifacts/Wonder-win-x64
```

发布程序为 `artifacts/Wonder-win-x64/Wonder.Desktop.exe`，设置和布局位于同目录的 `WonderData/`。更新程序时保留此数据目录。`artifacts/`、游戏文件、用户数据和编译缓存不提交到源码仓库。编译产物需连同适用的许可及第三方声明按协议分发。

第一阶段修复针对多开覆盖、第三方凭据轮换后取消和官方 profile 换版本的登记问题，见[修复验收记录](Guides/32-first-repair-results.md)。第三方认证已返回并验证的新凭据会先保存，取消仍会停止后续启动；保存失败时应保持启动器打开，修复数据目录权限后重试账户刷新或启动。更新前请关闭旧版启动器进程。

2026-10-28 第二阶段修复统一账户上限与版本来源、修正矩形占位头像，并加强实例索引刷盘及保存多重故障诊断。最新完整测试 **954 项通过、0 失败、0 跳过**（Workspace 225、Game 374、Desktop 355）；Release 构建 0 警告、0 错误，发布 EXE 双进程冒烟通过，发布前后 33 个 WonderData 文件哈希一致。范围与剩余问题见[第二阶段验收记录](Guides/34-second-repair-results.md)。

此前 0.7.2 HD 外观兼容更新完整测试为 **898 项通过、0 失败、0 跳过**（Workspace 210、Game 341、Desktop 347），包含 HD 纹理校验与下载、双层皮肤像素、披风和账户头像交互验证，以上用例继续包含在最新回归中。详细范围见[外观预览](Guides/30-layered-skins-capes-and-avatars.md)，登录排障见[登录诊断与默认皮肤](Guides/28-login-diagnostics-and-default-skins.md)。测试不能代表真实账户认证或 AppID 已获批。

2026-10-27 第一阶段修复后的完整测试为 **833 项通过、0 失败、0 跳过**（Workspace 210、Game 314、Desktop 309）；Release 构建 0 警告、0 错误，发布 EXE 双进程冒烟通过。原有 WonderData 的 32 个文件哈希保持一致，证据及限制见[本轮验收记录](Guides/32-first-repair-results.md)。

## 许可与转载

原创代码与原创文档采用 **GPL-3.0-only**，并附有 [GPLv3 第 7 条附加条款](ADDITIONAL_TERMS.md)。[中文许可说明](LICENSE.zh-CN.md)解释适用范围，完整英文协议以 [LICENSE](LICENSE) 为准。

- **允许使用、研究、修改、分发及商业使用**，无需为 GPL 已授予的权利另行申请作者批准。
- **分发基于本项目的修改版时，须按 GPLv3 授权受覆盖的整个作品，并向接收者提供相应源码**；分发 EXE 也须按第 6 条提供对应版本源码及必要构建材料。仅公开旧版本、删去实际修改或只给一个无法构建的片段均不能替代相应源码。
- 保留适用的版权、许可、无担保及合理署名声明；明确标注修改和日期，并让第三方修改版与官方版本明显区分。
- **源码许可不授予商标使用权**。可以如实说明“基于 Wonder Launcher”，但不得冒充官方、暗示官方背书或把修改版包装成未修改的官方版本。名称、命名空间和署名的具体边界见 [BRANDING.md](BRANDING.md)。
- 第三方库、运行时、游戏内容与商标保留各自权利。项目许可不改变它们的独立许可，详见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。

GPL 不要求公开没有向他人分发的私人修改，也不要求把修改提交给原作者。公开 Fork 和回馈上游是欢迎的社区实践。独立作品的简单聚合不会仅因放在同一个发行包中就自动改用 GPL；具体范围以协议为准。

欢迎通过 [Issues](https://github.com/TouristH/Wonder-Launcher/issues) 和 Pull Request 参与，贡献规则见 [CONTRIBUTING.md](CONTRIBUTING.md)。安全问题请发至联系邮箱，避免公开令牌、个人数据或游戏路径。

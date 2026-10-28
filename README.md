# 奇想启动器 · Wonder Launcher

面向 Minecraft Java Edition 的桌面式启动器：自由组合功能区和组件，让实例管理、启动与日常工具融入用户自己的工作区。

**当前版本：0.7.2 开发预览。** 项目原创代码按 **[GNU GPLv3（仅第 3 版）](LICENSE)** 开源，并适用第 7 条允许的[署名、修改标识与商标附加条款](ADDITIONAL_TERMS.md)。分发修改版须保留相应的源码开放义务；Wonder 品牌使用另见[品牌政策](BRANDING.md)。

> NOT AN OFFICIAL MINECRAFT PRODUCT. NOT APPROVED BY OR ASSOCIATED WITH MOJANG OR MICROSOFT.
>
> 本项目不是 Minecraft 官方产品，未获 Mojang 或 Microsoft 认可，亦与其无关联。Minecraft 相关商标和游戏内容属于各自权利人。

维护者：[TouristH](https://github.com/TouristH) · 项目联系邮箱：[radiafidw@gmail.com](mailto:radiafidw@gmail.com)

[使用说明](Wonder.md) · [帮助目录](Guides/README.md) · [隐私说明](PRIVACY.md) · [第三方声明](THIRD-PARTY-NOTICES.md)

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

## 认证与隐私

微软正版账户使用 Wonder 内置的公开应用 ID，由用户在微软官方网页主动授权；启动器不收集微软密码。正版启动路径验证游戏权限和角色，认证失败不会自动切换离线身份。当前尚未完成真实账户的线上全链路验收，也未声明通过 Minecraft AppID 审核。离线和自定义 Yggdrasil 模式不能证明微软游戏所有权。

第三方密码只发送到用户选择的 HTTPS 认证服务且不保存；账户库使用 Windows 当前用户 DPAPI 加密。第三方纹理默认阻止内网、回环和其他受限目标，可在单个账户详情中明确允许准确的 HTTPS 来源；仍执行证书验证，纹理不经过系统 HTTP 代理。配置与撤销方式见[账户帮助](Guides/26-account-management.md)。不随源码分发游戏文件、Java、存档或账户数据，详细说明见[隐私政策](PRIVACY.md)。

## 从源码构建

需要 .NET 10 SDK。在源码根目录执行：

```powershell
dotnet build Wonder.slnx -c Release
dotnet run --project src/Wonder.Desktop/Wonder.Desktop.csproj
dotnet publish src/Wonder.Desktop/Wonder.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o artifacts/Wonder-win-x64
```

`Wonder.slnx` 包含六个生产工程，版本统一在 `Directory.Build.props` 中维护。发布程序为 `artifacts/Wonder-win-x64/Wonder.Desktop.exe`，设置、布局、账户和实例登记位于程序旁的 `WonderData/`。更新前关闭启动器，保留数据目录；移动到其他电脑后，绑定原 Windows 用户的账户需要重新登录。

公开仓库保留 `src/`、解决方案与构建配置、README、Wonder 使用说明、用户 Guides，以及许可、隐私和第三方原始通知。测试工程、探针、AI 开发约定、实施计划、内部验收记录和本地工具配置不纳入公开提交；游戏文件、用户数据、编译缓存及发布产物也不提交。

分发程序时，请同步提供 README、Wonder.md、公开 Guides、PRIVACY.md、LICENSE、LICENSE.zh-CN.md、ADDITIONAL_TERMS.md、BRANDING.md、NOTICE、THIRD-PARTY-NOTICES.md 和 ThirdPartyLicenses，并按许可要求提供对应源码和必要构建材料。

## 许可与转载

原创代码与原创文档采用 **GPL-3.0-only**，并附有 [GPLv3 第 7 条附加条款](ADDITIONAL_TERMS.md)。[中文许可说明](LICENSE.zh-CN.md)解释适用范围，完整英文协议以 [LICENSE](LICENSE) 为准。

- **允许使用、研究、修改、分发及商业使用**，无需为 GPL 已授予的权利另行申请作者批准。
- **分发基于本项目的修改版时，须按 GPLv3 授权受覆盖的整个作品，并向接收者提供相应源码**；分发 EXE 也须按第 6 条提供对应版本源码及必要构建材料。仅公开旧版本、删去实际修改或只给一个无法构建的片段均不能替代相应源码。
- 保留适用的版权、许可、无担保及合理署名声明；明确标注修改和日期，并让第三方修改版与官方版本明显区分。
- **源码许可不授予商标使用权**。可以如实说明“基于 Wonder Launcher”，但不得冒充官方、暗示官方背书或把修改版包装成未修改的官方版本。名称、命名空间和署名的具体边界见 [BRANDING.md](BRANDING.md)。
- 第三方库、运行时、游戏内容与商标保留各自权利。项目许可不改变它们的独立许可，详见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。

GPL 不要求公开没有向他人分发的私人修改，也不要求把修改提交给原作者。公开 Fork 和回馈上游是欢迎的社区实践。独立作品的简单聚合不会仅因放在同一个发行包中就自动改用 GPL；具体范围以协议为准。

欢迎通过 [Issues](https://github.com/TouristH/Wonder-Launcher/issues) 和 Pull Request 参与，贡献规则见 [CONTRIBUTING.md](CONTRIBUTING.md)。安全问题请发至联系邮箱，避免公开令牌、个人数据或游戏路径。

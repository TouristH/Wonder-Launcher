# 账户中心、登录与角色外观

适用版本：0.7.2。更新日期：2026-10-26。登录错误定位见[补充说明](28-login-diagnostics-and-default-skins.md)，新版第三方预览、双层皮肤、已选披风和组件头像见[外观说明](30-layered-skins-capes-and-avatars.md)。

## 使用

在组件库找到“账户管理”，直接点“管理账户”，或将组件拖到工作区。账户中心左侧是可搜索的账户卡片，右侧是所浏览账户的详情，展示玩家名、身份类型、UUID、认证来源和最近验证时间。**点击卡片只浏览资料，点击详情中的“使用此账户”才切换启动身份**；桌面组件下拉框可直接切换，启动组件同步当前账户。

“添加账户”进入独立的微软正版、第三方或离线流程，成功添加后自动使用。已有在线账户可“刷新资料”或“重新登录”；重新登录必须回到原身份，不能借此覆盖成另一个玩家。刷新和重新登录其他条目不会改变正在使用的启动账户。UUID 可复制。移除账户需确认，会删除本机凭据，不会删除存档或停止已运行游戏。

### 微软正版

1. 添加账户时选择“微软正版”，点击“登录 Microsoft 账户”。Wonder 内置自己的公开 Application / Client ID，**玩家无需填写应用 ID、Client Secret 或其他开发者配置**。维护者的公开 ID 为 `fab70daf-af0d-423d-9468-6b42edce3c81`；自行部署修改版应在源代码和应用注册中配置自己的应用。
2. 取得本次设备码后自动打开微软官方登录页，可点击“复制设备码”或“重新打开登录页”。核对页面域名和应用名称 Wonder Launcher，再输入设备码并授权。不要使用陌生人发来的设备码，也不要分享自己的设备码。
3. Wonder 完成 Xbox/XSTS、Minecraft 会话、entitlement 和 profile 检查后保存账户。没有游戏权限、没有角色、账号受限、网络错误或 API 拒绝时不能完成正版启动，不自动改用离线身份。
4. “取消登录”或关闭账户窗口取消等待。启动前重新刷新并验证所选账户；Microsoft 密码/多因素验证始终由官方网页处理。

应用注册须支持个人 Microsoft 账户、公共客户端/设备授权以及 Xbox 所需权限；Minecraft API 还可能需要 AppID 审核。提供 Client ID 不代表这些配置已经验证。代码和合成测试已经实现认证流程，但尚无本项目真实账户全链路授权成功的证据。

### 正版皮肤与披风

账户详情中提供二维像素角色预览，支持正面与背面切换；这不是三维角色编辑器。读取到官方皮肤后展示对应经典或纤细模型，资料或纹理暂不可用时显示明确标注的默认示意图。纹理只从官方纹理主机读取，下载请求不携带账户令牌。

1. 点击“选择皮肤文件…”，选择不超过 1 MiB 的 **64×64 或 64×32 PNG**。本地文件先进入预览，选择文件本身不会修改远端。
2. 选择经典（Classic）或纤细（Slim）模型，查看预览后点击“上传到 Minecraft”。提交前会刷新并验证该账户，成功后重新读取资料。
3. “恢复默认皮肤”需要再次确认。披风列表仅包含此账户拥有的披风，选择一项或隐藏后，点击“应用披风选择”才提交。
4. 网络中断或超时可能发生在远端接受请求之后，应点击“刷新资料”核对；正在运行的游戏可能需要重新登录才能显示变化。

离线账户可通过明确确认 UUID 变化来选择游戏内置默认皮肤，不注入自定义纹理。第三方“选择皮肤”会打开已保存的认证网站；外观由该网站管理。升级后或网站更换外观后点击“刷新资料”，可在账户详情预览双层皮肤和已选披风，原有账户组件左侧同步显示头像。版本适配、身份影响与网站设置见[补充说明](28-login-diagnostics-and-default-skins.md)，新版预览见[外观说明](30-layered-skins-capes-and-avatars.md)。

### 第三方 Yggdrasil 与认证服务器

1. 打开“认证服务器”，输入服务主页、API 地址或该服务提供的 `authlib-injector:` 链接，点击“读取服务器信息”。发现阶段不发送账户凭据，可按 HTTPS 定位提示找到实际 API；禁止 HTTP、API URL 内用户名密码、查询参数和片段。
2. 核对返回的服务器名称与**实际认证 API 地址**，例如 `https://skin.example.com/api/yggdrasil/`，点击“信任此地址并保存”。若最终地址不同于输入网站，尤其应核对是否属于你要使用的服务。仅发现信息不会自动保存或登录。
3. 回到第三方登录，选择已保存服务器，使用该服务自己的账号和密码。不能使用微软密码。若服务提供多个角色，选择所需角色完成添加；可再次添加同服务的其他角色。密码不保存，带凭据请求不自动跟随重定向。
4. 服务器可重命名或移除。已有账户仍引用该服务器时，须先移除这些账户，才能移除服务器；重命名不改变认证地址。
5. 首次第三方启动会从 [authlib-injector 官方项目](https://github.com/yushijinhun/authlib-injector) 的发布端点自动准备 **1.2.8（build 56）** JAR，核对固定 SHA-256、限制大小后写入缓存；后续启动再次校验缓存。下载、校验或认证失败会停止启动，无需玩家手工选择 JAR。
6. 启动前刷新会话并验证角色连续性。适配器只加入第三方启动；不能用此账户访问要求微软正版身份的服务器。

已有账户刷新取得有效且身份一致的新会话后，会先保存新凭据，再读取可选的皮肤与披风资料。此时取消仍会停止后续启动，但不会撤销已完成的凭据保存；取消新增登录仍不会添加账户。

若提示新账户凭据已生效但本机保存失败，请保持启动器打开，检查数据目录写入权限和磁盘空间后，再点“刷新资料”或重试启动；程序会先重试保存内存中保留的新凭据。仍不能保存时不会继续启动。若在保存恢复前退出，内存中的新会话无法保留，下次可能需要重新登录。身份不匹配的响应不会进入账户库。

认证适配器从 `authlib-injector.yushi.moe` 下载，不向下载端点发送账户凭据，JAR 会在第三方游戏进程中执行。其原始许可、版权和来源独立于 Wonder，见[第三方声明](../THIRD-PARTY-NOTICES.md)。普通账户流程使用自动准备；底层仍兼容既有手动路径与哈希字段，便于读取本项目旧账户库。

### 离线

输入 3–16 位字母、数字或下划线的玩家名，添加后自动使用。详情中可修改离线玩家名，点击“确认修改名字与 UUID”保存；同名账户已存在时会拒绝改名，改用已有条目即可。离线 UUID 使用 Minecraft 的 `OfflinePlayer:<name>` 规则，名字区分大小写，**改名会改变 UUID 和游戏身份**，可能导致服务器或存档按另一玩家处理。此模式不联网认证，也不验证微软游戏所有权。

既有 Wonder 的离线用户名保留在全局设置中，作为新增账户的预填值；还没有账户条目时需添加一次。0.7.0 兼容本项目 0.6.0 加密账户库，既有第三方账户的 API 地址会进入认证服务器列表；不导入其他启动器的凭据，不重置已有 WonderData 或工作区布局。

## 保护边界

- 账户业务上限统一为 256 个账户、128 字符的内部账户 ID 和 16 字符的角色名；服务层与加密存储使用相同边界，超限提示具体原因。第三方登录邮箱/用户名属于登录标识，不受角色名长度限制替代。离线玩家名仍遵循 3–16 位字母、数字或下划线规则。
- 账户库：`WonderData/accounts.dat`，整个文件经 Windows DPAPI CurrentUser 加密，写入临时文件也只有密文。解密失败保留原文件并阻止写入；先备份再修复，不能靠重试覆盖损坏库。
- 凭据不进入 `workspace.json`、组件持久状态、UI错误详情或测试夹具；测试使用合成账号与合成响应。第三方服务器不能接收 Microsoft 令牌。
- 正版启动会拒绝认证路由覆盖、Java agent 及身份参数覆盖。只有游戏 access token 被送给游戏进程，刷新令牌不进入启动参数。
- Wonder 的输出日志过滤本次游戏令牌的原值及 URL 编码值。未登记的其他秘密、自定义编码、外部游戏日志以及不可信模组不在过滤保证范围内；Windows 同用户恶意程序、管理员和进程内存仍可能泄露游戏令牌。
- 删除本机 Microsoft 账户不撤销云端授权，可到 [Microsoft 应用权限页](https://account.live.com/consent/Manage) 撤销；Yggdrasil 删除会尝试使服务器令牌失效，失败时提示。
- 便携布局和实例仍可移动，但加密账户库绑定原 Windows 用户/系统，换电脑后需重新登录。不要将账号库、备份或游戏日志发布到仓库。

## 架构与协议依据

`Wonder.Core/Accounts.cs` 定义不带 UI 的身份、外观与服务器资料；`AccountService` 统一账户管理、服务器确认、取消、持久化回滚及会话取得。Infrastructure 包含 `WindowsAccountStore`、`MicrosoftAuthenticator`、`YggdrasilAuthenticator`、认证服务器发现、适配器下载及官方纹理读取；Desktop 使用 `AccountComponents` 和分离登录/外观页面的 `AccountManagerWindow`。现有 `InstanceService` 在构建游戏计划前请求已选账户，`LocalGameLauncher` 固定身份并在输出端脱敏。

协议参考：[Microsoft 设备授权](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-device-code)、[Windows DPAPI](https://learn.microsoft.com/en-us/windows/win32/api/dpapi/nf-dpapi-cryptprotectdata)、[Yggdrasil 服务规范](https://yushijinhun.github.io/authlib-injector/en/yggdrasil-server-technical-specification.html)、[authlib-injector 启动器规范](https://yushijinhun.github.io/authlib-injector/en/launcher-technical-specification.html)。Minecraft 请求路径使用 [PrismLauncher 认证实现](https://github.com/PrismLauncher/PrismLauncher/tree/develop/launcher/minecraft/auth/steps) 作为互操作参考；账户列表、外观操作和服务器确认参考了 [HMCL 账户界面](https://github.com/HMCL-dev/HMCL/tree/main/HMCL/src/main/java/org/jackhuang/hmcl/ui/account) 的功能组织。Wonder 实现独立编写，未复制这些项目的代码或资源；本轮范围不代表复刻 HMCL 的全部功能。

## 验证记录

0.7.1 的历史验证为 **759 项全部通过**（Workspace 210、Game 268、Desktop 281），包含新增登录诊断、离线默认皮肤及网站选肤用例；真实 Java 自身 UUID 哈希验证通过。0.7.2 验证结果见[外观说明](30-layered-skins-capes-and-avatars.md)。以下保留 0.7.0 的历史验证范围。

0.7.0 Release 构建 **0 警告、0 错误**；完整测试 **712 项通过、0 失败、0 跳过**（Workspace 210、Game 224、Desktop 278）。本次新增服务测试覆盖内置应用 ID、刷新与重新登录不改变选择、身份连续性、改名、服务器发现确认与删除保护、自动适配器，以及外观失败时保存已旋转凭据、取消后不覆盖本地资料。

DPAPI、认证协议、取消竞争、启动身份固定及日志脱敏的回归覆盖继续通过。10 项账户桌面测试检查实际点击、浏览与启动选择分离、设备码取消、服务器确认、披风与恢复默认的明确操作、外部切换同步、登录回调不打断新页面，以及本地皮肤预览优先于迟到下载、默认皮肤的披风显示。已查看账户详情、登录、服务器、紧凑尺寸、桌面组件及角色正反面的 Headless 渲染。

`Wonder.JavaSmoke` 真实合成 Java 子进程验证通过，覆盖离线/模拟在线身份、中文和空格路径、stdout/stderr、长令牌脱敏和清除继承的 `JAVA_TOOL_OPTIONS`，不连接认证服务。实际下载的官方 authlib-injector 1.2.8 为 349681 字节，SHA-256 与内置固定值相符，下载验收没有执行该 JAR。报告路径为 `artifacts/accounts-java-smoke/java-smoke.json`，界面截图路径为 `artifacts/accounts-preview/`，均不提交到仓库。

发布程序位于 `artifacts/Wonder-win-x64/Wonder.Desktop.exe`。发布前后已核对主目录及发布目录下的 4 个顶层 WonderData 文件，SHA-256 均未改变；没有读取真实凭据或将用户数据用作测试夹具。发布包同步包含帮助文档与第三方原始许可。

仍需人工完成：真实 Microsoft 账户授权、实际 AppID 服务访问、本人皮肤/披风修改、自选第三方服务登录及真实游戏多人认证。合成测试与 Headless 渲染不能替代这些线上验收，也不代表已提交或通过 AppID 审核；独立离线和第三方模式仍不能证明微软游戏所有权。

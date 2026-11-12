# 隐私说明 / Privacy statement

适用版本：Wonder Launcher 0.7.2。更新日期：2026-11-12。维护者：[TouristH](https://github.com/TouristH)，联系邮箱：[radiafidw@gmail.com](mailto:radiafidw@gmail.com)。

## 当前实现

Wonder 将设置与工作区保存在应用旁的 `WonderData/`：Windows/Linux 位于可执行程序旁，macOS 位于 `Wonder Launcher.app` 同级，不写入 `.app` 内部。三端共用设置与工作区格式。没有项目自建云服务、账号同步或遥测上传功能。用户主动登录时会连接所选认证服务。

| 本地数据 | 用途 |
| --- | --- |
| `settings.json` | 主题、个性化和交互参数 |
| `downloads.json` | 下载任务、资源元数据、安装目标与恢复状态；不含账户凭据 |
| 实例目录内 `.minecraft/wonder.instance.json` | Wonder 创建署名、实例身份与启动参数，随实例保存，不含账户令牌；实例内部路径使用相对位置 |
| `workspace.json` | 功能区、组件快捷方式、便笺等应用状态 |
| `plugins/`、`plugin-settings.json`、`plugin-data/` | 插件安装文件、启用状态、对插件文件指纹的同意记录、配置及插件自己的数据 |
| `instances.json` | 用户主动登记的游戏路径、启动设置、所选实例及离线用户名 |
| `accounts.dat` | Windows 当前用户 DPAPI 加密的账户库，包括账户资料、所选身份、刷新/游戏令牌、公开 Client ID、皮肤 URL/模型和自有披风资料、最近验证时间、保存的认证服务器及第三方适配器路径/哈希 |
| `accounts.linux.dat` | Linux 使用 AES-GCM 加密的同类账户资料；随机密钥保存在桌面 Secret Service 密钥环，文件内仅含密文和解密所需的非秘密标识、随机数及验证标签 |
| `accounts.macos.dat` | macOS 使用同一 AES-GCM 库格式的账户资料；随机密钥通过系统 Security.framework API 存入当前用户钥匙串，不以命令行参数或明文文件传递 |
| 缓存与运行日志 | 本地启动所需临时内容与诊断；日志可能含用户名、路径和游戏输出 |
| `logs/` | 本地轮转的启动器异常诊断，记录异常类型、错误码和调用方法，省略异常原文和源文件路径 |
| `CrashReports/` | 经用户确认保存的本地 ZIP，汇总启动与游戏诊断日志、崩溃报告和运行信息 |

用户主动选择游戏目录后，程序读取与实例发现和启动相关的版本元数据、库和已有启动设置。官方 profile 的账号字段不作为登录凭据导入；其他启动器的账号令牌也不在适配范围内。程序不递归扫描整个磁盘寻找个人信息。

实例/目录删除须输入 `sure`，使用系统回收站；目录设置可执行实际同盘移动或改名。操作前应核对界面显示的目标范围，具体行为见[实例管理说明](Guides/22-instance-selection-and-directory-actions.md)。

启动后的 Java 游戏进程、用户安装的模组和所连接的服务器可能独立访问网络、处理账号或产生游戏日志；这些行为不由本声明代表。程序默认不上传本地设置或日志。向 Issues 或邮箱发送诊断信息属于用户主动分享，应先移除令牌、私人路径、存档与个人信息。

启动和运行期间，Wonder 会过滤启动记录中的当前游戏访问令牌，将记录写入系统临时目录，并在本次启动流程结束后移除临时文件。保存崩溃诊断包需确认。ZIP 仅保存在本地，打包时会过滤诊断文本中的当前游戏访问令牌。游戏或模组记录的用户名、路径、服务器地址及其他敏感信息仍可能出现在包内。诊断包可以从 `WonderData/CrashReports/` 单独删除。

## 登录与凭据

刷新在线插件仓库时，Wonder 向用户选择的 HTTPS 索引地址请求目录，默认托管于 GitHub。下载安装时连接索引指定的 HTTPS 文件地址，这些服务可收到来源 IP 和请求的文件路径。请求不携带 Wonder 账户凭据。启用的代码插件在启动器进程内运行，具有当前系统用户权限，其网络与数据行为由插件实现决定。插件配置以本地 JSON 保存。

首次启用前，Wonder 在本地读取插件及附带程序集的元数据，提示检测到的敏感 API 引用与无法完整分析的情况；此过程不执行插件代码，也不上传插件文件。用户整体同意后才加载插件，文件变更后需重新确认。自动分析、作者补充说明和人工验证都不构成安全保证；Wonder 不通过此确认限制插件的系统权限。

用户打开下载列表、搜索资源或创建安装任务时，启动器访问 Mojang/Microsoft、BMCLAPI、Fabric、Quilt、Forge、NeoForge、OptiFine、Modrinth、CurseForge 及元数据指定的 HTTPS 文件地址。项目图标从对应平台 CDN 加载并缓存在内存中。这些下载服务可收到来源 IP、搜索词和所请求的版本，下载请求不携带 Wonder 账户令牌。镜像选择、默认资源平台和并发参数保存在本地设置。Java、游戏、资源和未完成片段保存在用户选定的安装或下载目录附近。

CurseForge 入口暂时隐藏。继续旧的 CurseForge 任务时，所需的 API 密钥仅随请求发送到 `api.curseforge.com`。环境变量提供的密钥由用户管理。下载和图标请求不携带该密钥，下载任务记录也不保存该密钥。

微软登录使用设备码授权，权限为 Xbox 登录和离线刷新，使用启动器内置的公开 Wonder Client ID，玩家无需填写。密码与多因素验证仅在微软官方网页填写。Wonder 向 Microsoft、Xbox、Minecraft Services 交换本次用户会话并查询 entitlement/角色，不向第三方服务器发送微软令牌。用户点击登录后会打开微软官方页面；设备码只在当前窗口显示，不写入布局或日志，点击“复制设备码”时才写入系统剪贴板。登录可取消，关闭账户窗口取消正在等待的登录。

Yggdrasil 服务器发现会向用户填写的 HTTPS 地址发送不带凭据的元数据请求，可能根据 HTTPS 重定向或服务器声明找到实际 API 地址。界面会显示最终地址，由用户确认并保存；发现阶段不发送密码或账户令牌。登录只向所选服务器的实际 HTTPS API 根地址发送该服务自己的账号和密码，禁止自动跟随带凭据请求的 HTTP 重定向。密码不写入账户库，发送请求前清空密码输入框，登录后的第三方令牌加密保存。请勿在此输入微软密码。离线账户无需网络认证，也不代表游戏所有权。

显示正版角色外观时，Wonder 从 `textures.minecraft.net` 读取官方皮肤/披风纹理，请求不携带账户令牌；该服务仍会收到普通网络请求的来源 IP 等信息。选择皮肤文件只会先读取并预览本地 PNG，点击上传时才向固定的 Minecraft Services 外观接口提交图片和皮肤模型；恢复默认皮肤、应用或隐藏自有披风也由明确的按钮操作触发。外观请求使用经刷新验证的本人游戏会话，不向自定义服务器发送正版令牌。远端可能在请求超时前已接受修改，失败后应刷新资料核对。

第三方登录和刷新后，会向已选择认证服务器的 session profile 接口查询所选 UUID 的公开皮肤与披风属性，该查询不发送密码或令牌。显示第三方外观时会访问其声明的 HTTPS 纹理域名，纹理请求不携带账户凭据、不跟随重定向；相关服务器仍可收到来源 IP 等普通网络信息。第三方纹理默认阻止私网、回环和其他受限网络目标；用户可在单个账户中明确允许可信 HTTPS 来源，许可不会从认证服务器或外观资料自动取得。该许可与纹理 URL/模型和当前披风资料一起保存在加密账户库，可逐项移除。第三方纹理直接连接，不经过系统 HTTP 代理，不发送 Cookie 或自动凭据；放行内网也仍执行系统 TLS 证书校验。解码图片仅在内存中显示于账户详情及原有账户组件头像，关闭控件时释放。工作区布局不保存这些账户信息。预览不会上传图片，也不向游戏注入皮肤。

Windows 整个账户文件采用 DPAPI CurrentUser 加密。Linux/macOS 共用 AES-GCM 账户存储实现。Linux 的随机 256 位密钥通过 `secret-tool` 交给桌面 Secret Service 保存；密钥环条目标签为 `Wonder Launcher account vault`，关联信息为应用名、用途和随机账户库 ID，不包含游戏账户或用户名。macOS 使用原生 Security.framework API 将随机密钥保存为当前用户钥匙串中的通用密码项，服务名为 `org.wonderlauncher.account-vault`，账户字段为随机库 ID，系统可能请求解锁或授权访问。密钥不会写到程序目录、命令行参数或日志。

Linux/macOS 需要原用户的系统密钥服务；不可用、被拒绝或密钥缺失时账户操作失败，不回退为明文。三个平台的原子替换都只写密文；读取或解密失败时保留原文件并阻止覆盖。复制文件不会复制系统密钥，也不会自动迁移平台账户文件。

上述保护依赖原系统用户及其系统保护服务，不能保证跨电脑解密，也不能防止同一用户权限下的恶意程序、管理员、内存转储或恶意游戏/模组读取运行中会话。游戏进程启动参数必须含 Minecraft access token；不将 Microsoft refresh token 传入游戏。Wonder 展示的输出会过滤当前游戏令牌的原值及 URL 编码值；未登记的其他秘密、自定义编码、外部游戏日志和崩溃报告不受此过滤保证。

账户窗口内删除会移除本机账户及保存凭据，第三方同时尽力调用 invalidate；网络失败会提示服务器注销未确认。微软账户删除不等同于撤销云端应用授权，可在 [Microsoft 账户的应用权限](https://account.live.com/consent/Manage) 中另行撤销。删除不停止已经启动的游戏，不保证底层磁盘或备份的安全擦除。

第一次第三方启动会从 `authlib-injector.yushi.moe` 官方端点下载固定的 authlib-injector 1.2.8，限制文件大小并核对预置 SHA-256 后原子写入本地缓存；以后使用前继续核验。下载不携带认证服务器密码或账户令牌，下载服务仍可收到来源 IP 等普通请求信息。JAR 会在第三方游戏进程中执行，不加入微软正版启动命令；其独立许可与来源见[第三方声明](THIRD-PARTY-NOTICES.md)。

离线默认皮肤选项与对应 UUID 保存在加密账户库中。应用前提示 UUID 变化的影响。预览优先读取已登记游戏实例客户端 JAR 的默认纹理。本地缺少纹理时，账户详情会从 Mojang 固定的 Java 客户端地址请求包含默认皮肤的文件片段，校验后在内存中缓存。请求只包含公开文件地址和字节范围。第三方选肤按钮向默认浏览器交付保存的 HTTPS 网站地址，网站独立处理登录与上传。

## 数据移除

关闭 Wonder 后，可自行备份并删除应用旁的 `WonderData/`（macOS 为 `.app` 同级）以移除其本地设置和登记。该操作不等于删除外部游戏目录，也不会代替在游戏服务中管理账号。删除 `WonderData/` 会丢失便笺和布局，请先备份。

Linux/macOS 删除账户条目或 `WonderData/` 不会自动删除系统密钥服务中的账户库密钥。彻底弃用某个账户库及其备份后，可在桌面密码管理器中移除对应条目：Linux 标签为 `Wonder Launcher account vault`；macOS 可在“钥匙串访问”中查找服务 `org.wonderlauncher.account-vault`，按随机库 ID 区分。删除密钥后，原账户库和备份将无法解密。不要误删仍在使用的其他便携目录所对应的密钥。

## English summary

Version 0.7.2 stores local settings and an account vault in WonderData beside the application; on macOS this folder is outside and beside the .app bundle. Windows uses CurrentUser DPAPI. Linux and macOS share an AES-GCM vault implementation with separate platform files. Linux keys are held by the desktop Secret Service via secret-tool; macOS keys are held by the current user Keychain via native Security.framework APIs. An unavailable or denied key store blocks account operations rather than allowing plaintext storage. Copying the data folder does not copy system keys. Removing the vault does not automatically remove its keyring or Keychain entry. Microsoft device-code authentication uses the built-in public application ID and sends no Microsoft password to Wonder. Yggdrasil discovery sends no credentials and requires confirmation of the actual HTTPS API address; login passwords go only to that service and are not persisted. Official texture downloads carry no bearer token. Skin uploads, reset, and owned-cape changes require explicit actions and use fixed Minecraft Services endpoints. Third-party launch downloads and verifies the pinned authlib-injector from its official host without account credentials. These hosts can receive ordinary network metadata such as the source IP. Online launch refreshes the selected identity; failures do not fall back to offline. Local deletion removes stored credentials, but Microsoft cloud consent must be revoked separately. Game access tokens necessarily reach the game process; same-user malware, untrusted mods, and external game logs are outside at-rest encryption and launcher-output redaction guarantees. No project telemetry, cloud account service, or token-import feature is implemented. Live authentication and AppID approval are not asserted by synthetic tests.

Third-party appearance lookup sends the selected UUID to that authentication service's public session-profile endpoint without account credentials. Its declared HTTPS texture hosts receive image requests without tokens or redirects. Texture metadata stays in the encrypted vault; decoded images remain in view memory and are not persisted in workspace layout.

After a launch failure or game crash, Wonder asks before saving a diagnostic ZIP under `WonderData/CrashReports/`. The ZIP stays local and may contain paths, usernames, and data recorded by the game or mods. Review the contents before sharing.

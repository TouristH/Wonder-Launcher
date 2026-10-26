# Minecraft Services AppID 审核说明

更新日期：2026-10-25。本文区分当前实现、官方表单要求和计划；不是已通过审核的证明。

## 项目身份

- 申请应用显示名：**Wonder Launcher**。中文短名：奇想启动器。
- 开发者：[TouristH](https://github.com/TouristH)。联系邮箱：[radiafidw@gmail.com](mailto:radiafidw@gmail.com)。
- 公开源码仓库：[TouristH/Wonder-Launcher](https://github.com/TouristH/Wonder-Launcher)。
- 当前版本：0.7.2。Windows x64 桌面程序，使用 .NET 10 与 Avalonia。
- 许可：[GPL-3.0-only](../LICENSE) 及适用的[第 7 条附加条款](../ADDITIONAL_TERMS.md)，[品牌政策](../BRANDING.md)单独说明来源与商标边界。第三方依赖另行声明，不将代码开源等同于服务访问授权。
- Application (Client) ID：维护者提供 `fab70daf-af0d-423d-9468-6b42edce3c81`；Directory (Tenant) ID、注册配置和 Minecraft API 访问授权仍需维护者核实。公开 ID 不是授权成功的证据。

## 已核对的官方要求

已读取 [AppID Review 表单](https://aka.ms/mce-reviewappid) 的当前说明：

1. 应用应在允许游戏或访问服务器/站点前完成身份认证和游戏所有权验证；不得绕过或禁用认证、所有权、许可或安全检查。该要求不因服务器配置不同而豁免。
2. 新申请应用名不得包含表单列明的 Mojang、Minecraft、Microsoft、Live、Xbox、Discord 或 Hypixel。注册名使用 Wonder Launcher；提交前须统一注册、登录授权页、对外产品说明与实际发布显示名。
3. 新申请需提交联系邮箱、应用名、Client ID、Tenant ID、项目网址和合理的 API 访问用途说明。联系信息需要能与 Azure Portal 信息核对。

审核规则以提交时官方表单为准。仓库介绍和非官方声明帮助审核者理解项目，不能替代认证实现或保证批准。

## 当前实现与代码入口

| 行为 | 事实与代码 |
| --- | --- |
| 本地实例发现 | [InstanceDiscovery.cs](../src/Wonder.Infrastructure/Instances/InstanceDiscovery.cs) 读取启动元数据，不导入账号登录状态 |
| 本地离线启动 | [LocalGameLauncher.cs](../src/Wonder.Infrastructure/Launching/LocalGameLauncher.cs) 保留显式离线账户方式；不是线上认证或所有权证明 |
| 启动入口 | [InstanceService.cs](../src/Wonder.Application/InstanceService.cs) 从 AccountService 获取选中账户会话，正版与第三方失败不会自动降级 |
| 账号数据排除测试 | [InstanceDiscoveryTests.cs](../tests/Wonder.Game.Tests/InstanceDiscoveryTests.cs) 使用合成账号字段验证不导入 |
| 微软 OAuth、Xbox/XSTS、Minecraft Services | [MicrosoftAuthenticator.cs](../src/Wonder.Infrastructure/Accounts/MicrosoftAuthenticator.cs) 实现设备码、刷新和认证链；账户库 Windows DPAPI 加密。当前仅完成合成协议测试，尚无真实授权成功证据 |
| 所有权检查、有效会话与强制启动门禁 | 正版路径检查 entitlement/profile，失败拒绝启动；离线和第三方模式仍独立存在，没有覆盖所有路径的正版门禁 |

不将本地目录、离线用户名或离线 UUID 当作游戏所有权证明，不将“不会连接正版服务器”当作绕过验证的理由。当前源码公开是为了透明展示进展，不应以 0.7.2 声称已满足 AppID 申请条件。

## 申请前必须落实的认证方案

以下区分已实现控制与尚待完成的申请条件：

1. 已配置维护者提供的公开 Client ID，使用用户主动发起的官方设备码流程，不嵌入客户端机密、不收集微软密码；须核实 Entra 个人账户、公共客户端与 Xbox 权限等配置。
2. 正版路径已实现 Microsoft / Xbox / Minecraft 服务链、entitlement/profile 验证；失败拒绝启动、不自动降级。仍需真实账户、受限账户、无所有权和 API 授权的线上验收。
3. 要满足表单所述所有路径门禁，仍须解决独立离线/第三方模式与审核要求之间的差异；本次用户明确要求三种登录方式，不能声称现状已满足这一条件。
4. DPAPI 当前用户加密、删除凭据、取消与日志脱敏已实现，[PRIVACY.md](../PRIVACY.md) 已描述边界；同用户进程、游戏/模组及外部游戏日志不受加密保护。
5. 待真实注册和线上证据齐备后再准备申请。本次未提交审核，也没有批准结果。

官方身份流程参考：[Microsoft identity platform device authorization grant](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-device-code)。实现及测试入口见[账户说明](26-account-management.md)。

## English reviewer summary

**Application:** Wonder Launcher. **Maintainer:** TouristH. **Contact:** radiafidw@gmail.com.

The application provides a customizable desktop workspace and local Minecraft Java Edition instance management. The intended API integration is user-initiated Microsoft authentication followed by official game-ownership and profile/session checks for the user's own gameplay.

**Current limitation:** version 0.7.2 implements Microsoft device authentication, Xbox/XSTS, Minecraft entitlement/profile checks, DPAPI storage and failure-closed Microsoft launch. These controls have synthetic tests, not confirmed live authentication. Independent offline and Yggdrasil modes remain; universal authenticated ownership gating is not satisfied. No AppID approval or successful live authenticated launch is asserted.

The repository excludes credentials, user data, game binaries and assets. Source access does not grant any Minecraft account or game entitlement. The public Client ID was supplied by the maintainer; its registration and service access still require verification. Privacy behavior and implementation evidence are documented separately from approval claims.

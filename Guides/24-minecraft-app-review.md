# Minecraft Services AppID 审核说明

更新日期：2026-10-25。本文区分当前实现、官方表单要求和计划；不是已通过审核的证明。

## 项目身份

- 申请应用显示名：**Wonder Launcher**。中文短名：奇想启动器。
- 开发者：[TouristH](https://github.com/TouristH)。联系邮箱：[radiafidw@gmail.com](mailto:radiafidw@gmail.com)。
- 公开源码仓库：[TouristH/Wonder-Launcher](https://github.com/TouristH/Wonder-Launcher)。
- 当前版本：0.5.1。Windows x64 桌面程序，使用 .NET 10 与 Avalonia。
- 许可：[GPL-3.0-only](../LICENSE) 及适用的[第 7 条附加条款](../ADDITIONAL_TERMS.md)，[品牌政策](../BRANDING.md)单独说明来源与商标边界。第三方依赖另行声明，不将代码开源等同于服务访问授权。
- Application (Client) ID / Directory (Tenant) ID：本次发布未提供可核验的注册信息；实际申请时由维护者填写真实注册值。

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
| 本地离线启动 | [LocalGameLauncher.cs](../src/Wonder.Infrastructure/Launching/LocalGameLauncher.cs) 使用离线用户名/UUID、占位 token/session 和空 Client ID/XUID；不是有效线上认证 |
| 启动入口 | [InstanceService.cs](../src/Wonder.Application/InstanceService.cs) 与桌面启动组件；下方提交条件中的门禁尚待实现 |
| 账号数据排除测试 | [InstanceDiscoveryTests.cs](../tests/Wonder.Game.Tests/InstanceDiscoveryTests.cs) 使用合成账号字段验证不导入 |
| 微软 OAuth、Xbox/XSTS、Minecraft Services | 尚未实现，没有服务端请求或令牌存储实现可供审核 |
| 所有权检查、有效会话与强制启动门禁 | 尚未实现；现有离线启动是明确的审核阻塞 |

不将本地目录、离线用户名或离线 UUID 当作游戏所有权证明，不将“不会连接正版服务器”当作绕过验证的理由。当前源码公开是为了透明展示进展，不应以 0.5.1 声称已满足 AppID 申请条件。

## 申请前必须落实的认证方案

以下均为计划与验收条件：

1. 维护者注册自己的公开客户端应用，以用户主动操作发起 Microsoft 官方身份流程；不嵌入客户端机密，不借用官方或其他启动器的 Client ID，不收集微软密码。具体授权流程和最小权限须按真实 Entra 注册配置落实。
2. 通过官方 Microsoft / Xbox / Minecraft 服务链获取属于该用户的会话、游戏所有权和资料；所有权未知、未拥有游戏、登录失败、会话失效或 API 拒绝时，不允许进入游戏。不能降级到当前未经验证的离线启动路径。
3. 在所有 UI、组件和服务调用入口统一执行认证与所有权门禁，并覆盖不能通过设置或额外启动参数绕过的测试。
4. 明确实现凭据保护、退出登录、清除凭据与日志脱敏后，更新 [PRIVACY.md](../PRIVACY.md)。当前不对尚不存在的加密或令牌实现作承诺。
5. 整理可复现的认证、无所有权拒绝、会话过期、取消登录和不同服务器配置的验收证据，然后使用真实注册信息提交一次申请。

官方身份流程参考：[Microsoft identity platform device authorization grant](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-device-code)。这是方案参考，不能作为当前代码已实现该流程的证据。

## English reviewer summary

**Application:** Wonder Launcher. **Maintainer:** TouristH. **Contact:** radiafidw@gmail.com.

The application provides a customizable desktop workspace and local Minecraft Java Edition instance management. The intended API integration is user-initiated Microsoft authentication followed by official game-ownership and profile/session checks for the user's own gameplay.

**Current limitation:** version 0.5.1 only supports local offline launch. Authentication, Minecraft Services calls, ownership checks, and the mandatory authenticated launch gate are not implemented. The existing offline path must be corrected before this application can be represented as meeting the AppID review requirements. No approval, registered Client ID, or successful authenticated launch is asserted in this publication.

The repository excludes credentials, user data, game binaries and assets. Source access does not grant any Minecraft account or game entitlement. We will provide the project's own Client ID, Tenant ID, implementation evidence and updated privacy information only when those items are available and verifiable.

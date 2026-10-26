# 账户管理设计与实施计划

日期：2026-10-25。目标：在现有组件系统内新增账户管理，支持添加、删除、选择 Microsoft、Yggdrasil 与离线账户，并将选中身份接入现有实例启动。

## 设计

- 延续 Wonder.Core / Application / Infrastructure / Desktop 分层，直接在主目录实现；保持已有工作区、实例、组件生命周期和 WonderData。
- 账户管理组件及独立窗口共享 AccountService；组件状态仅含账户显示资料，不含令牌、密码或设备凭据。
- Microsoft 使用官方设备授权流程和系统浏览器；默认公开 Client ID 为用户提供的 `fab70daf-af0d-423d-9468-6b42edce3c81`。不收集微软密码，不嵌入 Client Secret，不借用其他应用 ID。继续 Xbox / XSTS / Minecraft Services 流程，检查 entitlement 和 profile，失败不自动退回离线身份。
- 第三方按用户确认的自定义 Yggdrasil 服务器实现，显式 HTTPS API 地址，禁止重定向转发凭据；支持多角色选择和刷新。密码只在本次请求使用。第三方启动使用用户选择并固定 SHA-256 的 authlib-injector JAR；不将第三方 agent 注入正版启动。
- 离线身份由用户名生成标准离线 UUID，与正版身份清楚区分。升级时保留已有离线用户名，首次使用可登记为离线账户；不将其描述为所有权证明。
- 整个账户库通过 Windows DPAPI CurrentUser 加密并原子写入，不持久化明文密码。文件损坏或解密失败时保留原文件并阻止覆盖；切换 Windows 用户/电脑需重新登录。
- 启动时取得所选账户会话；正版和第三方会话刷新失败即拒绝启动。禁止额外参数覆盖账户身份，日志过滤敏感值。游戏本身必须取得游戏 access token，同用户进程或恶意模组不属于 DPAPI 防护边界。
- 选择设备码而非内嵌登录页，避免启动器接触微软密码；选择系统加密而非自造密钥。AppID 服务授权与真实账户验收仍须实际验证，保留离线方式意味着不能宣称满足官方审核。

## 实施计划

技术：.NET 10、Avalonia 12、HttpClient、Windows DPAPI；不引入跨框架 UI 到核心契约。

- [x] 核心模型和账户服务：账户增删选、持久化失败回滚、并发/取消控制、会话取得。验证无账户/无所有权/刷新失败不会静默降级。
- [x] Microsoft 认证：固定官方端点、设备码轮询及取消/超时、Xbox/XSTS/Minecraft、所有权和 profile 验证、错误信息脱敏。使用合成 HTTP 响应测试，真实凭据不进夹具；真实授权仍待用户参与验收。
- [x] 加密账号库与第三方认证：DPAPI round-trip、磁盘无明文、损坏文件保护；HTTPS、重定向拒绝、多角色和 refresh/invalidate。
- [x] 启动接入：选中身份参数、authlib-injector 隔离与哈希核验、日志脱敏、拒绝覆盖身份的参数，保留当前离线启动行为。
- [x] 桌面组件：账户列表和选择、删除确认、三类添加、浏览器登录提示、取消、配置 Client ID/agent、组件生命周期、无秘密进入 workspace。
- [x] 更新 README、Wonder.md、PRIVACY.md、Guides；完整 Release build/test，发布 artifacts/Wonder-win-x64，保留 WonderData。人工渲染检查账户窗口与组件。最终证据见 26 号文档。

审核重点：磁盘损坏不覆写；取消之后不保存；第三方服务不得收到正版 token；认证失败不得降级；游戏输出不能回显 access token。发布前检查 Git 差异、缓存排除及个人路径，不推送公开仓库。

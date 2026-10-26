# 登录诊断、离线默认皮肤与第三方选肤

适用版本：0.7.1。更新日期：2026-10-26。

## 微软公共客户端配置与 Minecraft AppID 审批是两个阶段

登录顺序为 Microsoft 设备授权 → Microsoft 会话 → Xbox 登录 → XSTS 授权 → Minecraft Services 登录 → 游戏许可 → 玩家资料。新版错误提示保留阶段和 HTTP 状态；Microsoft OAuth 错误只显示白名单协议标识与已知 AADSTS 数字。可以点击“复制诊断”分享这些安全信息，不必分享令牌、密码或账户库。上游自由文本、响应正文和追踪标识不会直接回显。

本轮最初复现的错误是 **HTTP 401 / invalid_client / AADSTS70002**，Microsoft 返回该应用必须标记为移动客户端。这发生在取得设备码以前，等待 Minecraft AppID 审批不能修复这一错误。维护者应在 [Microsoft Entra 管理中心](https://entra.microsoft.com/) 的应用注册中选择 Wonder，打开“身份验证 / Authentication”，在高级设置中将 **Allow public client flows / 允许公共客户端流**设为 Yes 并保存，同时确认支持个人 Microsoft 账户。设备码流程不需要给桌面程序添加 Client Secret。依据：[桌面应用配置](https://learn.microsoft.com/en-us/entra/identity-platform/scenario-desktop-app-configuration#enable-public-client-flow)、[设备授权协议](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-device-code)。

用户启用该配置后已推进到 **Minecraft 登录失败**。按当前代码路径，这说明 Microsoft 会话及 Xbox/XSTS 请求已成功，失败发生在固定的 Minecraft Services 登录接口。**尚未获准的 AppID 服务访问权限是主要排查方向，但通用错误截图不能证明只差审批。**

- **Minecraft 登录 HTTP 403**：核对 AppID 的服务访问审批、获准权限及实际 Client ID；403 是拒绝访问的证据，不是审核状态查询结果。
- **Minecraft 登录 HTTP 401**：会话未被接受，先重新登录；持续失败时检查认证链和应用权限。
- **HTTP 429/5xx 或网络超时**：可能为限流、服务或网络异常，应按实际提示排查，不能等同于未审批。
- 通过 Minecraft 登录后仍需通过游戏许可和玩家资料检查；前一阶段成功不代表全部流程已经完成。

Wonder 内置公开应用 ID，普通玩家不填写开发配置。没有替换成其他启动器 ID，也没有添加跳过官方认证或所有权检查的备用路径。本说明不代表 AppID 已获批。

## 离线默认皮肤

账户中心 → 离线账户详情 → “游戏默认皮肤”。提供 9 种角色各自的经典/纤细模型（Java 1.19.3+ 默认规则），以及旧版 1.8–1.19.2 的 Steve/Alex 选项。

1. 选择一项后显示候选 UUID、新旧版本各自会分配的默认皮肤，以及模型预览；**选择本身不保存**。
2. 点击“应用默认皮肤…”后阅读身份变更提示，再点击“确认更改皮肤与 UUID”保存。
3. 要恢复原身份，选择“自动分配 · 恢复按玩家名生成的 UUID”，再次确认即可。

默认皮肤是按游戏使用的 UUID 哈希规则选择的，因此更改皮肤会改变离线身份。原世界的背包、成就或服务器权限可能不再对应；Wonder 不自动迁移或删除存档。修改玩家名时会保留皮肤选项并重新计算身份。重新添加同名离线账户会选择已有条目，不重置皮肤或创建同名身份。

不同版本的映射并不一致，例如现代版经典 Steve 的 UUID 在旧版可能显示 Alex。界面明确列出两种结果；早于 1.8 的版本通常只有 Steve。服务器可能按用户名重新生成 UUID，模组或资源包也可能覆盖外观，所以此功能不保证服务器中的最终皮肤。它不安装皮肤模组、不注入第三方认证、不上传图片。

预览仅从当前选中、已安装游戏的已知客户端 JAR 位置读取指定默认纹理，限制文件及 PNG 尺寸；未安装相应纹理时显示带说明的模型示意。程序不复制或分发 Minecraft 默认纹理，不扫描整个磁盘或修改游戏 JAR。

默认皮肤名称和双模型由 [Minecraft 官方介绍](https://www.minecraft.net/en-us/article/introducing-new-default-skins)确认；UUID 映射互操作参考 [HMCL TexturesLoader](https://github.com/HMCL-dev/HMCL/blob/main/HMCL/src/main/java/org/jackhuang/hmcl/game/TexturesLoader.java)。Wonder 独立实现 Java UUID 哈希和确定性身份生成，没有复制上游代码或资源。

## 第三方选择皮肤

第三方账户详情中的“选择皮肤 · 打开认证网站”会自动在系统默认浏览器打开该服务器保存的网站地址。先采用服务公开元数据声明的主页；没有主页时使用认证 API 所在域名的 HTTPS 首页。用户在网站中登录并选择皮肤，Wonder 不把密码或令牌加入 URL 或交给浏览器。

如果皮肤站有专门的管理页，在“认证服务器”中填写“皮肤管理网站”并点击“保存网站”。只接受不含用户凭据的 HTTPS URL；留空恢复域名首页。不同 Yggdrasil 服务没有统一皮肤上传接口，所以不猜测管理 API、不假装已经提交皮肤。

## 验证

合成测试覆盖安全登录诊断、全部预设 UUID 与现代/旧版规则、身份保存/重命名/恢复、加密库校验、启动参数和网站 URL 校验。桌面点击验证确认前不改 UUID、恢复原 UUID，以及第三方选肤按钮只在点击时打开指定网站。真实 Java 子进程检查传入的 UUID，并用 Java 自身的 `UUID.hashCode()` / `Math.floorMod` 验证默认皮肤索引。

Release 构建 0 警告、0 错误。完整测试 **759 项通过、0 失败、0 跳过**（Workspace 210、Game 268、Desktop 281）。13 项账户桌面测试均通过并检查渲染；真实合成 Java 进程对默认皮肤 UUID 的 Java 哈希验证通过，报告位于 `artifacts/account-skins-java-smoke/java-smoke.json`。

发布到 `artifacts/Wonder-win-x64/Wonder.Desktop.exe`，主目录和发布目录的 4 个顶层 WonderData 文件在发布前后 SHA-256 相同。没有把真实实例、账户或设置复制进测试夹具。

本轮没有使用真实用户令牌执行 Minecraft 请求，也没有验证真实游戏画面或云端皮肤变更。

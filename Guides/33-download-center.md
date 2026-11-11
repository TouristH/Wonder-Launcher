# 下载组件与下载源

在组件库中找到“下载”，拖入桌面或侧边栏，点击“打开下载中心”。组件显示后台任务状态。关闭下载中心窗口不会停止任务。

## 下载源与并发

打开 **设置 → 启动器设置**，或在下载中心点击“下载设置”：

- 自动：对受支持的 Minecraft 文件优先使用 BMCLAPI，失败后尝试官方地址。
- 官方：使用发布方原始地址。
- BMCLAPI：优先使用 BMCLAPI 镜像，失败后回退官方。

Modrinth 和未提供镜像的服务保留原始地址。切换设置影响后续查询和新任务，已入队任务保留创建时的配置。默认同时下载 8 个文件，每个大文件最多 4 个连接，重试 3 次，可按网络情况调整。小文件或不支持 HTTP Range 的服务器使用单连接。

## 安装游戏

1. 选择 Minecraft 版本，可搜索版本号并筛选正式版或快照。
2. 选择原版、Fabric、Quilt、Forge 或 NeoForge，并选择对应加载器版本。
3. 设置新实例名称和安装父目录。默认位于用户主目录的 `Games/Wonder`，也可自行选择。每个实例使用独立目录，不覆盖已有实例；游戏文件与启动器的 `WonderData` 分开保存。
4. 加入任务，获取游戏、依赖库、资源、日志配置和适配的 Mojang Java；也可指定已有 Java。
5. 完成后自动登记到实例管理。若索引暂不可写，任务提示文件已安装但登记失败，可从实例管理导入结果目录。

游戏文件下载与账户认证相互独立；正版启动仍需有效账户和游戏权限。离线模式不代表满足 Minecraft AppID 审核要求。使用旧版资源索引的实例会在启动准备时生成 virtual/resources 映射，校验内容并保护已有的不同文件。

## 模组、整合包、资源包和光影

资源目录使用 Modrinth。分类内可搜索、翻页，按游戏版本与加载器筛选，查看并选择安装版本。

- 模组：选择兼容的已安装实例，下载到 `mods`，自动解析必需依赖；不能将需要加载器的模组装到原版实例。
- 资源包：下载到 `resourcepacks`，进入游戏后启用。
- 光影：下载到 `shaderpacks`，需要实例已有适配的光影模组，进入游戏后启用。
- 整合包：安装 Modrinth `.mrpack` 到新实例，读取包中游戏与加载器版本、文件哈希和客户端覆盖文件。服务端专用文件不会装入客户端。

已有同名且内容不同的资源文件不会被静默覆盖。更新或替换时先处理旧文件，避免多个版本并存冲突。当前目录不包含 CurseForge，也不使用其他启动器的 API 密钥。

## Wonder 原生实例配置

Wonder 下载的游戏和整合包会在实例的 `.minecraft/wonder.instance.json` 中生成原生配置，包含格式版本、稳定实例 ID、实例名称、Minecraft/加载器版本、创建时间、`createdBy` 中的 Wonder 名称与程序版本，以及 Java、内存、窗口、JVM 和游戏参数等 `launchSettings`。这是来源署名，不是密码学数字签名，也不改变游戏与模组的版权归属。

这份配置随实例保存，不依赖原来的 `WonderData/instances.json`。重新导入实例目录、`.minecraft` 或版本目录时优先读取 Wonder 配置；损坏的 Wonder 配置会报错，不伪装为普通官方实例。内部 Java 和游戏目录使用相对路径，整体移动实例后可以重新解析。外部 Java 绝对路径仍需目标电脑存在对应文件。

在实例设置中保存名称或参数时，同时更新此文件与本地索引。未填写的启动项继续继承全局设置；实例明确设置的值优先。配置不保存账户、访问令牌、布局或其他启动器凭据。读取失败保留原文件，保存失败尝试回滚。手动编辑前请关闭启动器并备份。

## 任务控制

任务管理显示阶段、进度、速度、文件数和错误，提供暂停、继续、取消、重试和打开结果目录。安装任务顺序执行，任务内并发下载文件，避免同一实例被同时修改。

文件校验成功后才提交最终路径。游戏与整合包先安装到 `.wonder-install` 临时目录，全部成功后变为正式目录。取消保留可复用片段，清除任务记录不会删除游戏或临时文件。

退出整个启动器时暂停任务，下次打开后手动继续。任务历史位于 `WonderData/downloads.json`，包含目标路径和资源元数据，不含账户令牌。记录损坏时保留原文件并显示错误。

## 参考与许可

参考 [PCL 多连接、重试与任务机制](https://github.com/Meloong-Git/PCL/blob/main/Plain%20Craft%20Launcher%202/Modules/Base/ModNet.vb)、[HMCL 安装选择](https://github.com/HMCL-dev/HMCL/blob/main/HMCL/src/main/java/org/jackhuang/hmcl/ui/download/InstallersPage.java)和 [Axolotl 下载界面](https://github.com/Axolotl-Launcher/Axolotl/blob/main/apps/app-frontend/src/pages/Downloads.vue)，由 Wonder 独立实现，未复制其源码或素材。使用 [Modrinth API](https://docs.modrinth.com/api/)、Mojang 元数据和 [BMCLAPI](https://bmclapidoc.bangbang93.com/) 公开服务。

下载的 Java、游戏、加载器和资源仍适用各自许可；保留随包许可文件，不将这些内容归为 Wonder 原创。上游可用性、速度和第三方整合包实际兼容性取决于服务及用户环境。

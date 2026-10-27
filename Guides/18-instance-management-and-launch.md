# 实例管理与离线启动

更新日期：2026-10-24（0.5.1）。首轮兼容官方目录、HMCL、PCL、Prism/MultiMC，身份采用明确的离线模式；微软登录后续接入。

## 使用流程

从实例管理组件打开管理窗口，首页左侧以卡片显示游戏根目录，右侧显示所选目录中的实例，可切换长条列表与紧凑网格。添加目录支持已有游戏目录、版本目录或 Prism/MultiMC 实例目录。名称、版本摘要与路径换行完整显示；点击实例卡片只选中并高亮，独立启动组件同步当前目标。卡片右侧“设置”进入详情，不改变启动目标；返回列表保留目录位置和草稿。实例与目录均有“删除”，需警告中准确输入小写 `sure` 后移入系统回收站并移除登记。删除范围、共享资源保护和目录设置见 [实例选择与目录操作](22-instance-selection-and-directory-actions.md)。

版本摘要显示实际 Minecraft 版本和已识别的加载器名称、版本，例如 `Minecraft 1.20.1 · Forge 47.3.0`。支持识别 Fabric、Quilt、Forge、NeoForge，以及元数据中明确记录的 OptiFine；多个条目合并显示。加载器信息来自本地版本描述、继承版本或启用的 Prism/MultiMC 组件，不从自定义文件夹名称猜测。无法识别 Minecraft 版本时显示“版本待识别”，实例仍可登记和管理。读取旧 Wonder 索引时可只读补全缺失的版本信息，不为补全信息改写索引或来源文件；已有版本信息在来源暂不可用时保留。

实例详情按“实例信息”“Java 与内存”“游戏窗口”“高级启动参数”分组。细调包含名称、Java 路径、最小/最大内存、游戏目录、窗口宽高、全屏、额外 JVM 与游戏参数；“当前有效设置与来源诊断”可核对合并结果。表单编辑需要点击“保存设置”，保存后用于下次启动。切换实例保留当前窗口内的草稿，关闭管理窗口前请保存。

## 分类设置与配置优先级

从设置快捷方式、组件库或顶栏菜单打开同一个设置窗口，左侧选择类别：

| 类别 | 调整内容 | 生效与保存 |
| --- | --- | --- |
| 启动器设置 | 长按拖动时间、侧边栏展开尺寸和收起延迟 | 修改立即生效、自动保存 |
| 游戏全局设置 | Java、最小/最大内存、窗口宽高、全屏、额外 JVM 与游戏参数 | 点击“保存全局设置”后用于下次启动 |
| 个性化设置 | 主题、强调色、工作区留白、显示功能区序号 | 修改立即生效、自动保存 |

游戏配置按以下顺序逐项合并，后面明确设置的值覆盖前面：

`内置默认值 → 来源启动器的有效配置 → Wonder 游戏全局设置 → 单个实例覆盖`

全局或实例字段留空表示继承，不会把未填写的字段清零。额外参数留空同样继承；勾选“清空”会明确移除继承的对应额外参数。游戏目录由单个实例管理，不能通过全局设置统一覆盖。

例如，来源最大内存为 4096 MiB、Wonder 全局为 6144 MiB 时，没有单独覆盖的实例使用 6144 MiB；某个实例设为 8192 MiB 后，该实例使用 8192 MiB。“清除实例覆盖”只清除当前实例的启动覆盖，继续继承全局设置或来源配置；全局页面的“恢复继承设置”只清除 Wonder 全局值，保留各实例独立覆盖。启动器与个性化页面的“恢复默认设置”另行恢复外观和交互参数，不清空布局或游戏配置。

外部配置只读。Wonder 索引、离线用户名、游戏全局设置和实例覆盖保存在程序旁的 `WonderData/instances.json`；启动器交互与个性化设置保存在 `WonderData/settings.json`。游戏自身的 options.txt、存档与模组文件不作为启动参数覆盖项写回。

实例索引采用同目录临时写入、显式刷盘并关闭后替换的流程。写入、刷盘、关闭或提交失败时保留旧索引；关闭和清理过程的后续错误不会覆盖首个错误。残留临时文件不会被当作正式索引读取。相关故障注入与实际目标文件占用测试见[第二阶段修复记录](34-second-repair-results.md)。

离线用户名显示在启动组件，在设置→游戏全局设置中单独保存，格式为 3–16 位英文字母、数字或下划线；离线身份不提供正版服务器认证。启动状态组件窗口提供日志与取消准备，实例管理不执行启动。只启动用户已安装且文件完整的实例；本轮不提供下载、修复、账号迁移或启动前后脚本执行。

## 工程边界

- Wonder.Core：实例/设置模型、设置校验、不可变启动计划。
- Wonder.Application：实例目录、导入/保存/选择/启动用例与状态；接口隔离磁盘和进程。
- Wonder.Infrastructure：有界本地文件读取、各启动器适配、版本继承及平台规则解析、Java 检查、原生库提取、进程与日志。
- Wonder.Desktop：目录选择器、管理窗口、两个注册组件及服务装配。工作区仍只负责布局。

组件隐藏和移除只结束显示订阅；正在准备的用户任务和已运行游戏由应用服务持有。启动时固定实例与参数快照，同一服务不允许重复启动；准备过程可取消，游戏正常退出后更新状态。关闭启动器不强制结束游戏。日志有数量和长度限制，不保存凭据或执行 shell 命令。

## 兼容与失败行为

标准实例读取 versions/<id>/<id>.json，支持有界继承和现代/旧式参数。HMCL/PCL 读取实例与明确相关的上级配置；Prism/MultiMC 读取 instance.cfg、mmc-pack.json、patches 及本地元数据缓存。无法完整还原的来源字段展示诊断，不把缺失元数据解释为可启动。未提供账号登录状态导入。

启动计划验证 Java、主类、客户端 JAR、适用库及资产索引等必需文件；缺失项明确列出，原目录不执行隐式联网修复。原生库仅提取到 Wonder 自己的缓存子目录，检查归档越界及体积限制。进程使用 ProcessStartInfo.ArgumentList，禁止 shell 拼接，中文和空格路径作为常规验收项。

## 验收

合成目录覆盖各来源、继承/覆盖/刷新、损坏配置、重复导入、原文件不变、读写失败与恢复。启动测试覆盖版本/平台规则、带空格参数、离线 UUID、缺失依赖、重复启动及隐藏后任务保持。Headless 验证目录分层、详情导航、草稿保留、细调保存、启动状态和组件联动；使用最小 Java 测试程序验证实际进程启动与退出。0.4.1 已使用用户的真实 26.2 客户端、库和资产，在独立测试游戏目录启动到游戏窗口与声音引擎就绪，再正常关闭，详情见[实例分层与启动修正](20-instance-navigation-and-launch-fix.md)。

0.4.1 不再把可选日志 XML 的缺失视为游戏依赖不完整：原文件存在时使用原文件；已知 Log4j2 配置缺失时生成 Wonder 缓存内的本地控制台配置，未知格式则保留游戏默认日志。这个回退不会下载文件或修改原实例配置，必需 JAR、库、资产等检查保持有效。

## 0.4.0 实际支持范围

| 来源 | 读取内容 | 需要留意的边界 |
| --- | --- | --- |
| 官方目录 | `versions/<id>/<id>.json`、`launcher_profiles.json` 中已固定版本的配置；名称、独立游戏目录、Java、堆内存、JVM 参数、分辨率 | 多个 profile 可共用同一版本，各自登记。`latest-release/latest-snapshot` 不作为固定实例导入，目录中的本地版本仍会列出。账号对象不导入。 |
| HMCL 旧格式 | `hmclversion.cfg` 的实例覆盖与隔离；相关 `hmcl.json` 中匹配游戏根目录的 `profiles.*.global` | 手动/已检测 Java 路径与自动模式区分；按版本挑选 Java、自动内存等策略只提示，不复制算法。 |
| HMCL 新格式 | `.hmcl/config/instance-game-settings.json`，按 `overrideProperties` 合并 `parent` 预设；支持 `customJavaPath`、`windowType`、运行目录等 | 在游戏根目录及直接上级的 `hmcl/config`、`.hmcl/config`、`config` 中查找 `game-settings.json`。找不到父预设会提示；实例覆盖的空运行目录表示版本目录隔离。 |
| PCL | 版本目录 `PCL/Setup.ini`、`PCL/config.json`；实例/相关全局自定义内存、JVM/游戏参数、显式隔离、指定 Java 路径 | 支持冒号分隔的 INI，保留参数中的等号；内存滑块换算为 MiB。注册表全局项、自动内存/隔离、Java 范围选择和窗口策略没有完整复刻；在来源诊断中核对，再使用 Wonder 覆盖值。 |
| Prism / MultiMC | `instance.cfg`、全局配置与 Override 标志、`mmc-pack.json`；本地 `patches/<uid>.json` 优先，再读取共享 `meta/<uid>/<version>.json` | 共用 launcher 根目录的 libraries/assets，支持实例本地库和 tweakers；最大化窗口不等同全屏。找不到元数据仍可登记，但启动时明确失败，不启动残缺组件组合。 |

可选择游戏根目录、单版本目录、`versions` 目录、Prism/MultiMC 实例目录（或其 minecraft 子目录）、包含 instances 的启动器目录。扫描限于直接相关位置，不递归搜遍磁盘。重复登记按规范化来源路径及官方 profile 标识合并；刷新保留 Wonder 名称与覆盖设置。

官方固定版本 profile 更换 `lastVersionId` 后，重新读取来源或再次导入会更新同一条 Wonder 登记的版本及元数据，保留原 ID、Wonder 名称、覆盖设置和当前选择。不同 profile 即使共用一个版本也分别保留；单独版本目录与 Prism/MultiMC 的匹配规则保持。若历史数据已有同来源、同 profile 的多条登记，显式刷新仅更新选定条目；再次导入遇到匹配歧义会提示处理重复登记，不自动删除任何一条。可先核对各条覆盖设置，再使用“移除登记”清理不需要的条目。

启动支持本地标准版本继承、库覆盖、系统/架构/功能规则、现代参数和旧式 `minecraftArguments`、Prism 主 JAR 与本地库。检查文件存在、Java 最低版本、资产索引与对象，缺失时提示具体路径。当前不下载或修复依赖，不执行安装处理器、启动前后脚本，不支持 jarMods、额外 agents、未知 Prism traits 和旧式 virtual/map_to_resources 资产映射。外部配置中的特殊优化、自动 Java 下载、渲染器/系统原生库替换等行为没有承诺完整复刻。

准备完成后以 `ProcessStartInfo.ArgumentList` 启动 Java；最小/最大内存与窗口以表单有效值为准，额外参数不能覆盖离线身份、游戏目录或窗口项。stdout/stderr 统一为 UTF-8，解决 Windows Java 默认输出编码与 .NET 不一致的问题。原生库缓存按实例及归档指纹隔离在 `WonderData/cache/natives`，日志仅保留内存中的最近 200 行，每行最多 2048 字符；界面更新合并，隐藏时退订，重新显示时读取最新状态。

实例与全局表单修改均需要分别保存；离线用户名通过游戏全局设置中的“保存离线用户名”单独保存。“重新读取来源”保留 Wonder 名称和实例覆盖，按最新来源配置与现有全局设置重新计算有效值。关闭窗口或移除快捷方式不结束游戏；程序完全退出后不再监控游戏进程。

## 格式参考与代码来源

适配器根据公开文件结构独立编写，未复制其他启动器的实现，也未引入其认证、安装或脚本执行代码。新增基础设施只使用 .NET 标准库。

- HMCL 的新设置、覆盖清单与预设结构参考 [GameSettings.java](https://github.com/HMCL-dev/HMCL/blob/main/HMCL/src/main/java/org/jackhuang/hmcl/setting/GameSettings.java) 和 [GameSettingsPresets.java](https://github.com/HMCL-dev/HMCL/blob/main/HMCL/src/main/java/org/jackhuang/hmcl/setting/GameSettingsPresets.java)；旧字段模式参考 [LegacyGameSettingsMigrator.java](https://github.com/HMCL-dev/HMCL/blob/main/HMCL/src/main/java/org/jackhuang/hmcl/setting/LegacyGameSettingsMigrator.java)。这些上游源码保留各自 GPL 声明，本文链接不表示将上游源码纳入本项目。
- PCL 配置键参考 [Settings.vb](https://github.com/Meloong-Git/PCL/blob/main/Plain%20Craft%20Launcher%202/Pages/PageSetup/Settings.vb)；内存滑块换算使用兼容性测试覆盖边界值。
- Prism 的继承开关与窗口字段参考 [MinecraftInstance.cpp](https://github.com/PrismLauncher/PrismLauncher/blob/develop/launcher/minecraft/MinecraftInstance.cpp)，组件结构参考 [Prism 元数据](https://meta.prismlauncher.org/v1/net.minecraft/1.20.1.json)。
- Java 标准流编码的背景见 [OpenJDK JEP 400](https://openjdk.org/jeps/400)。

首轮具体命令与验证产物见 [实施验收记录](19-instance-management-plan.md)；0.5.0 设置分类与实例卡片说明见 [分类设置与实例卡片](21-settings-pages-and-instance-cards.md)。

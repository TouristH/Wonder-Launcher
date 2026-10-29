# 实例管理与游戏启动

Wonder 可登记并启动已安装的本地 Minecraft 实例，读取官方目录、HMCL、PCL、Prism 和 MultiMC 的相关配置。游戏账户通过[账户中心](26-account-management.md)管理；当前不提供游戏下载、缺失依赖修复或其他启动器账户迁移。

## 添加与选择实例

1. 从组件库或桌面快捷方式打开“实例管理”。
2. 添加游戏目录，可选择游戏根目录、单个版本目录、`versions` 目录、Prism/MultiMC 实例目录或其 `minecraft` 子目录，也可选择包含 `instances` 的启动器目录。
3. 左侧选择游戏根目录，右侧浏览该目录中的实例。右上角可切换列表或网格，排列偏好会保存。
4. 点击实例卡片，只选中启动目标并高亮。卡片右侧“设置”打开详情，不改变当前启动目标；详情中的修改需要点击“保存设置”。
5. 在账户管理中选择启动身份，再点击“启动游戏”组件的任意位置启动当前目标；组件显示选中的实例、版本和账户。

扫描只读取所选目录的直接相关位置，不递归扫描整个磁盘。名称、版本和路径可在卡片中查看；版本摘要根据本地元数据显示 Minecraft、Fabric、Quilt、Forge、NeoForge，以及明确记录的 OptiFine 版本。无法识别时显示“版本待识别”，仍可登记管理。显示版本信息不等于文件已经完整，启动前仍会检查依赖。

## 调整启动设置

实例详情分为“实例信息”“Java 与内存”“游戏窗口”“高级启动参数”。可以调整名称、Java 路径、最小/最大内存、游戏目录、窗口宽高、全屏、额外 JVM 与游戏参数，并在“当前有效设置与来源诊断”核对最终结果。

每个字段依次合并，后面的明确设置覆盖前面：

`内置默认值 → 来源启动器的有效配置 → Wonder 游戏全局设置 → 单个实例覆盖`

留空表示继承；额外参数勾选“清空”表示明确移除继承参数。游戏目录由单个实例管理，不提供全局覆盖。“清除实例覆盖”仅清除当前实例的启动覆盖；全局页面的“恢复继承设置”仅清除 Wonder 全局值。详见[分类设置与实例卡片](21-settings-pages-and-instance-cards.md)。

切换实例或返回列表会保留当前窗口内的表单草稿，关闭管理窗口前请分别保存。外部启动器的配置、游戏 `options.txt`、存档和模组不会因保存 Wonder 启动设置而被改写。

## 刷新与目录管理

“重新读取来源”重新计算来源配置和版本信息，保留 Wonder 名称、实例 ID、覆盖设置及当前选择。官方固定版本 profile 更换版本后，刷新或再次导入会更新同一条登记；不同 profile 即使共用一个版本，也分别保留。

再次导入按来源路径和官方 profile 标识识别已有登记。遇到历史重复登记而无法唯一匹配时会提示，不自动删除条目；先核对覆盖设置，再使用“移除登记”清理不需要的条目。

目录设置可以移动或重命名目录。“删除”会要求精确输入小写 `sure`，确认后移入 Windows 系统回收站。它与“移除登记”的含义不同；实际范围、运行中实例与共享依赖保护见[实例选择与目录操作](22-instance-selection-and-directory-actions.md)。

## 启动状态与失败处理

“启动游戏”和“启动日志”是两个独立组件，可分别在组件库中使用，或拖到不同功能区创建快捷方式。已有启动组件的位置保留，新增日志组件会自动出现在组件库中，不会自动放到桌面。

启动使用点击时的实例、参数和账户。整个“启动游戏”组件都是按钮，准备和运行时显示对应文字，不能重复启动。“启动日志”组件显示共享的任务状态、失败原因和实时脱敏日志；准备期间可点击“取消启动准备”，点击“查看启动状态”可打开状态窗口。

日志组件仅在可见时订阅界面更新，再次显示会读取当前状态和最近日志。隐藏任一组件、移除快捷方式或关闭状态窗口都不会终止后台任务或游戏；取消准备需使用明确的取消按钮。关闭启动器不强制结束游戏，完全退出后不再监控游戏进程。

准备时检查 Java 版本、主类、客户端 JAR、适用库、资产索引与资源对象。缺失项会列出具体路径，不在原目录隐式下载或修复。额外参数不能覆盖身份、游戏目录或窗口设置；在线身份的验证失败也不会自动降为离线启动。离线身份不提供正版服务器认证，也不验证微软游戏所有权。

原生库只提取到 `WonderData/cache/natives`，不会覆盖原实例的原生库。可选日志 XML 缺失时，已知格式使用 Wonder 缓存内的本地控制台配置，未知格式保留游戏默认日志；必需游戏文件仍须完整。启动服务在内存中保留最近 200 行脱敏日志，每行最多 2048 字符，由日志组件和状态窗口共享显示。

实例登记、全局游戏设置和实例覆盖保存在 `WonderData/instances.json`。保存失败保留旧索引，残留临时文件不会被当作正式索引读取。数据保存及错误处理见[便携数据](15-portable-data-and-motion.md)。

## 来源兼容范围

| 来源 | 可读取的主要内容 | 使用限制 |
| --- | --- | --- |
| 官方目录 | 本地版本描述、固定版本 profile 的名称、独立游戏目录、Java、内存、JVM 参数及分辨率 | `latest-release` 和 `latest-snapshot` 不作为固定 profile 导入；目录中的本地版本仍会列出。不导入账号对象。 |
| HMCL | 旧版 `hmclversion.cfg`、相关全局配置；新版实例设置、覆盖清单与父预设 | 找不到预设时显示诊断；自动 Java、自动内存等策略不完整复刻。 |
| PCL | 版本目录中的 `PCL/Setup.ini`、`PCL/config.json` 及相关配置；显式 Java、内存、参数和隔离设置 | 注册表全局项、自动内存/隔离、Java 范围选择及窗口策略不完整复刻，请核对来源诊断并设置 Wonder 覆盖值。 |
| Prism / MultiMC | `instance.cfg`、全局配置和覆盖开关、`mmc-pack.json`、本地 patches 与共享元数据缓存 | 支持相关本地库与 tweakers；最大化窗口不等同全屏。元数据缺失时可登记，但不能启动残缺组合。 |

支持本地标准版本继承、库覆盖、平台与功能规则、现代参数、旧式 `minecraftArguments` 以及 Prism 主 JAR 和本地库。当前不执行安装处理器、启动前后脚本，不支持 jarMods、额外 agents、未知 Prism traits 或旧式 `virtual/map_to_resources` 资产映射。来源启动器的自动下载、特殊优化和渲染器替换等行为不保证完整兼容。

## 格式参考与代码来源

适配器根据公开文件结构独立编写，未复制其他启动器的实现，也未引入其认证、安装或脚本执行代码。相关基础设施使用 .NET 标准库。

- HMCL 的设置、覆盖清单与预设结构参考 [GameSettings.java](https://github.com/HMCL-dev/HMCL/blob/main/HMCL/src/main/java/org/jackhuang/hmcl/setting/GameSettings.java)、[GameSettingsPresets.java](https://github.com/HMCL-dev/HMCL/blob/main/HMCL/src/main/java/org/jackhuang/hmcl/setting/GameSettingsPresets.java) 和 [LegacyGameSettingsMigrator.java](https://github.com/HMCL-dev/HMCL/blob/main/HMCL/src/main/java/org/jackhuang/hmcl/setting/LegacyGameSettingsMigrator.java)。上游源码保留各自 GPL 声明，链接不表示将其源码纳入本项目。
- PCL 配置键参考 [Settings.vb](https://github.com/Meloong-Git/PCL/blob/main/Plain%20Craft%20Launcher%202/Pages/PageSetup/Settings.vb)。
- Prism 的继承开关与窗口字段参考 [MinecraftInstance.cpp](https://github.com/PrismLauncher/PrismLauncher/blob/develop/launcher/minecraft/MinecraftInstance.cpp)，组件结构参考 [Prism 元数据](https://meta.prismlauncher.org/v1/net.minecraft/1.20.1.json)。
- Java 标准流编码背景见 [OpenJDK JEP 400](https://openjdk.org/jeps/400)。

上述引用不改变上游许可，完整归属见[第三方声明](../THIRD-PARTY-NOTICES.md)。

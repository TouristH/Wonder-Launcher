# 插件管理

从组件库点击“插件”，或将它拖到桌面后打开。窗口包含已安装插件与在线仓库。

## 本地插件

“打开插件目录”打开当前便携目录下的 `WonderData/plugins/`。每个插件放在自己的目录中，根部包含 `plugin.json`、入口 DLL 与依赖。“导入 ZIP”可安装符合 Wonder 格式的本地包，导入后显示在已安装列表。

安装后点击“启用”，首次使用时会显示插件名称、版本、启动器自动分析的行为提示，以及可确认的人工验证信息。分析在执行插件代码前读取插件及附带 DLL 的元数据，提示检测到的文件访问、联网、启动程序、剪贴板等调用；作者说明只作补充。阅读提示后选择“同意并启用”才会加载插件代码；不同意或关闭提示窗口，插件保持关闭。已验证插件也需要同意。

这是一项整体知情同意，不提供逐项权限开关或安全沙箱。插件以当前系统用户权限运行，能够访问文件、联网和启动程序。静态分析是对代码中 API 引用的提示，不能保证这些行为一定发生，也不能完整识别反射、动态下载代码或本地库的行为。分析限制会在窗口中显示；未检测到某类调用不代表插件没有该能力，作者说明与人工验证同样不保证安全。

点击“关闭”释放插件实例。已经同意且文件未变化的插件，在下次启动时恢复此前的启用状态；更新、修改插件文件或权限说明后，需要再次同意。普通配置修改不重复要求确认。此前启用但没有同意记录的插件，也需要先确认。

每个插件的配置位于 `WonderData/plugin-data/<id>/config.json`。声明配置字段的插件在安装或发现时自动生成缺失的默认文件；未声明字段的插件在首次读取配置或启用时生成 `{}`。插件声明配置字段且配置符合声明时，“修改配置”默认提供可视化表单，可以切换 JSON；没有字段声明时沿用 JSON 对象编辑器。类型或范围不符的现有值会保留，编辑窗口提示先在 JSON 中修正。新增字段自动补默认值，不覆盖已有值和未知字段。保存有效配置后，已启用插件重新激活并读取；重启失败时恢复原配置。更新安装包会保留数据目录。插件消息与加载错误显示在管理窗口中。

手动修改插件文件后使用刷新重新读取。损坏的清单或不兼容的 API 会显示错误。若 `plugin-settings.json` 读取失败，先备份并修复该文件，再刷新以恢复启停操作。

## 在线仓库

在线仓库预设「原版源（GitHub）」和「镜像源（jsDelivr）」，默认使用原版源。切换源后会刷新列表，也可以手动刷新。网络不可用时，可查看随启动器发布的目录，验证状态以在线刷新结果为准。两种源均读取 Wonder Plugins 的索引，插件 ZIP 从索引中的发布地址下载。安装器验证下载大小和 SHA-256，并检查 ZIP 的解压路径。安装成功后选择启用插件。

当前默认索引地址为 `https://raw.githubusercontent.com/TouristH/Wonder-Plugins/main/public/v1/index.json`。投稿与工具使用方式见 [Wonder Plugins](https://github.com/TouristH/Wonder-Plugins)。仓库使用 C# 工具重新分析安装包，核对必需的权限报告；技术检查通过即可收录，人工验证由维护者在机器人公布精确版本和哈希后回复 `/ap`，已验证版本仍可逐个撤销。网络请求由刷新与安装操作触发，关闭管理窗口会取消该窗口正在等待的网络操作。

同一插件按版本展示。人工验证通过的版本标绿，未验证或验证已撤销的版本标红，并显示对应文字；所有已收录版本都可以下载。验证记录绑定具体版本和安装包 SHA-256，新版本不会继承旧版本的验证状态。本地插件若无法与已获取的验证记录核对，则显示无法确认其验证状态。

## 编写插件

目标运行时为 .NET 10。入口类型实现 `Wonder.Extensibility.Contracts.IWonderPlugin`，提供 `Activate(IPluginContext context)` 和 `Dispose()`。上下文提供 `ConfigurationJson`、`DataDirectory`、`Log(string)`，API 2 增加 `RegisterComponent(PluginComponentRegistration)`。当前宿主同时接受 API 1、2；使用组件注册的新插件须在清单声明 `apiVersion: 2`。入口操作应及时返回，插件自行在 Dispose 中结束其活动。

插件引用与宿主一致的 `Wonder.Extensibility.Contracts.dll`。宿主共享该契约程序集，插件自己的依赖由独立加载上下文解析。发布包包含插件 DLL 和所需依赖，主启动器继续按现有单文件方式发行。

包根的 `plugin.json` 示例为

```json
{
  "id": "wonder.example.hello",
  "name": "Wonder 问候示例",
  "description": "在插件管理窗口显示可配置的问候语。",
  "author": "Wonder contributors",
  "version": "1.0.0",
  "apiVersion": 1,
  "entryAssembly": "Wonder.Example.Hello.dll",
  "entryType": "Wonder.Example.Hello.HelloPlugin",
  "permissions": [
    {
      "name": "读取插件配置",
      "description": "读取此插件的问候语设置，并在插件管理窗口记录问候消息。"
    }
  ]
}
```

`permissions` 是可选的作者补充说明，每项包含 `name` 和 `description`，可解释实际用途、访问的目录或网络范围。该字段不代替启动器自动分析，也不授予或约束系统权限；无需填写此字段才能使用插件。无论是否填写，启用前均须阅读分析结果与整体风险提示并同意。

在线索引包含 `schemaVersion: 1`、`product: "wonder"` 和 `plugins` 数组。每个条目包含 id、name、description、author、version、apiVersion，以及 downloadUrl、sha256、size、sourceUrl；可附带与包内一致的 permissions 和绑定该安装包的 review 记录。同一 ID 可有多个不同版本，不允许 ID 与版本均相同的重复条目。下载 URL 必须指向实际的 HTTPS ZIP，大小与校验值对应其完整字节。

在线仓库下一阶段采用的发布包规范为：根部包含 `plugin.json`、入口 DLL、`LICENSE`、`RISK.md` 和工具自动生成的 `permissions.json`，以及插件依赖。权限报告记录分析器版本、包内文件哈希、检测结果与分析限制；仓库重新分析后核对，缺失或不一致则拒绝收录。这项在线收录规则随仓库重写接入；当前启动器已提供报告生成/核对 API，本地导入仍会独立分析而不信任附带报告。

## API 2 桌面卡片

### 自动配置与表单

在 `plugin.json` 中增加可选 `configurationSchema`，宿主无需执行插件即可生成配置和编辑界面。例如：

```json
"configurationSchema": {
  "version": 1,
  "fields": [
    { "key": "showObservations", "label": "显示观察读数", "type": "boolean", "default": true },
    { "key": "dragDelayMilliseconds", "label": "拖动延迟", "description": "单位毫秒", "type": "number", "default": 60, "minimum": 0, "maximum": 200 }
  ]
}
```

字段类型支持 `string`、`boolean`、`integer`、`number`、`choice`；各字段需有唯一 `key`、`label`、`type`、`default`，可带 `description`。数字支持 `minimum`/`maximum`，字符串支持 `maxLength`，选项使用字符串数组 `options`。默认值也必须合规。字段名对应顶层 JSON 属性，不解释点号或嵌套路径。未知配置属性不会被表单移除。包内声明了 `configurationSchema` 时，在线目录条目也必须携带相同声明；双方均未声明时可省略。

### 注册与展示

在 `Activate` 的同一线程内同步调用 `context.RegisterComponent(...)`。定义 ID 必须以插件 ID 加 `.` 开头。宿主在整次激活成功后公布注册；停用或激活失败时撤回该代次。卡片由宿主渲染，插件无需引用 Avalonia。

```csharp
context.RegisterComponent(new PluginComponentRegistration(
    new ComponentDefinition("example.plugin.card", "卡片", "说明", 220, 96)
    {
        Desktop = new ComponentDesktopOptions
        {
            PlaceOnFirstActivation = true,
            SnapToGrid = false,
            DragDelayMilliseconds = 60,
            MaximumDragTiltDegrees = 6
        }
    },
    new ComponentCard { Title = "卡片", TextKey = "text", FooterKey = "footer" },
    state => new MyComponentRuntime(state)));
```

`MyComponentRuntime` 实现 `IComponentRuntime`，通过 `Values` 字典输出正文和右对齐尾行；正文最多两行并省略溢出文字。`ComponentCard.Style` 可设置颜色、透明度、内边距、圆角与字号。`Changed` 通知刷新；运行时由宿主按可见性调用 `Resume`/`Suspend`，并负责 `Dispose`。组件库预览和拖动预览不创建运行时；实际打开的组件库仍是可交互视图。

`ComponentCard.Rows` 可声明多个 `ComponentCardRow(label, valueKey)` 读数行，每行显示次要色标签和正文色值；`Monospace=true` 使用等宽数字，`HighlightKey` 绑定值为 `true` 时亮起，否则灰显。`SecondaryValueKey` 在行右侧显示次要值，`ActionId` 绑定运行时动作，`AccentColor` 设置左侧强调线。内容较多时使用 `Scrollable=true`：固定标题，正文、行和尾行在原生滚动区内显示，刷新保留滚动位置。`Desktop.SnapToGrid` 默认 `true`，设为 `false` 后不受个性化网格开关影响。

`PlaceOnFirstActivation` 仅在首次加入组件库时放置入口；可用 `FirstActivationOrder` 排序，用入场选项设置延时、淡入和竖向浮入，不反复覆盖玩家位置。`StretchToAreaWidth` 可让卡片随所属功能区宽度变化。移除入口后可从组件库重新拖入。插件停用后原位置保留不可用占位，重新启用恢复；不会清除组件共享状态。拖动选项默认为零，沿用普通组件行为；带延迟的卡片可立即抓取，在工作区内自由摆放，释放停在当前显示位置，也可拖回已打开的组件库移除快捷方式。Esc 或拖到无效目标取消，应用和存档保留。可滚动卡片从标题区拖动，正文用于浏览。

### 真实观察快照

`context.GetObservation()` 返回 `PluginObservationSnapshot?`，不可用时为 `null`。快照包含 UTC 时间、单调累计时间、指针所在启动器窗口、窗口内 DIP 坐标和宽高、静止时长、最近五次点击（新到旧、递增序号、按键类型和单调时间）、最多 64 条 `RecentActions`，以及当前选择实例的 ID/名称。动作覆盖鼠标按下/释放/滚轮、快捷键、组件与功能区拖动、侧栏调整、菜单和所属窗口变化。连续指针移动只更新指针快照，语义拖动中途事件节流。仅观察当前启动器及所属窗口与弹出层；离开或窗口失活后指针为 `null`。输入框只记录控件名称和发生输入，不读取正文；不提供其他程序活动、密码、令牌或游戏内部输入。`SelectedInstance` 表示启动器选择，不能当成已进入游戏的证明。

读取是线程安全的，停用后上下文不再提供数据。已有接口使用默认实现保持 API 1/2 兼容；新插件应对 `null` 降级。实际游戏内事件将在相应施工步骤增加。

## 请稍候施工预览

《请稍候》0.3.0 在第 1–11 步基础上推进了第 12–18 步：增加解说条、真实操作驱动的旁白、打断与同类节流、配套卡片和首次入场。Minecraft 关卡与结局尚未实现。

1. 执行 `pwsh -File packaging/Wonder.PackagePleaseWait.ps1`，得到 `artifacts/Wonder-plugins/wonder.please-wait-0.3.0.zip`。宿主使用本轮构建的 API 2 版本；已安装旧版时导入新包更新，数据目录保留。
2. 在插件管理中导入 ZIP，启用并阅读插件行为提示。首次按解说条、出勤表、工具面板、记录本、实例列表的顺序放置五张卡片，每隔 300ms 淡入并上浮 4 DIP；没有功能区时会创建一个。已有位置保留，不反复重排或补回主动移除的入口。
3. 用记事本打开 `WonderData/plugin-data/wonder.please-wait/state.json`，把 `text` 改为自己的一句话，保存，然后重开启动器或停用再启用插件。新文字出现在卡片上，尾行保留上一轮实际显示的文字。
4. 卡片无网格吸附，60ms 跟随延迟、最多 ±6° 倾斜。出勤表以 220 × 180 的紧凑尺寸显示，正文用滚轮或滑块查看隐藏内容，从标题区拖动。拖回组件库或右键可移除快捷方式，下次打开保留其余卡片位置。
5. 移动光标、停留、点击或拖动控件，读数每 50ms 检查一次；静止 0.5 秒后显示实际停留，移动归零。最近五次点击亮 600ms 后灰显；可滚动查看近期行为，切换实例同步读到名称。旁白先说“玩家打开了启动器。”，真实输入或移动会清空当前句，静默至少 300ms；快速移动或停留不足 1 秒时保持沉默，同类句子 8 秒内不重复。
6. “修改配置”可以调整显示读数、拖动延迟与倾斜。关闭显示读数后恢复 220 × 96 卡片，仍记录会话。保存配置会重启插件。

工具面板当前显示四行未解锁功能，记录本显示“第 1 天”；实例列表有“请稍候”行动入口，但明确提示“关卡尚未接入”，不会伪造已创建或启动游戏。解说条随所属功能区宽度变化，两侧各留 24 DIP，保持单行。完整开发示例、字段说明与事件名见[插件开发指南](36-plugin-development.md)。

停用/退出后 `state.json` 的 `sessions` 追加一条 UTC `start` 与整数 `minutes`，每条一行；实际时长使用单调时钟。`lastSeenUtc` 用于重启后的离开间隔：不足 2 小时显示“2 小时内”，随后按小时、天、超过 30 天按月；五档边界为 2 小时、24 小时、7 天、30 天。系统时钟倒退或会话内跳变会标记 `clockAdjusted`。首次启动显示“首次”；光标在启动器外显示“未观测”。

默认文件含 `step: 1`、`text: "测试"`；第一次实际显示后写入 `lastDisplayedText`。`step` 供施工验证，手工修改不会被强制重置，不表示已通关。首次没有上一句时尾行为空，下一次显示“上次：测试”。文件是 UTF-8 无 BOM、两格缩进、可读中文；未知字段保留。JSON 格式错误时显示明确错误并保留原文件，修正后重新启用。停用、移除入口和更新插件都不删除此目录。

插件源码位于 `src/Wonder.Plugin.PleaseWait/`，只有 Contracts 项目引用。打包脚本保留 GPL-3.0-only、附加条款与品牌通知，不包含玩家数据。这个本地开发包尚未提交到在线仓库。

# Wonder 插件开发指南

本指南对应本地 dev 的 API 2，与《请稍候》0.3.0 一起开发。使用方式见 [插件管理](35-plugins.md)，可编译的完整实现见 `src/Wonder.Plugin.PleaseWait/`。目前插件提供声明式卡片、共享状态、配置和启动器交互观察；Minecraft 游戏内事件与剧情关卡尚未开放。

`Wonder.Extensibility` 是与 `Wonder.Core`、`Wonder.Desktop` 并列的插件运行时工程，负责加载、生命周期、配置、行为报告和组件布局计算。`Wonder.Extensibility.Contracts` 定义插件调用的接口和数据结构。`Wonder.Desktop/Plugins` 接入 Avalonia 窗口、组件渲染和输入事件，工作区继续持有和保存实际布局。

## 1. 创建一个独立插件

安装 .NET 10 SDK，创建类库，目标为 `net10.0`。只引用与目标宿主一致的 `Wonder.Extensibility.Contracts.dll`，不引用 Desktop、Avalonia 或启动器内部实现。源仓库中的项目可像《请稍候》一样使用 ProjectReference；仓库外开发使用下面的引用，路径改成自己的 SDK 构建路径。

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="Wonder.Extensibility.Contracts">
      <HintPath>../sdk/Wonder.Extensibility.Contracts.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <None Update="plugin.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

项目名为 `Wonder.Plugin.Counter`。插件目录/ZIP 根部放置 `plugin.json`、`Wonder.Plugin.Counter.dll` 及实际使用的依赖与许可证。契约程序集由宿主共享，不将自己的契约副本当作新类型系统加载。

```json
{
  "id": "wonder.counter",
  "name": "计数示例",
  "description": "展示配置、共享状态和组件动作。",
  "author": "Wonder contributors",
  "version": "1.0.0",
  "apiVersion": 2,
  "entryAssembly": "Wonder.Plugin.Counter.dll",
  "entryType": "Wonder.Plugin.Counter.CounterPlugin",
  "configurationSchema": {
    "version": 1,
    "fields": [
      { "key": "heading", "label": "标题", "type": "string", "default": "计数器", "maxLength": 40 }
    ]
  }
}
```

下面是完整入口和运行时。点击“增加”行后，所有指向同一应用的快捷方式同时刷新，关闭快捷方式后计数仍由宿主保存。

```csharp
using System.Globalization;
using System.Text.Json;
using Wonder.Extensibility.Contracts;

namespace Wonder.Plugin.Counter;

public sealed class CounterPlugin : IWonderPlugin
{
    public void Activate(IPluginContext context)
    {
        using var config = JsonDocument.Parse(context.ConfigurationJson);
        var heading = config.RootElement.TryGetProperty("heading", out var value)
            ? value.GetString() ?? "计数器" : "计数器";
        context.RegisterComponent(new(
            new("wonder.counter.main", "计数器", "点击增加计数", 220, 140)
            {
                Desktop = new() { PlaceOnFirstActivation = true }
            },
            new ComponentCard
            {
                Title = heading,
                Rows = [new("增加", "count") { ActionId = "increment" }]
            },
            state => new CounterRuntime(state)));
    }

    public void Dispose() { }
}

internal sealed class CounterRuntime(IComponentState state) : IComponentRuntime
{
    public event EventHandler? Changed;
    public IReadOnlyDictionary<string, string> Values => new Dictionary<string, string>
    {
        ["text"] = "所有快捷方式共享这份计数。",
        ["count"] = state.Get("count", "0"),
        ["footer"] = "关闭入口保留数据"
    };

    public void Resume() { }
    public void Suspend() { }
    public void Invoke(string actionId, string? value)
    {
        if (actionId != "increment") return;
        int.TryParse(state.Get("count", "0"), out var count);
        state.Set("count", (count + 1).ToString(CultureInfo.InvariantCulture));
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public void Dispose() { }
}
```

执行 `dotnet build -c Release`，把输出 DLL 和清单放入包根，再通过启动器“插件 → 导入 ZIP → 启用”试用。第一次启用需要用户阅读行为分析并同意；不要通过手改同意记录绕过这个流程。

## 2. 激活与组件生命周期

`Activate` 必须及时返回。在该方法的同一线程内同步完成 `RegisterComponent`，不能在后台任务或稍后的回调里注册。组件 ID 以插件 ID 加 `.` 开头，并且不与其他组件重复。整次激活成功后才公布注册；激活失败和停用会撤回注册。

每个应用由宿主持有一个 `IComponentRuntime`，多个快捷方式共享 `IComponentState`；位置属于每个快捷方式。宿主在 UI 线程调用运行时的 `Resume`、`Suspend`、`Invoke`、`Dispose`，回调不要执行耗时 IO。只有可见视图时才进行显示刷新；静态预览不能推进剧情。后台工作需要自行同步数据、提供稳定的 `Values` 快照，并在停用/Dispose 时停止定时器、取消工作和解绑事件。显示通知由宿主切回 UI 线程处理。

`IComponentState.Set` 适合小型共享状态；大型或独立存档写入 `context.DataDirectory`。不要依赖当前工作目录，不要覆盖玩家手改的未知字段。配置和游戏存档分开：宿主管理 `config.json`，插件自己管理例如 `state.json`。

## 3. 自动配置与可视化编辑

`configurationSchema.version` 为 1，`fields` 描述顶层属性，不解释嵌套路径。支持：

| type | 可选约束 | 示例默认值 |
| --- | --- | --- |
| `string` | `maxLength` | `"你好"` |
| `boolean` | 无 | `true` |
| `integer` | `minimum`、`maximum` | `3` |
| `number` | `minimum`、`maximum` | `0.5` |
| `choice` | 必填字符串数组 `options` | `"quiet"` |

每项需要唯一 `key`、`label`、`type`、`default`，可加 `description`。默认值也必须满足约束。声明字段后，发现或安装插件时自动生成缺失文件，升级新增字段补默认值；已有值和未知字段保留。不合规配置保留原文，先在 JSON 页修正，合规后可用表单编辑。没有 schema 的插件首次读取或启用时生成 `{}`。

保存后启用中的插件会重启，重新获得 `ConfigurationJson`；重启失败恢复旧配置。插件不要持有旧上下文继续运行。在线索引若带 schema，须与 ZIP 内清单相同。`permissions` 只解释用途，不授予或限制权限；代码插件仍以系统用户权限运行。

## 4. 卡片、滚动与动作

`ComponentCard` 绑定 `Values` 的字符串键：`TextKey` 为正文、`FooterKey` 为右对齐尾行，`Rows` 为读数/动作行。`ComponentCardStyle` 控制颜色、背景透明度、字号、内边距与圆角；颜色使用 `#RRGGBB`，尺寸使用 DIP。

`Scrollable=true` 给正文、行和尾行加原生垂直滚动，仅标题固定；刷新复用控件并保留滚动位置。此模式正文区用于浏览，拖动使用标题区，滚动条不会触发组件拖动。不要为了增加日志行数持续扩大卡片。

`ComponentCardRow` 支持 `Monospace`、`HighlightKey`（值为 `true` 时亮起）、`SecondaryValueKey`（次要值右对齐）、`ActionId`（调用运行时 Invoke）、`AccentColor`（行左侧强调线）。`RowLineHeight` 控制行高。`ComponentNarrationPresentation` 提供单行字幕条、状态点及透明度绑定；绑定透明度使用不随地区变化的 0–1 字符串。旁白逻辑和节奏属于插件，宿主只呈现数据。

## 5. 布局、网格与首次演出

普通内置组件尺寸按 20 DIP 网格统一。玩家开启吸附时，宿主按功能区原点和组件缩放对齐，**不显示网格线**。外部组件可自由定尺寸；`Desktop.SnapToGrid=false` 明确退出吸附。

| `ComponentDesktopOptions` | 作用 |
| --- | --- |
| `PlaceOnFirstActivation` | 第一次进入组件库时放置入口；已保存/主动删除的入口不自动重排或补回 |
| `FirstActivationOrder` | 同一批首次放置排序，宿主从右到左错开已有卡片，换行避让 |
| `StretchToAreaWidth` | 宽度随所属功能区变化，扣除两侧 `HorizontalMargin`；不是独占整个窗口的浮层 |
| `HorizontalMargin` | 响应宽组件的两侧 DIP 留白，默认 24 |
| `TopInset` | 首次摆放距功能区标题下方的自然尺寸间距，默认 16，随组件比例缩放 |
| `RevealDelayMilliseconds` | 首次淡入前等待时间 |
| `RevealDurationMilliseconds` | 首次淡入时长；0 表示立即显示 |
| `RevealOffsetY` | 首次出现的竖向浮入距离；动画不改变保存坐标 |
| `DragDelayMilliseconds` | 指针跟随延迟；0 沿用普通长按拖动 |
| `MaximumDragTiltDegrees` | 拖动最大倾斜角 |

《请稍候》用排序 0–4、延时 0/300/600/900/1200ms、180ms 淡入和 4px 浮入；重建布局、配置重启和重新启用不会重新排序玩家已放好的入口。带重量的卡片在功能区内按玩家实际看到的位置放下；拖回已打开的组件库可移除快捷方式，Esc 取消。移除只影响这一处入口，应用/存档仍保留。

内置组件在四边侧栏中会呈现 120×40 的页面入口按钮，不执行启动、切实例或编辑等直接功能；移动回普通功能区恢复完整卡片。该模式基于宿主内置注册元数据，不强制套用到外部插件。

## 6. 读取真实操作

调用 `context.GetObservation()` 获取线程安全快照；旧宿主或停用后可能返回 `null`，应显示“未观测”或降级，不能编造行为。`Elapsed` 是单调计时，适合停留、节流和演出；`TimestampUtc` 用于跨会话时间。系统改时不能推进实际停留时间。

指针包含所在 `Surface`、窗口内 X/Y、窗口宽高与 `StationaryDuration`。指针离开可观测窗口时为 null。`RecentClicks` 最多五条，新到旧，含 `Sequence`、`Target`、`AtElapsed` 和 `Button`（left/right/middle/x1/x2）。`SelectedInstance` 仅说明启动器选择，不说明游戏已启动。

`RecentActions` 最多 64 条，新到旧，每条为 `PluginInteractionObservation(Sequence, Kind, Target, Detail, AtElapsed)`。按序号去重；如需按发生顺序处理，对本次新增记录倒序遍历。快照是有界近期记录，不是无限完整日志。连续指针移动与原生 DragOver 只更新 `Pointer`，不写入动作历史；语义拖动的 `move` 事件约每 50ms 记录一次，开始/结束/取消独立记录。

| Kind | 含义 |
| --- | --- |
| `pointer.press` / `pointer.release` | 鼠标按下与释放；按键类型见 Detail |
| `pointer.wheel` | 滚轮行为 |
| `keyboard.shortcut` / `keyboard.key` / `text.input` | 快捷键、功能键及“发生输入”的摘要；不含输入正文 |
| `drag.enter` / `drag.drop` | 原生拖入与放下 |
| `window.open/close/state/move/resize` | 所属窗口打开、关闭、状态与位置/尺寸变化 |
| `instance.select` | 启动器内选择实例 |
| `component.drag.start/move/end/cancel` | 组件拖动阶段 |
| `area.drag.start/move/end/cancel` | 整个功能区拖动阶段 |
| `center.drag.*` / `center.reset` / `center.mode` | 工作区原点平移、复位与模式 |
| `area.resize.*` / `sidebar.resize.*` / `sidebar.mode` | 分隔线/侧栏调整及模式 |
| `component.add/remove/action` | 添加或移除快捷方式、执行组件动作 |
| `area.add/remove/rename/arrange` / `workspace.replace` / `context.open` | 功能区结构、整理、布局替换与菜单 |

范围是**本次启动器的主窗口、组件库、所属窗口与弹出层**，包括功能区之外的菜单和标题工具区域，不是其他程序或系统全局桌面。输入框只记录控件名/行为，不读取文本、密码或令牌；`component.action` 记录动作 ID，不记录参数正文。后续版本可能增加 Kind，遇到不认识的动作可以忽略。

## 7. 验证与发布

先在空的独立测试目录安装，验证启用/停用、配置重启、删除入口后不补回、多个入口共享状态、隐藏暂停、宽度缩放、拖放和 Esc。不要用真实 WonderData 当测试夹具。纯逻辑可用假 IPluginContext 与可控单调时间，涉及路由输入/滚动的部分使用桌面 Headless 测试，并查看实际渲染图。

仓库回归命令：`dotnet build Wonder.slnx -c Release`。插件包示例：`pwsh -File packaging/Wonder.PackagePleaseWait.ps1`。宿主发布：`pwsh -File packaging/Wonder.Publish.ps1 -Runtime win-x64`。

ZIP 中保留原始依赖许可、版权、项目许可和通知，不包含 WonderData、玩家实例、账号、缓存或调试探针。在线仓库的索引、行为报告和人工验证规则见 [插件管理](35-plugins.md)；生成本地包不等于已经投稿或获人工验证。

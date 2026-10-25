# 设置组件

设置功能始于 Wonder Launcher 0.2.3，现已接入 0.3.0 [通用组件系统](16-component-system.md)，仍复用原设置窗口和存储服务。目录和动效修订见[便携数据与交互动效](15-portable-data-and-motion.md)。

设置是第一个有实际功能的内置应用。可以从桌面设置快捷方式、组件库、顶栏 WONDER 菜单或工作区右键菜单打开。多个入口复用同一个非模态窗口；移除快捷方式、清空桌面和关闭窗口都不会删除设置。

0.5.0 将窗口划分为“启动器设置、游戏全局设置、个性化设置”，保留以下全部选项，并新增实际参与启动的全局偏好。外观与交互选项仍自动保存，游戏全局配置需点击保存；具体继承顺序和界面说明见[分类设置与实例卡片](21-settings-pages-and-instance-cards.md)。

## 已实现选项

| 选项 | 默认值 | 范围与效果 |
| --- | --- | --- |
| 主题 | 深色 | 跟随系统、深色、浅色；现有窗口和后续窗口统一更新 |
| 强调色 | 薄荷绿 | 薄荷绿、晴空蓝、琥珀金、蔷薇粉；组件与标准控件同步使用 |
| 工作区留白 | 22 DIP | 0–40 DIP，立即调整工作区四周边距 |
| 显示功能区序号 | 关闭 | 控制标题左侧的序号；隐藏后不留占位，新建和重建的功能区沿用设置 |
| 长按拖动时间 | 450 ms | 250–1000 ms，作用于组件与功能区长按；分隔边仍可直接拖动 |
| 侧边栏展开尺寸 | 320 DIP | 200–600 DIP，左右为宽度、上下为高度；空间不足时临时约束显示 |
| 侧边栏收起延迟 | 280 ms | 100–1000 ms；离开后等待，拖动期间继续保持展开 |

修改立即生效并自动保存，不需要重启。调整参数时会取消正在进行的工作区手势，避免按旧参数开始、按新参数提交。键盘可操作选项与滑块；窗口缩小时内容可滚动，保存状态和恢复入口固定在底部。

## 数据与恢复

- 设置文件：`<程序目录>/WonderData/settings.json`；布局使用同目录 `workspace.json`。启动时创建此目录及预留的 `plugins/`，目录创建失败会显示错误，不退回 AppData。
- 设置结构版本为 1，文件上限 64 KiB；字段、枚举、类型、数值范围均校验。缺失文件使用默认值，但打开窗口本身不会创建文件。
- `showAreaNumbers` 为可选布尔字段，已有 Wonder v1 设置未包含该字段时默认关闭；其余选项保持原值，不需要升级文件版本。
- 保存先写同目录临时文件，刷新到磁盘后原子替换，最后清理临时文件。
- 无法读取时保留原文件，用默认值运行，暂时禁用编辑。可在修复文件后点“重试”；也可点“恢复默认设置”并明确确认覆盖。
- 无法写入时保留当前会话效果，显示“尚未保存”。修复文件/目录权限后点“重试”写入当前值。确认覆盖损坏文件后遇到写入失败，也走该保存重试路径。
- 恢复默认设置会重置全部设置选项，不会清空功能区、删除快捷方式或重置布局。
- 不读取前身产品的数据、账号、缓存或插件配置。兼容清理仅针对本轮重建产生的 Wonder UI 预览：移除 kind 0/1/2 的演示应用与快捷方式，保留区域结构和空布局，补入 kind 3 的设置应用。未知种类仍拒绝读取，不静默丢弃。

## 代码入口

| 文件 | 职责 |
| --- | --- |
| `src/Wonder.Workspace/LauncherSettings.cs` | 不依赖 UI 的不可变配置和校验 |
| `src/Wonder.Workspace/LauncherSettingsStore.cs` | 严格读取、原子写入 |
| `src/Wonder.Desktop/SettingsWindow.cs` | 设置窗口、错误提示、默认值确认 |
| `src/Wonder.Desktop/MainWindowSettings.cs` | 窗口复用、应用配置、重试与错误状态 |
| `src/Wonder.Desktop/Palette.cs` | 可更新的共享画刷与明暗配色 |
| `src/Wonder.Desktop/WorkspaceView.cs` | 将配置应用到工作区手势 |

外观实现使用 Avalonia 的 `RequestedThemeVariant` 和 `ActualThemeVariantChanged`，跟随系统时设为 `ThemeVariant.Default`，参见[官方主题文档](https://docs.avaloniaui.net/docs/styling/theme-variants)。设置作为本体内置组件接入现有快捷方式结构，尚未实现第三方插件宿主或插件设置注册 API。

## 验证

```powershell
dotnet test Wonder.slnx -c Release
dotnet build Wonder.slnx -c Release
dotnet publish src/Wonder.Desktop/Wonder.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o artifacts/Wonder-win-x64
```

测试覆盖配置回读与非法值拒绝、原子保存失败、旧演示清理、设置入口复用、主题即时切换、滑块键盘操作、重新打开恢复、读写失败重试、确认恢复、真实指针长按与侧栏时序。截图使用 Avalonia Headless + Skia 渲染；原生系统主题事件、多屏 DPI 和 Windows OLE 拖放仍需实际桌面验收。

0.2 初版验证结果：Workspace **174/174**、Desktop **149/149**，合计 **323 项通过**，无失败或跳过。当前本地版本的验证、发布大小和 SHA-256 见 [本地整理与验证](23-local-workspace.md)。

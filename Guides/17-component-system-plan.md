# 通用组件系统实施计划

> 执行方式：当前聊天负责集成与验收，独立代理并行实现不重叠的模型与运行模块，最终独立审查。设计：[通用组件系统](16-component-system.md)。

**目标：** 新增组件通过统一注册、状态、动作与生命周期接入，交付设置、便笺与启动器状态。

**技术：** .NET 10、Avalonia 12.1.1、xUnit、Avalonia Headless；沿用现有依赖。

## 约束与审查重点

- 使用 `Wonder.*`；不引入旧启动器运行依赖。
- 明确区分应用数据与快捷方式布局，清空桌面不清除数据。
- v1 坐标升级、v2 数字标识、未知新定义分别验证。
- 重建布局、搜索和拖动预览不得意外产生后台活动。
- 保存错误、运行错误和错误重试分别处理，不静默覆盖文件。
- 输入控件的操作不能被父工作区接管。
- 主窗口最小化、组件库关闭及侧栏隐藏必须影响可见性生命周期。

## 1. 接续基线

- [x] 读取两个引用聊天并核对最新改进要求。
- [x] 识别当前检出与上轮源码不一致；保留原工作区。
- [x] 将当前旧检出移至工作区外备份，复制上轮实际源码，逐项核对 168 个文件 SHA-256。
- [x] 验证原有完整测试，建立可复核基线：180 项 Workspace + 224 项 Desktop，共 404 项通过。

## 2. 通用文档与状态

文件：`WorkspaceDocument.cs`、`WorkspaceStore.cs`、`ComponentIds.cs` 及 Workspace 测试。

接口：`ComponentItem.DefinitionId`、应用级 `State`、`CreateApplication`、`CreateShortcut`、`EnsureLibrary(IEnumerable<string>?)`。

- [x] 先写并运行新标识、多应用、未知定义与迁移测试。
- [x] 用稳定标识替换枚举驱动；保留引用校验与文件限制。
- [x] 实现 v3 写入与旧 Wonder 预览读取。
- [x] 运行 `dotnet test tests/Wonder.Workspace.Tests -c Release`。

## 3. 注册、运行与展示

文件：`Components/`、`ComponentView.cs`、通用契约工程及新增 Desktop 测试。

接口：注册表提供描述；宿主按应用 ID 管理运行实例；视图使用 `(ComponentItem, ComponentHost, preview)`，保留命中、尺寸及选择接口。

- [x] 先写状态同步、生命周期、失败与重试测试。
- [x] 迁入设置，接入便笺及启动器状态。
- [x] 保留设置多边形与点击波纹；允许不同组件自然尺寸。
- [x] 验证预览无副作用，输入可编辑，失效组件局部降级。

## 4. 工作区与组件库集成

文件：`WorkspaceView.cs`、`AreaView.cs`、`ComponentLibraryView.cs`、`MainWindow.cs` 及现有 Desktop 测试。

- [x] 消除组件库目录和工作区中的设置专用分支。
- [x] 使用定义尺寸定位，避免为测量创建运行实例。
- [x] 路由可见性及销毁；输入区域不触发拖动。
- [x] 保持清空/重置布局的应用数据与引用身份。
- [x] 更新旧测试的标识与三组件目录断言，保留原交互验收。

## 5. 验收与交付

- [x] 独立审查并修复确认的问题。
- [x] `dotnet test Wonder.slnx -c Release` 全部通过。
- [x] Release 构建与 Windows x64 自包含单文件发布。
- [x] 渲染查看三组件和便笺编辑场景。
- [x] 更新 `Wonder.md`、`Guides/README.md`，记录真实验证结果与限制。

## 验收记录 · 2026-10-22

- 最终完整回归：Workspace **208/208**，Desktop **254/254**，共 **462/462**；相较接续基线增加 58 项。
- Release 构建：0 警告、0 错误。Windows x64 自包含单文件 `artifacts/Wonder-win-x64/Wonder.Desktop.exe`，版本 0.3.0，101,690,406 字节。
- 发布程序在独立临时目录成功创建主窗口并正常关闭，退出码 0；上一版 Wonder 布局副本读入后写为 v3，组件库包含三种应用。
- 新程序旁保留上一版 Wonder 的 `settings.json` 和 `workspace.json`；复制前后与原目录 SHA-256 一致。启动检查使用独立副本，没有改动这些交付数据。
- 已查看 Headless 组件库与双便笺截图：`artifacts/components-0.3.0/library.png`、`notes-workspace.png`。原生多屏、不同 DPI 的人工交互验收尚未进行。
- 审查修复覆盖独立应用身份、可见性暂停、末次输入保存、故障展示清理、事件取消订阅、观察者隔离及拖动/选择回调异常；拖动预览故障不创建运行实例。
- 一次全套执行中，既有 `Animated_sidebar_resize_then_escape_restores_hover_extent_and_keeps_document` 动画时序断言失败；单独复跑与最终完整回归均通过。该用例仍依赖实际动画时钟，保留为后续测试稳定性改进项。

构建日志、最终 TRX、产物 SHA-256、数据保留记录和启动结果见 `artifacts/components-0.3.0/verification.json` 及相邻日志。实现保留在当前工作树，未提交或推送远端。

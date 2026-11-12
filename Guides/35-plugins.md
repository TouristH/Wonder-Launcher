# 插件管理

从组件库点击“插件”，或将它拖到桌面后打开。窗口包含已安装插件与在线仓库。

## 本地插件

“打开插件目录”打开当前便携目录下的 `WonderData/plugins/`。每个插件放在自己的目录中，根部包含 `plugin.json`、入口 DLL 与依赖。“导入 ZIP”可安装符合 Wonder 格式的本地包，导入后显示在已安装列表。

安装后点击“启用”，首次使用时会显示插件名称、版本、启动器自动分析的行为提示，以及可确认的人工验证信息。分析在执行插件代码前读取插件及附带 DLL 的元数据，提示检测到的文件访问、联网、启动程序、剪贴板等调用；作者说明只作补充。阅读提示后选择“同意并启用”才会加载插件代码；不同意或关闭提示窗口，插件保持关闭。已验证插件也需要同意。

这是一项整体知情同意，不提供逐项权限开关或安全沙箱。插件以当前系统用户权限运行，能够访问文件、联网和启动程序。静态分析是对代码中 API 引用的提示，不能保证这些行为一定发生，也不能完整识别反射、动态下载代码或本地库的行为。分析限制会在窗口中显示；未检测到某类调用不代表插件没有该能力，作者说明与人工验证同样不保证安全。

点击“关闭”释放插件实例。已经同意且文件未变化的插件，在下次启动时恢复此前的启用状态；更新、修改插件文件或权限说明后，需要再次同意。普通配置修改不重复要求确认。此前启用但没有同意记录的插件，也需要先确认。

“修改配置”提供 JSON 对象编辑器。保存有效配置后，已启用插件重新激活并读取配置。每个插件的配置位于 `WonderData/plugin-data/<id>/config.json`，更新安装包会保留此目录。插件消息与加载错误显示在管理窗口中。

手动修改插件文件后使用刷新重新读取。损坏的清单或不兼容的 API 会显示错误。若 `plugin-settings.json` 读取失败，先备份并修复该文件，再刷新以恢复启停操作。

## 在线仓库

仓库迁移期间，默认源暂时使用 Wonder Plugins 已有的开发预览索引 `dev/public/wonder/v1/index.json`，可在窗口中输入其他同格式 HTTPS 索引地址。正式仓库重写与入口切换尚未完成。点击刷新获取列表，再选择下载安装。安装器验证下载大小和 SHA-256，并检查 ZIP 的解压路径。安装成功后选择启用插件。

当前默认索引地址为 `https://raw.githubusercontent.com/TouristH/Wonder-Plugins/dev/public/wonder/v1/index.json`。仓库迁移进展见 [Wonder Plugins](https://github.com/TouristH/Wonder-Plugins)。网络请求由刷新与安装操作触发，关闭管理窗口会取消该窗口正在等待的网络操作。

同一插件按版本展示。人工验证通过的版本标绿，未验证或验证已撤销的版本标红，并显示对应文字；所有已收录版本都可以下载。验证记录绑定具体版本和安装包 SHA-256，新版本不会继承旧版本的验证状态。本地插件若无法与已获取的验证记录核对，则显示无法确认其验证状态。

## 编写插件

目标运行时为 .NET 10。入口类型实现 `Wonder.Extensibility.Contracts.IWonderPlugin`，提供 `Activate(IPluginContext context)` 和 `Dispose()`。上下文提供 `ConfigurationJson`、`DataDirectory` 与 `Log(string)`，接口随实际功能需求扩展。入口操作应及时返回，插件自行在 Dispose 中结束其活动。

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

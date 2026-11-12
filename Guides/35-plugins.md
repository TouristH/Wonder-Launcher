# 插件管理

从组件库点击“插件”，或将它拖到桌面后打开。窗口包含已安装插件与在线仓库。

## 本地插件

“打开插件目录”打开当前便携目录下的 `WonderData/plugins/`。每个插件放在自己的目录中，根部包含 `plugin.json`、入口 DLL 与依赖。“导入 ZIP”可安装符合 Wonder 格式的本地包，导入后显示在已安装列表。

安装后点击“启用”立即调用插件入口。点击“关闭”释放插件实例。启动器下次打开时恢复此前的启用状态。代码插件拥有当前系统用户权限，启用前请核对作者和来源。

“修改配置”提供 JSON 对象编辑器。保存有效配置后，已启用插件重新激活并读取配置。每个插件的配置位于 `WonderData/plugin-data/<id>/config.json`，更新安装包会保留此目录。插件消息与加载错误显示在管理窗口中。

手动修改插件文件后使用刷新重新读取。损坏的清单或不兼容的 API 会显示错误。若 `plugin-settings.json` 读取失败，先备份并修复该文件，再刷新以恢复启停操作。

## 在线仓库

默认源为 Wonder Plugins 仓库的 `dev/public/wonder/v1/index.json`，可在窗口中输入其他同格式 HTTPS 索引地址。点击刷新获取列表，再选择下载安装。安装器验证下载大小和 SHA-256，并检查 ZIP 的解压路径。安装成功后选择启用插件。

默认索引地址为 `https://raw.githubusercontent.com/TouristH/UNuse/dev/public/wonder/v1/index.json`。仓库源码、示例与投稿方式见 [Wonder Plugins](https://github.com/TouristH/UNuse/tree/dev)。网络请求由刷新与安装操作触发，关闭管理窗口会取消该窗口正在等待的网络操作。

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
  "entryType": "Wonder.Example.Hello.HelloPlugin"
}
```

在线索引包含 `schemaVersion: 1`、`product: "wonder"` 和 `plugins` 数组。每个条目包含 id、name、description、author、version、apiVersion，以及 downloadUrl、sha256、size、sourceUrl。下载 URL 必须指向实际的 HTTPS ZIP，大小与校验值对应其完整字节。仓库提供构建与校验工具。

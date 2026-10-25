# 参与 Wonder Launcher

欢迎提交问题、文档改进、测试与代码贡献。普通问题可使用 [Issues](https://github.com/TouristH/Wonder-Launcher/issues)，涉及凭据或个人数据的安全问题请发至 [radiafidw@gmail.com](mailto:radiafidw@gmail.com)，不要直接公开敏感信息。

## 贡献许可与来源

项目原创代码和原创文档使用 GPL-3.0-only 及 [GPLv3 第 7 条附加条款](ADDITIONAL_TERMS.md)。你有意提交并请求纳入本项目的贡献，应当是你有权按相同条款提供的内容；如另有约定，应在合并前明确书面说明。

你保留贡献的版权，不需要转让给维护者。不要把别人的 GPL 不兼容代码、未授权图片、游戏文件、账号数据、私有配置或机密提交进仓库。引用第三方材料时，标注来源并保留其许可、版权和必要通知；原依赖的许可不能被项目许可覆盖。

维护者不会把提交贡献视为授予项目整体商业例外或任意重新许可权。GPL 本身已允许符合条件的商业使用；第三方品牌授权与代码许可分开处理。

## 开发与验证

使用 .NET 10 SDK，在当前源码目录执行：

```powershell
dotnet build Wonder.slnx -c Release
dotnet test Wonder.slnx -c Release
```

对行为修改补充有意义的回归测试；文档或许可变更核对链接、声明及第三方边界即可。提交中不包含 `bin/`、`obj/`、`artifacts/` 或 `WonderData/`，不要为验证拷入真实游戏和账号数据。使用方式改变时同步更新 README、Wonder.md 和对应 Guides。

Fork 和修改版发布须按 GPL 提供相应源码、标明修改，并遵守[品牌政策](BRANDING.md)。提交上游是自愿的，维护者可按项目目标审查是否合并。

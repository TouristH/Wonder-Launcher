# 第三方依赖与来源声明

本项目的 GPLv3 及适用附加条款仅适用于有权按该许可授权的项目内容，**不改变第三方组件的独立许可**。不能把下列 MIT、BSD、Apache 等许可的依赖、原始通知或第三方商标统一改标为本项目版权或品牌限制。组成受 GPL 覆盖的发行物时仍须满足各组件与 GPL 的适用要求。

源码通过 NuGet 引用依赖，没有在本仓库提交依赖二进制、Minecraft 文件或用户数据。以下记录基于 0.7.0 当前依赖元数据及运行时可选下载，升级包或发布自包含程序时须重新核对实际分发内容。

## 运行与构建依赖

| 组件 | 版本 | 包内许可声明/说明 |
| --- | --- | --- |
| [Avalonia](https://github.com/AvaloniaUI/Avalonia) Desktop、Fluent 及相关组件 | 12.1.1 | MIT；BuildServices 为 11.3.2 |
| [SkiaSharp](https://github.com/mono/SkiaSharp) 与 NativeAssets | 3.119.4 | MIT；原生实现另含多项第三方许可和声明 |
| [HarfBuzzSharp](https://github.com/mono/SkiaSharp) 与 NativeAssets | 8.3.1.3 | MIT；原生实现另含多项第三方许可和声明 |
| Avalonia.Angle.Windows.Natives | 2.1.27548.20260419 | 包内 LICENSE 为 BSD 3-Clause；保留其原文 |
| [MicroCom](https://github.com/AvaloniaUI/MicroCom) Runtime | 0.11.6 | MIT |
| [Tmds.DBus.Protocol](https://github.com/tmds/Tmds.DBus) | 0.94.1 | MIT |
| [.NET runtime](https://github.com/dotnet/runtime) | 当前本地自包含发布使用 10.0.13 | MIT 及其第三方声明；系统安装的 SDK 不属于本项目原创代码 |

## 第三方认证运行时组件

自定义 Yggdrasil 游戏启动按需从官方端点下载 [authlib-injector 1.2.8](https://github.com/yushijinhun/authlib-injector/tree/v1.2.8)（build 56），不会把它加入微软正版启动，也不在源码仓库或本次 EXE 中打包其 JAR。版权及作者信息属于上游权利人。

该组件采用 **AGPL-3.0-or-later，附带 AUTHLIB-INJECTOR 例外**，不是 Wonder 的 GPL-3.0-only 加附加条款。[上游许可说明](https://github.com/yushijinhun/authlib-injector/blob/v1.2.8/COPYING.md)和[完整原文及例外](ThirdPartyLicenses/authlib-injector/1.2.8/AGPLv3-with-authlib-injector-exception.txt)均保留。例外覆盖原样二进制打包、通过其进程间接口交互和 Java Agent 加载的相应行为；若另行修改或重新分发该组件，仍须满足其原始许可要求。固定下载来源、校验值及许可原文来源见[组件来源记录](ThirdPartyLicenses/authlib-injector/1.2.8/SOURCE.md)。

账户管理功能组织参考 HMCL，Minecraft 认证和外观接口互操作参考 Prism Launcher 的公开实现；Wonder 的相关代码独立编写，没有复制这些项目的代码、图标或默认皮肤资源。链接见[账户中心说明](Guides/26-account-management.md#架构与协议依据)。

## 本地验证使用的依赖

以下依赖用于维护者本地测试，不属于公开源码树中的测试工程或发布 EXE 的运行依赖。保留其既有元数据和原始通知以维持来源记录，不随本次公开范围整理删除或改写第三方原文。

- Microsoft.NET.Test.Sdk 17.12.0 与相关测试平台包：按包内 MIT 及第三方声明授权。
- Avalonia.Headless.XUnit 12.1.1：MIT。
- xUnit 2.9.2、xUnit v3 3.2.2、runner 与 analyzer：当前包元数据声明 Apache-2.0；旧 xunit.abstractions 2.0.3 仅提供上游许可地址，不能把整个测试依赖集合统一写为 MIT。
- Newtonsoft.Json 13.0.1 及其他传递依赖：按各包许可声明授权。完整包 ID、版本、作者、版权和许可链接见 [DEPENDENCIES.json](ThirdPartyLicenses/DEPENDENCIES.json)。
- 测试依赖可能携带字体或跨平台原生资源；包级许可摘要不扩大或替代具体资产的独立许可。本仓库不单独复制这些字体或原生资源。

## 原始许可与声明

三端同源发行另附 macOS x64/arm64 `.NET runtime 10.0.13`、`SkiaSharp.NativeAssets.macOS 3.119.4`、`HarfBuzzSharp.NativeAssets.macOS 8.3.1.3` 的包内原始许可与第三方通知。源码组织方式参考 HMCL 的平台检测/分发思路，没有复制其实现；Wonder 使用自己的 .NET/Avalonia 工程与平台接口。

Linux x64 0.7.2 试发行包另附 `.NET runtime linux-x64 10.0.13`、`SkiaSharp.NativeAssets.Linux 3.119.4` 与 `HarfBuzzSharp.NativeAssets.Linux 8.3.1.3` 的包内原始许可和第三方通知，保存在 `ThirdPartyLicenses/` 的同名目录中。系统提供的 X11、字体、GIO 与 Secret Service 工具需由用户按发行版安装，不打包进应用。

本仓库在 [ThirdPartyLicenses/](ThirdPartyLicenses/) 保留从当前 NuGet 包中复制的 SkiaSharp、HarfBuzzSharp、ANGLE、.NET runtime、Newtonsoft.Json 和 Microsoft 测试平台许可/第三方通知原文。对应版权归各原文中的权利人，不能删除、改写或覆盖为本项目版权。上游没有随包提供完整许可文件的依赖，记录其元数据中的许可表达式或许可链接。

这些通知不是所有未来二进制包的永久许可清单。发布程序时需将完整通知随实际依赖一起分发；无需把 NuGet 缓存或整个运行时源码提交进本仓库。

## 兼容格式参考

实例适配器的格式参考包括 HMCL、PCL、Prism Launcher 和 MultiMC。现有实施记录声明适配器按公开结构编写，未将这些启动器的实现纳入新工程；具体文件和上游链接保留在[格式参考与代码来源](Guides/18-instance-management-and-launch.md#格式参考与代码来源)。这些引用不改变上游项目的 GPL 或其他许可，也不赋予本项目重新授权其代码的权利。

## 游戏与商标

Minecraft、Mojang、Microsoft、Xbox、Java 及其他名称属于各自权利人。游戏客户端、资产、模组、存档、账号凭据和第三方启动器均不因本项目源码可见而取得任何额外授权。

NOT AN OFFICIAL MINECRAFT PRODUCT. NOT APPROVED BY OR ASSOCIATED WITH MOJANG OR MICROSOFT.

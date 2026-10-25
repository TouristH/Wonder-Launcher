# 隐私说明 / Privacy statement

适用版本：Wonder Launcher 0.5.1。更新日期：2026-10-25。维护者：[TouristH](https://github.com/TouristH)，联系邮箱：[radiafidw@gmail.com](mailto:radiafidw@gmail.com)。

## 当前实现

Wonder 将设置与工作区保存在 EXE 所在目录的 `WonderData/`。当前没有项目自建云服务、账号同步或遥测上传功能，也没有实现微软账号登录。不要把“尚无账号功能”理解为已经完成账号安全实现。

| 本地数据 | 用途 |
| --- | --- |
| `settings.json` | 主题、个性化和交互参数 |
| `workspace.json` | 功能区、组件快捷方式、便笺等应用状态 |
| `instances.json` | 用户主动登记的游戏路径、启动设置、所选实例及离线用户名 |
| 缓存与运行日志 | 本地启动所需临时内容与诊断；日志可能含用户名、路径和游戏输出 |

用户主动选择游戏目录后，程序读取与实例发现和启动相关的版本元数据、库和已有启动设置。官方 profile 的账号字段不作为登录凭据导入；其他启动器的账号令牌也不在适配范围内。程序不递归扫描整个磁盘寻找个人信息。

实例/目录删除须输入 `sure`，使用系统回收站；目录设置可执行实际同盘移动或改名。操作前应核对界面显示的目标范围，具体行为见[实例管理说明](Guides/22-instance-selection-and-directory-actions.md)。

启动后的 Java 游戏进程、用户安装的模组和所连接的服务器可能独立访问网络、处理账号或产生游戏日志；这些行为不由本声明代表。程序默认不上传本地设置或日志。向 Issues 或邮箱发送诊断信息属于用户主动分享，应先移除令牌、私人路径、存档与个人信息。

## 未来账号功能

微软登录、刷新令牌保存、Minecraft Services 访问和所有权校验仍待实现。启用前须更新本声明，明确实际权限、保存方式、退出登录/删除凭据、撤销授权及日志脱敏行为；不会提前声称已实现加密存储或安全认证。

## 数据移除

关闭 Wonder 后，可自行备份并删除程序旁的 `WonderData/` 以移除其本地设置和登记。该操作不等于删除外部游戏目录，也不会代替在游戏服务中管理账号。删除 `WonderData/` 会丢失便笺和布局，请先备份。

## English summary

Version 0.5.1 stores settings, workspace/notes, local instance registrations, and the offline username in `WonderData/` beside the executable. It has no implemented Microsoft sign-in, token-import feature, project telemetry, or project-operated cloud account service. Local diagnostic output can contain paths, usernames, and game logs. Game processes, mods, and servers have their own behavior. Future authentication and credential storage must be documented before release; they are not represented as implemented here.

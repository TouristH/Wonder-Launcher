// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using Wonder.Extensibility.Contracts;

namespace Wonder.Plugin.PleaseWait;

internal static class PleaseWaitActions
{
    internal static string Button(string button) => button switch
    {
        "right" => "右键", "middle" => "中键", "x1" or "xbutton1" => "侧键 1", "x2" or "xbutton2" => "侧键 2", _ => "左键"
    };

    internal static string Label(PluginInteractionObservation action) => action.Kind switch
    {
        "pointer.press" => Button(action.Detail ?? "left") + "按下",
        "pointer.release" => Button(action.Detail ?? "left") + "松开",
        "pointer.wheel" => "滚动", "keyboard.shortcut" => "键盘快捷键", "keyboard.key" => "按键",
        "text.input" => "输入文字", "drag.enter" => "拖入", "drag.drop" => "投放",
        "window.open" => "打开窗口", "window.close" => "关闭窗口", "window.state" => "窗口状态",
        "window.move" => "移动窗口", "window.resize" => "调整窗口大小",
        "component.add" => "添加组件", "component.remove" => "移除组件", "component.action" => "组件操作",
        "instance.select" => "选择实例",
        "area.add" => "添加功能区", "area.remove" => "删除功能区", "area.rename" => "重命名功能区",
        "area.arrange" => "整理组件", "center.reset" => "重置原点", "sidebar.mode" => "切换侧边栏",
        "workspace.replace" => "替换布局", "context.open" => "打开右键菜单",
        _ => GestureLabel(action.Kind)
    };

    private static string GestureLabel(string kind)
    {
        var prefix = kind.StartsWith("component.drag.", StringComparison.Ordinal) ? "组件拖动" :
            kind.StartsWith("area.drag.", StringComparison.Ordinal) ? "功能区拖动" :
            kind.StartsWith("center.drag.", StringComparison.Ordinal) ? "原点拖动" :
            kind.StartsWith("area.resize.", StringComparison.Ordinal) ? "功能区尺寸调整" :
            kind.StartsWith("sidebar.resize.", StringComparison.Ordinal) ? "侧边栏尺寸调整" : "操作";
        return prefix + (kind.Split('.').Last() switch
        {
            "start" => "开始", "move" => "进行中", "end" => "结束", "cancel" => "取消", _ => ""
        });
    }

    internal static string Readout(PluginInteractionObservation action)
    {
        var detail = action.Kind == "instance.select" || action.Kind.StartsWith("pointer.", StringComparison.Ordinal) && action.Kind != "pointer.wheel"
            ? "" : string.IsNullOrEmpty(action.Detail) ? "" : " · " + action.Detail;
        return $"{action.AtElapsed:mm\\:ss} {Label(action)} · {action.Target}{detail}";
    }

    internal static bool IsNarratable(PluginInteractionObservation action) =>
        action.Kind is not ("pointer.release" or "drag.enter" or "window.move" or "window.resize") &&
        !action.Kind.EndsWith(".move", StringComparison.Ordinal);

    internal static string Sentence(PluginInteractionObservation action) => action.Kind switch
    {
        "pointer.press" => $"玩家用{Button(action.Detail ?? "left")}点了「{action.Target}」。",
        "pointer.wheel" => $"玩家在「{action.Target}」滚动了内容。",
        "keyboard.shortcut" => $"玩家在「{action.Target}」用了快捷键 {action.Detail}。",
        "keyboard.key" => $"玩家在「{action.Target}」按了 {action.Detail}。",
        "text.input" => $"玩家在「{action.Target}」输入了文字。",
        "window.open" => $"玩家打开了「{action.Target}」。",
        "window.close" => $"玩家关闭了「{action.Target}」。",
        "instance.select" => action.Detail is null ? "玩家取消了实例选择。" : $"玩家选中了实例「{action.Target}」。",
        "context.open" => $"玩家打开了「{action.Target}」的右键菜单。",
        _ => $"记录：{Label(action)} · {action.Target}。"
    };
}

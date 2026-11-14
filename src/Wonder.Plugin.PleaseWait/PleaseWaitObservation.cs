// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Globalization;
using Wonder.Extensibility.Contracts;

namespace Wonder.Plugin.PleaseWait;

internal static class PleaseWaitObservation
{
    internal static readonly IReadOnlyList<ComponentCardRow> Rows = Array.AsReadOnly(new ComponentCardRow[]
    {
        new("光标", "cursor") { Monospace = true },
        new("停", "dwell") { Monospace = true },
        new("选择：", "instance"),
        new("距上次", "absence"),
        new("档", "tier") { Monospace = true },
        new("时钟", "clock"),
        new("点了：", "click-0") { HighlightKey = "click-0-bright" },
        new("点了：", "click-1") { HighlightKey = "click-1-bright" },
        new("点了：", "click-2") { HighlightKey = "click-2-bright" },
        new("点了：", "click-3") { HighlightKey = "click-3-bright" },
        new("点了：", "click-4") { HighlightKey = "click-4-bright" }
    }.Concat(Enumerable.Range(0, 64).Select(index => new ComponentCardRow("行为", "action-" + index)
    { HighlightKey = "action-" + index + "-bright" })).ToArray());

    internal static Dictionary<string, string> Values(PluginObservationSnapshot? snapshot, string text, string footer,
        TimeSpan? absence, bool clockAdjusted)
    {
        var pointer = snapshot?.Pointer;
        var stationary = pointer is null || pointer.StationaryDuration < TimeSpan.FromSeconds(.5)
            ? 0 : pointer.StationaryDuration.TotalSeconds;
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["text"] = text,
            ["footer"] = footer,
            ["cursor"] = pointer is null ? "未观测" : FormattableString.Invariant($"{pointer.X:0}, {pointer.Y:0}"),
            ["dwell"] = stationary.ToString("0.0", CultureInfo.InvariantCulture) + " 秒",
            ["instance"] = snapshot?.SelectedInstance?.Name ?? "未选择",
            ["absence"] = AbsenceLabel(absence),
            ["tier"] = AbsenceTier(absence).ToString(CultureInfo.InvariantCulture),
            ["clock"] = clockAdjusted ? "已调整" : "正常"
        };
        for (var index = 0; index < 5; index++)
        {
            var click = snapshot is not null && index < snapshot.RecentClicks.Count ? snapshot.RecentClicks[index] : null;
            var age = click is null ? TimeSpan.MaxValue : snapshot!.Elapsed - click.AtElapsed;
            values["click-" + index] = click is null ? "" : (click.Button == "left" ? "" : PleaseWaitActions.Button(click.Button) + " · ") + click.Target;
            values["click-" + index + "-bright"] = age >= TimeSpan.Zero && age < TimeSpan.FromMilliseconds(600) ? "true" : "false";
        }
        for (var index = 0; index < 64; index++)
        {
            var action = snapshot is not null && index < snapshot.RecentActions.Count ? snapshot.RecentActions[index] : null;
            var age = action is null ? TimeSpan.MaxValue : snapshot!.Elapsed - action.AtElapsed;
            values["action-" + index] = action is null ? "" : PleaseWaitActions.Readout(action);
            values["action-" + index + "-bright"] = age >= TimeSpan.Zero && age < TimeSpan.FromMilliseconds(600) ? "true" : "false";
        }
        return values;
    }

    private static string AbsenceLabel(TimeSpan? duration)
    {
        if (duration is not { } value) return "首次";
        if (value < TimeSpan.FromHours(2)) return "2 小时内";
        if (value > TimeSpan.FromDays(30)) return (int)(value.TotalDays / 30) + " 个月";
        if (value >= TimeSpan.FromDays(1)) return (int)value.TotalDays + " 天";
        return (int)value.TotalHours + " 小时";
    }

    private static int AbsenceTier(TimeSpan? duration) => duration switch
    {
        null => 1,
        { TotalHours: < 2 } => 1,
        { TotalHours: < 24 } => 2,
        { TotalDays: < 7 } => 3,
        { TotalDays: <= 30 } => 4,
        _ => 5
    };
}

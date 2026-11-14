// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Text.Json;

namespace Wonder.Plugin.PleaseWait;

internal sealed record PleaseWaitConfiguration(bool ShowObservations, double DragDelayMilliseconds, double MaximumDragTiltDegrees)
{
    internal static PleaseWaitConfiguration Read(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowDuplicateProperties = false });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new JsonException("根节点必须是 JSON 对象。");
            var show = !root.TryGetProperty("showObservations", out var value) ? true : value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => throw new JsonException("showObservations 必须是布尔值。")
            };
            return new(show, Number(root, "dragDelayMilliseconds", 60, 200), Number(root, "maximumDragTiltDegrees", 6, 6));
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("请稍候的 config.json 无效：" + error.Message, error);
        }
    }

    private static double Number(JsonElement root, string key, double fallback, double maximum)
    {
        if (!root.TryGetProperty(key, out var value)) return fallback;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) &&
            double.IsFinite(number) && number >= 0 && number <= maximum) return number;
        throw new JsonException($"{key} 必须是 0–{maximum} 范围内的数字。");
    }
}

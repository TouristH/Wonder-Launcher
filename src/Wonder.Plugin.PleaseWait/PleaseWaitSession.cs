// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Collections.ObjectModel;
using System.Globalization;
using Wonder.Extensibility.Contracts;

namespace Wonder.Plugin.PleaseWait;

/// <summary>One activation's text and previous display, shared by all its component runtimes.</summary>
internal sealed class PleaseWaitSession : IDisposable
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static readonly JsonSerializerOptions CompactJson = new(Json) { WriteIndented = false };
    private readonly object _gate = new();
    private readonly string _path;
    private readonly Func<PluginObservationSnapshot?>? _observe;
    private readonly Action<string>? _log;
    private readonly PluginObservationSnapshot? _start;
    private readonly TimeSpan? _absence;
    private PluginObservationSnapshot? _lastObservation;
    private IReadOnlyDictionary<string, string> _values;
    private Timer? _timer;
    private int _views;
    private int _narrationViews;
    private readonly PleaseWaitNarration _narration = new();
    private bool _clockAdjusted;
    private bool _observationFailed;
    private bool _recorded;
    private bool _disposed;

    private PleaseWaitSession(string path, JsonObject state, Func<PluginObservationSnapshot?>? observe, Action<string>? log)
    {
        _path = path;
        _observe = observe;
        _log = log;
        Text = ReadString(state, "text") ?? "测试";
        var previous = ReadString(state, "lastDisplayedText");
        Footer = previous is null ? "" : "上次：" + previous;
        _start = _lastObservation = observe?.Invoke();
        if (_start is not null && ReadDate(state, "lastSeenUtc") is { } lastSeen)
        {
            var absence = _start.TimestampUtc - lastSeen;
            _clockAdjusted = absence < TimeSpan.Zero;
            _absence = absence < TimeSpan.Zero ? TimeSpan.Zero : absence;
        }
        _values = ReadOnly(PleaseWaitObservation.Values(_start, Text, Footer, _absence, _clockAdjusted));
    }

    public string Text { get; }
    public string Footer { get; }
    public bool HasObservations => _start is not null;
    public IReadOnlyDictionary<string, string> Values => Volatile.Read(ref _values);
    public event EventHandler? Changed;

    public static PleaseWaitSession Open(string directory, Func<PluginObservationSnapshot?>? observe = null, Action<string>? log = null)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "state.json");
        var exists = File.Exists(path);
        var state = exists
            ? ReadState(path)
            : new JsonObject { ["step"] = 1, ["text"] = "测试" };
        var session = new PleaseWaitSession(path, state, observe, log);
        if (!exists) session.WriteState(state);
        return session;
    }

    public void RecordFirstDisplay()
    {
        lock (_gate)
        {
            if (_recorded || _disposed) return;
            // The card may remain hidden after activation. Merge into the file as it is now,
            // while retaining this activation's displayed text and previous-display snapshot.
            var state = ReadState(_path);
            state["lastDisplayedText"] = Text;
            WriteState(state);
            _recorded = true;
        }
    }

    public void AttachView(bool narration = false)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _views++;
            if (narration) _narrationViews++;
            if (HasObservations && _views == 1)
                _timer = new Timer(_ => RefreshObservation(), null, 50, 50);
        }
        RefreshObservation();
    }

    public void DetachView(bool narration = false)
    {
        lock (_gate)
        {
            if (_views > 0) _views--;
            if (narration && _narrationViews > 0) _narrationViews--;
            if (_views != 0) return;
            _timer?.Dispose();
            _timer = null;
        }
    }

    private void RefreshObservation()
    {
        EventHandler? changed;
        try
        {
            lock (_gate)
            {
                if (_disposed || !HasObservations || _views == 0) return;
                var observation = _observe?.Invoke();
                if (observation is not null) RememberClock(observation);
                var values = PleaseWaitObservation.Values(observation, Text, Footer, _absence, _clockAdjusted);
                if (_narrationViews > 0)
                    foreach (var pair in _narration.Update(observation)) values[pair.Key] = pair.Value;
                if (_values.Count == values.Count && values.All(pair => _values.TryGetValue(pair.Key, out var value) && value == pair.Value)) return;
                Volatile.Write(ref _values, ReadOnly(values));
                changed = Changed;
            }
            // Listeners may suspend a view. Never invoke them while owning the session lock.
            changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception error)
        {
            // A timer exception must not terminate the host. State files are only written by
            // explicit lifecycle calls, where the host can report any file error to the player.
            lock (_gate)
            {
                if (_disposed || _observationFailed) return;
                _observationFailed = true;
            }
            try { _log?.Invoke("请稍候读取观察数据失败：" + error.Message); }
            catch { /* Logging must not escape the timer callback either. */ }
        }
    }

    private static IReadOnlyDictionary<string, string> ReadOnly(Dictionary<string, string> values) =>
        new ReadOnlyDictionary<string, string>(values);

    private void RememberClock(PluginObservationSnapshot observation)
    {
        if (_start is null) return;
        var elapsed = observation.Elapsed - _start.Elapsed;
        var wallTime = observation.TimestampUtc - _start.TimestampUtc;
        if ((wallTime - elapsed).Duration() >= TimeSpan.FromSeconds(2)) _clockAdjusted = true;
        _lastObservation = observation;
    }

    private static JsonObject ReadState(string path)
    {
        try
        {
            var state = JsonNode.Parse(File.ReadAllText(path), documentOptions: new() { AllowDuplicateProperties = false }) as JsonObject
                ?? throw new JsonException("根节点必须是 JSON 对象。");
            ReadString(state, "text");
            ReadString(state, "lastDisplayedText");
            ReadDate(state, "lastSeenUtc");
            if (state.TryGetPropertyValue("sessions", out var sessions))
            {
                if (sessions is not JsonArray entries) throw new JsonException("sessions 必须是会话数组。");
                foreach (var entry in entries)
                {
                    if (entry is not JsonObject session || ReadDate(session, "start") is null ||
                        session["minutes"] is not JsonValue minutes || !minutes.TryGetValue<int>(out var count) || count < 0)
                        throw new JsonException("每条会话必须包含 start 时间和非负整数 minutes。");
                }
            }
            return state;
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("请稍候的 state.json 无效：" + error.Message +
                " 请修正后重新启用插件；原文件已保留。", error);
        }
    }

    private static string? ReadString(JsonObject state, string key)
    {
        if (!state.TryGetPropertyValue(key, out var node)) return null;
        if (node is JsonValue value && value.TryGetValue<string>(out var text)) return text;
        throw new JsonException($"{key} 必须是文字。");
    }

    private static DateTimeOffset? ReadDate(JsonObject state, string key)
    {
        var text = ReadString(state, key);
        if (text is null) return null;
        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value)) return value;
        throw new JsonException($"{key} 必须是 ISO 日期时间。");
    }

    private void WriteState(JsonObject state)
    {
        var temporaryPath = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, FormatState(state), Utf8);
            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static string FormatState(JsonObject state)
    {
        // Keep the state indented for hand editing, with one complete session per line.
        var result = new StringBuilder("{\n");
        var index = 0;
        foreach (var (key, value) in state)
        {
            if (index++ != 0) result.Append(",\n");
            result.Append("  ").Append(JsonSerializer.Serialize(key, CompactJson)).Append(": ");
            if (key == "sessions" && value is JsonArray sessions)
            {
                result.Append('[');
                for (var row = 0; row < sessions.Count; row++)
                    result.Append(row == 0 ? "\n    " : ",\n    ").Append(sessions[row]?.ToJsonString(CompactJson) ?? "null");
                if (sessions.Count > 0) result.Append("\n  ");
                result.Append(']');
            }
            else result.Append((value?.ToJsonString(Json) ?? "null").Replace("\r\n", "\n").Replace("\n", "\n  "));
        }
        return result.Append("\n}\n").ToString();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
            Changed = null;
            if (_start is null) return;
            var final = _observe?.Invoke() ?? _lastObservation ?? _start;
            RememberClock(final);
            var state = ReadState(_path);
            var sessions = state["sessions"] as JsonArray;
            if (sessions is null) state["sessions"] = sessions = new JsonArray();
            var entry = new JsonObject
            {
                ["start"] = _start.TimestampUtc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm'Z'", CultureInfo.InvariantCulture),
                ["minutes"] = (int)Math.Max(0, (final.Elapsed - _start.Elapsed).TotalMinutes)
            };
            if (_clockAdjusted) entry["clockAdjusted"] = true;
            sessions.Add(entry);
            state["lastSeenUtc"] = final.TimestampUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
            WriteState(state);
        }
    }
}

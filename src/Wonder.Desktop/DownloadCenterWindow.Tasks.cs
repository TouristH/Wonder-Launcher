// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Wonder.Core;

namespace Wonder.Desktop;

internal sealed partial class DownloadCenterWindow
{
    private StackPanel _taskList = null!;
    private TextBlock _tasksSummary = null!, _taskStorage = null!;
    private ComboBox _taskFilter = null!;
    private readonly Dictionary<string, TaskCard> _taskCards = [];
    private Control BuildTasksPage()
    {
        _tasksSummary = PageStyles.Hint(""); _taskStorage = PageStyles.Hint(""); _taskStorage.Foreground = Palette.Error;
        _taskFilter = Choice("下载任务筛选", ["全部任务", "进行中与已暂停", "已完成", "失败与已取消"]); _taskFilter.SelectedIndex = 0;
        _taskFilter.SelectionChanged += (_, _) => RefreshTasks();
        _taskList = new StackPanel { Spacing = 12 };
        var clear = ActionButton("清除已结束记录", () => RunTaskAction(_downloads.ClearFinished)); clear.HorizontalAlignment = HorizontalAlignment.Left;
        return Stack(_tasksSummary, _taskStorage, _taskFilter, clear, _taskList);
    }
    private void RefreshTasks()
    {
        var snapshots = _downloads.Tasks;
        _tasksSummary.Text = $"{snapshots.Count(x => x.State is DownloadTaskState.Queued or DownloadTaskState.Running)} 项正在下载或排队 · " +
            $"{snapshots.Count(x => x.State == DownloadTaskState.Paused)} 项已暂停 · {snapshots.Count(x => x.State == DownloadTaskState.Completed)} 项已完成";
        _taskStorage.Text = _downloads.StorageError; _taskStorage.IsVisible = !string.IsNullOrEmpty(_downloads.StorageError);
        var visible = snapshots.Where(x => _taskFilter.SelectedIndex switch
        {
            1 => x.State is DownloadTaskState.Queued or DownloadTaskState.Running or DownloadTaskState.Paused,
            2 => x.State == DownloadTaskState.Completed,
            3 => x.State is DownloadTaskState.Failed or DownloadTaskState.Cancelled,
            _ => true
        }).Reverse().ToArray();
        var visibleIds = visible.Select(x => x.Id).ToHashSet();
        foreach (var key in _taskCards.Keys.Where(x => !visibleIds.Contains(x)).ToArray())
        { _taskList.Children.Remove(_taskCards[key].Surface); _taskCards.Remove(key); }
        if (visible.Length == 0)
        {
            if (_taskList.Children.Count == 0) _taskList.Children.Add(PageStyles.Card(PageStyles.Hint("这里还没有任务。选择游戏或资源，开始你的下一次冒险。")));
            return;
        }
        if (_taskCards.Count == 0) _taskList.Children.Clear();
        for (var index = 0; index < visible.Length; index++)
        {
            var task = visible[index];
            if (!_taskCards.TryGetValue(task.Id, out var card))
            {
                card = new TaskCard(this, task); _taskCards.Add(task.Id, card); _taskList.Children.Insert(index, card.Surface);
            }
            card.Update(task);
        }
    }

    internal static string StateLabel(DownloadTaskState state) => state switch
    {
        DownloadTaskState.Queued => "排队中", DownloadTaskState.Running => "进行中", DownloadTaskState.Paused => "已暂停",
        DownloadTaskState.Completed => "已完成", DownloadTaskState.Failed => "失败", _ => "已取消"
    };
    internal static string Bytes(long bytes) => bytes >= 1024L * 1024 * 1024 ? $"{bytes / (1024d * 1024 * 1024):F2} GiB" : bytes >= 1024 * 1024 ? $"{bytes / (1024d * 1024):F1} MiB" : bytes >= 1024 ? $"{bytes / 1024d:F1} KiB" : $"{bytes} B";
    private void RunTaskAction(Action action)
    {
        try { action(); }
        catch (Exception ex) { ShowStatus("任务操作失败：" + ex.Message, true); }
    }
    private void OpenResult(string? path)
    {
        try
        {
            if (path is null || !Directory.Exists(path)) throw new DirectoryNotFoundException("安装目录不存在。");
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) { ShowStatus("无法打开目录：" + ex.Message, true); }
    }
    private sealed class TaskCard
    {
        public Border Surface { get; }
        private readonly TextBlock _state, _stage, _metrics, _error;
        private readonly SelectableTextBlock _path;
        private readonly ProgressBar _progress;
        private readonly Button _pause, _resume, _cancel, _retry, _open;
        private DownloadTaskSnapshot _task;
        public TaskCard(DownloadCenterWindow owner, DownloadTaskSnapshot task)
        {
            _task = task;
            _state = PageStyles.Hint(""); _state.Foreground = Palette.Accent;
            var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            var title = Heading(task.Title); title.Margin = new Thickness(0, 0, 14, 0); header.Children.Add(title);
            Grid.SetColumn(_state, 1); header.Children.Add(_state);
            _stage = PageStyles.Hint(""); _metrics = PageStyles.Hint("");
            _error = PageStyles.Hint(""); _error.Foreground = Palette.Error;
            _path = new SelectableTextBlock { FontSize = 11, Foreground = Palette.Muted, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            _progress = new ProgressBar { Minimum = 0, Maximum = 100, Height = 7 };
            AutomationProperties.SetName(_progress, "下载进度·" + task.Id);
            _pause = ActionButton("暂停", () => owner.RunTaskAction(() => owner._downloads.Pause(task.Id)));
            _resume = ActionButton("继续", () => owner.RunTaskAction(() => owner._downloads.Resume(task.Id)));
            _cancel = ActionButton("取消下载", () => owner.RunTaskAction(() => owner._downloads.Cancel(task.Id)));
            _retry = ActionButton("重试下载", () => owner.RunTaskAction(() => owner._downloads.Retry(task.Id)));
            _open = ActionButton("打开安装目录", () => owner.OpenResult(_task.ResultPath));
            var actions = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 8, LineSpacing = 8 };
            foreach (var button in new[] { _pause, _resume, _cancel, _retry, _open }) actions.Children.Add(button);
            Surface = PageStyles.Card(Stack(header, _stage, _progress, _metrics, _error, _path, actions)); Update(task);
        }
        public void Update(DownloadTaskSnapshot task)
        {
            _task = task; _state.Text = StateLabel(task.State);
            var p = task.Progress;
            _stage.Text = p.TotalSteps > 0
                ? $"第 {p.StepNumber}/{p.TotalSteps} 步  {p.StepName}" + (p.Stage != p.StepName ? "\n" + p.Stage : "")
                : p.Stage;
            _progress.IsIndeterminate = task.State == DownloadTaskState.Running && p.TotalBytes is not > 0 && p.TotalFiles <= 0;
            _progress.Value = task.State == DownloadTaskState.Completed ? 100 : p.TotalBytes is > 0 ? Math.Clamp(p.BytesReceived * 100d / p.TotalBytes.Value, 0, 100) : p.TotalFiles > 0 ? Math.Clamp(p.CompletedFiles * 100d / p.TotalFiles, 0, 100) : 0;
            var percent = !_progress.IsIndeterminate && (p.TotalBytes is > 0 || p.TotalFiles > 0) ? $"{_progress.Value:F0}% · " : "";
            _metrics.Text = percent + Bytes(p.BytesReceived) + (p.TotalBytes is > 0 ? " / " + Bytes(p.TotalBytes.Value) : "") +
                (task.State == DownloadTaskState.Running ? " · " + Bytes((long)Math.Max(0, p.BytesPerSecond)) + "/s" : "") +
                (p.TotalFiles > 0 ? $" · {p.CompletedFiles}/{p.TotalFiles} 文件" : "");
            _error.Text = task.Error; _error.IsVisible = !string.IsNullOrEmpty(task.Error);
            _path.Text = task.ResultPath; _path.IsVisible = task.ResultPath is not null;
            _pause.IsVisible = task.State is DownloadTaskState.Queued or DownloadTaskState.Running;
            _resume.IsVisible = task.State == DownloadTaskState.Paused;
            _cancel.IsVisible = task.State is DownloadTaskState.Queued or DownloadTaskState.Running or DownloadTaskState.Paused;
            _retry.IsVisible = task.State is DownloadTaskState.Failed or DownloadTaskState.Cancelled;
            _open.IsVisible = task.State == DownloadTaskState.Completed && task.ResultPath is not null;
        }
    }
}

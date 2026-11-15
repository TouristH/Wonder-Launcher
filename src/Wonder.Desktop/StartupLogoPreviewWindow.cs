// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Wonder.Workspace;

namespace Wonder.Desktop;

internal sealed class StartupLogoPreviewWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _clock = new();
    private readonly StartupCometLogo[] _marks;
    private readonly Button[] _useButtons = new Button[2];
    private readonly Border[] _cards = new Border[2];
    private readonly TextBlock _playbackStatus = PageStyles.Hint("");
    private readonly TextBlock _selectionStatus = PageStyles.Hint("");
    private readonly Action<StartupLogoAnimation> _useAnimation;
    private bool _closed;
    internal bool IsPlaying => _timer.IsEnabled;
    internal StartupLogoAnimation SelectedAnimation { get; private set; }

    internal StartupLogoPreviewWindow(StartupLogoAnimation selected, Action<StartupLogoAnimation> useAnimation)
    {
        _useAnimation = useAnimation;
        Title = "开场动画对比 · Wonder Launcher";
        Width = 860; Height = 600; MinWidth = 780; MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Palette.Background; Foreground = Palette.Text;
        WonderMark.ApplyIcon(this);
        _marks = [new(260, StartupLogoAnimation.SettledTwinkle), new(260, StartupLogoAnimation.FragmentRelease)];
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(24) };
        var heading = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 20) };
        heading.Children.Add(PageStyles.PageTitle("选择开场动画"));
        heading.Children.Add(PageStyles.Hint("两版同步播放，使用当前主题配色。预览与重播不会占用今天的首次启动动画。"));
        layout.Children.Add(heading);
        var comparison = new Grid { ColumnDefinitions = new ColumnDefinitions("*,20,*") };
        for (var index = 0; index < _marks.Length; index++)
        {
            var animation = (StartupLogoAnimation)index;
            var content = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto") };
            content.Children.Add(PageStyles.SectionTitle(Label(animation)));
            var mark = _marks[index];
            mark.HorizontalAlignment = HorizontalAlignment.Center; mark.VerticalAlignment = VerticalAlignment.Center;
            var viewport = new Viewbox
            {
                Child = mark, Stretch = Stretch.Uniform, MaxWidth = 260, MaxHeight = 260,
                HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch
            };
            Grid.SetRow(viewport, 1); content.Children.Add(viewport);
            var description = PageStyles.Hint(index == 0
                ? "主星先停稳，再让碎星依次闪亮。"
                : "飞行中逐颗剥落，远离并缩小至原位。");
            description.MinHeight = 42; description.Margin = new Thickness(0, 8, 0, 12);
            Grid.SetRow(description, 2); content.Children.Add(description);
            var use = PageStyles.Button("使用此动画", "使用动画 " + Label(animation), () =>
            {
                _useAnimation(animation);
                SetSelected(animation);
            });
            use.HorizontalAlignment = HorizontalAlignment.Stretch;
            use.HorizontalContentAlignment = HorizontalAlignment.Center;
            use.Margin = new Thickness(0);
            Grid.SetRow(use, 3); content.Children.Add(use); _useButtons[index] = use;
            var card = PageStyles.Card(content, new Thickness(18));
            _cards[index] = card; Grid.SetColumn(card, index * 2); comparison.Children.Add(card);
        }
        Grid.SetRow(comparison, 1); layout.Children.Add(comparison);
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 18, 0, 0) };
        var feedback = new StackPanel { Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        feedback.Children.Add(_selectionStatus); feedback.Children.Add(_playbackStatus); footer.Children.Add(feedback);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(PageStyles.Button("重新播放", "重新播放开场动画", Replay));
        var close = PageStyles.Button("关闭", "关闭开场动画预览", Close); close.IsCancel = true;
        actions.Children.Add(close); Grid.SetColumn(actions, 1); footer.Children.Add(actions);
        Grid.SetRow(footer, 2); layout.Children.Add(footer);
        Content = layout;
        SetSelected(selected);
        _timer.Tick += Tick;
        Opened += (_, _) => Replay();
        Closed += (_, _) => { _closed = true; _timer.Stop(); _timer.Tick -= Tick; _clock.Stop(); };
        KeyDown += (_, args) => { if (args.Key == Key.Escape) { Close(); args.Handled = true; } };
    }

    internal void SetSelected(StartupLogoAnimation animation)
    {
        SelectedAnimation = animation;
        for (var index = 0; index < _useButtons.Length; index++)
        {
            var selected = index == (int)animation;
            _useButtons[index].Content = selected ? "当前使用" : "使用此动画";
            _useButtons[index].IsEnabled = !selected;
            _cards[index].BorderBrush = selected ? Palette.Accent : Palette.Line;
        }
        _selectionStatus.Text = "当前选择：" + Label(animation);
    }

    private void Replay()
    {
        if (_closed) return;
        foreach (var mark in _marks) mark.SetElapsed(0);
        _playbackStatus.Text = "正在同步播放…";
        _clock.Restart(); _timer.Start();
    }

    private void Tick(object? sender, EventArgs args)
    {
        var elapsed = _clock.Elapsed.TotalMilliseconds;
        foreach (var mark in _marks) mark.SetElapsed(elapsed);
        if (elapsed < StartupCometLogo.FlightMilliseconds + StartupCometLogo.SparkMilliseconds + StartupCometLogo.SettleMilliseconds) return;
        _timer.Stop(); _clock.Stop();
        _playbackStatus.Text = "播放完成 · 可重新播放";
    }

    private static string Label(StartupLogoAnimation animation) => animation == StartupLogoAnimation.FragmentRelease ? "B · 碎星剥落" : "A · 停稳闪星";
}

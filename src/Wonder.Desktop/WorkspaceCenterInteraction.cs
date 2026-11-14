// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using Avalonia;
using Avalonia.Input;
using Wonder.Workspace;

namespace Wonder.Desktop;

public sealed partial class WorkspaceView
{
    private string? _centerAreaId;

    private void EditCenter(Area? area)
    {
        CancelGesture();
        _centerAreaId = area?.Id;
        ObserveInteraction("center.mode", area?.Title ?? "工作区", area == null ? "off" : "on");
        foreach (var view in _areas.Values) view.ShowCenter(view.Area.Id == _centerAreaId);
        Focus();
        InvalidateArrange();
    }

    private void ResetCenter(Area area)
    {
        CancelGesture();
        area.CenterX = area.CenterY = 0;
        InvalidateArrange();
        NotifyChanged();
        ObserveInteraction("center.reset", area.Title);
    }

    private bool TryPressCenter(PointerPressedEventArgs e, Point position)
    {
        if (_centerAreaId is null || AreaAt(position) != _centerAreaId) return false;
        var area = Document.FindArea(_centerAreaId);
        if (area is null) return false;
        var bounds = GetAreaBounds(area.Id);
        if (position.Y <= bounds.Y + AreaView.HeaderHeight) return false;
        _gesture = new Gesture
        {
            Pointer = e.Pointer, Trigger = e, Start = position, Last = position,
            Active = true, CenterArea = area, OriginalX = area.CenterX, OriginalY = area.CenterY
        };
        ObserveGesture(_gesture, "start");
        e.Pointer.Capture(this);
        Focus();
        Cursor = new Cursor(StandardCursorType.SizeAll);
        e.Handled = true;
        return true;
    }
}

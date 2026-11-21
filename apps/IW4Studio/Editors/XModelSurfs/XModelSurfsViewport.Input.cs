using System.Numerics;
using Avalonia;
using Avalonia.Input;
using Vector = Avalonia.Vector;

namespace IW4.Studio.Desktop.Editors.XModelSurfs;

public sealed partial class XModelSurfsViewport
{
    private sealed class Drag
    {
        public required IPointer Pointer { get; init; }
        public required Point Start { get; init; }
        public required double StartOrbit { get; init; }
        public required int Index { get; init; }
        public required int Axis { get; init; }
        public required Vector3 StartPosition { get; init; }
        public required Vector3 StartSize { get; init; }
        public bool Orbit { get; init; }
        public Vector3 Position { get; set; }
        public Vector3 Size { get; set; }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_drag is not null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Focus();
        Point point = e.GetPosition(this);
        int axis = HitGrip(point);
        int hit = axis >= 0 ? _selectedIndex : HitSurface(point);
        if (axis < 0 && hit >= 0)
        {
            if (hit != _selectedIndex)
            {
                _selectedIndex = hit;
                _redrawMesh = true;
                InvalidateVisual();
                SelectionRequested?.Invoke(hit);
            }
            e.Handled = true;
            return;
        }
        bool orbit = axis < 0;
        XModelSurfsViewportSurface? surface = !orbit && hit >= 0 && hit < _surfaces.Count ? _surfaces[hit] : null;
        _drag = new Drag
        {
            Pointer = e.Pointer, Start = point, StartOrbit = _orbitDegrees,
            Index = hit, Axis = axis, Orbit = orbit,
            StartPosition = surface?.Position ?? Vector3.Zero,
            StartSize = surface?.Size ?? Vector3.Zero,
            Position = surface?.Position ?? Vector3.Zero,
            Size = surface?.Size ?? Vector3.Zero
        };
        if (surface is not null)
            EditRequested?.Invoke(new XModelSurfsViewportEdit(hit, surface.Position, surface.Size,
                XModelSurfsViewportPhase.Begin));
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Point point = e.GetPosition(this);
        if (_drag is not { } drag)
        {
            int axis = HitGrip(point);
            if (_hotAxis != axis) { _hotAxis = axis; InvalidateVisual(); }
            return;
        }
        if (drag.Pointer != e.Pointer) return;
        MoveDrag(drag, point);
        e.Handled = true;
    }

    private void MoveDrag(Drag drag, Point point)
    {
        Vector delta = point - drag.Start;
        if (drag.Orbit)
        {
            OrbitDegrees = drag.StartOrbit + delta.X * .58;
            return;
        }
        if (delta.Length < 2) delta = default;
        if (drag.Index < 0 || drag.Index >= _surfaces.Count || !_canEdit) return;
        Vector3 worldAxis = drag.Axis == 0 ? Vector3.UnitX : drag.Axis == 1 ? Vector3.UnitY : Vector3.UnitZ;
        Vector projectedAxis = Project(drag.StartPosition + worldAxis) - Project(drag.StartPosition);
        double denominator = projectedAxis.X * projectedAxis.X + projectedAxis.Y * projectedAxis.Y;
        if (denominator < 1e-8) return;
        float amount = (float)((delta.X * projectedAxis.X + delta.Y * projectedAxis.Y) / denominator);
        Vector3 position = drag.StartPosition;
        Vector3 size = drag.StartSize;
        if (_tool == XModelSurfsViewportTool.Move)
            position += worldAxis * amount;
        else
        {
            float original = Component(drag.StartSize, drag.Axis);
            size = SetComponent(size, drag.Axis, Math.Max(.001f, original + amount));
        }
        if (position == drag.Position && size == drag.Size) return;
        drag.Position = position;
        drag.Size = size;
        _redrawMesh = true;
        InvalidateVisual();
        EditRequested?.Invoke(new XModelSurfsViewportEdit(drag.Index, drag.Position, drag.Size,
            XModelSurfsViewportPhase.Update));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_drag?.Pointer != e.Pointer) return;
        MoveDrag(_drag, e.GetPosition(this));
        EndDrag(false);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_drag is not null) EndDrag(true);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key != Key.Escape || _drag is null) return;
        EndDrag(true);
        e.Handled = true;
    }

    private void EndDrag(bool cancel)
    {
        Drag drag = _drag!;
        _drag = null;
        _hotAxis = -1;
        if (drag.Orbit)
        {
            if (cancel) OrbitDegrees = drag.StartOrbit;
        }
        else
        {
            bool changed = drag.Position != drag.StartPosition || drag.Size != drag.StartSize;
            EditRequested?.Invoke(new XModelSurfsViewportEdit(drag.Index, drag.Position, drag.Size,
                cancel || !changed ? XModelSurfsViewportPhase.Cancel : XModelSurfsViewportPhase.Commit));
            _redrawMesh = true;
        }
        if (drag.Pointer.Captured == this) drag.Pointer.Capture(null);
        InvalidateVisual();
    }
}

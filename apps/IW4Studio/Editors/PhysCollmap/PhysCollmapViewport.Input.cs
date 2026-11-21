using System.Numerics;
using Avalonia;
using Avalonia.Input;
using IW4.Game.Assets.Physics;
using Vector = Avalonia.Vector;

namespace IW4.Studio.Desktop.Editors.PhysCollmap;

public sealed partial class PhysCollmapViewport
{
    private sealed class Drag
    {
        public required IPointer Pointer { get; init; }
        public required Point Start { get; init; }
        public required double StartOrbit { get; init; }
        public required int Index { get; init; }
        public required int Axis { get; init; }
        public required PhysCollmapViewportShape Shape { get; init; }
        public required bool Balance { get; init; }
        public required bool Orbit { get; init; }
        public required bool Editable { get; init; }
        public bool Moved { get; set; }
        public Vector3 Position { get; set; }
        public Vector3 Size { get; set; }
        public Vector3 Rotation { get; set; }
        public Point RingCenter { get; set; }
        public Vector RingA { get; set; }
        public Vector RingB { get; set; }
        public double RingDeterminant { get; set; }
        public double PreviousRingAngle { get; set; }
        public double RingAccumulated { get; set; }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_drag is not null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Focus();
        Point point = e.GetPosition(this);
        int axis = HitGrip(point);
        int shapeIndex = axis >= 0 ? _selectedIndex : HitShape(point);
        if (shapeIndex >= 0 && (shapeIndex != _selectedIndex || _editBalancePoint) && axis < 0)
            SelectionRequested?.Invoke(shapeIndex);
        bool balance = _editBalancePoint && axis >= 0;
        var shape = balance ? new PhysCollmapViewportShape(PhysGeomShape.Box, _balancePoint, Vector3.One, Vector3.Zero)
            : shapeIndex >= 0 && shapeIndex < _shapes.Count ? _shapes[shapeIndex] : null;
        bool orbit = axis < 0 && shapeIndex < 0;
        bool editable = _canEdit && (balance || shape is { CanTransform: true } &&
            (_tool != PhysCollmapViewportTool.Rotate || shape.CanRotate)) &&
            (axis >= 0 || _tool == PhysCollmapViewportTool.Move && shapeIndex >= 0);
        _drag = new Drag
        {
            Pointer = e.Pointer, Start = point, StartOrbit = _orbitDegrees,
            Index = balance ? -1 : shapeIndex, Axis = axis, Shape = shape ??
                new PhysCollmapViewportShape(PhysGeomShape.Box, Vector3.Zero, Vector3.One, Vector3.Zero),
            Balance = balance, Orbit = orbit, Editable = editable, Position = shape?.Position ?? Vector3.Zero,
            Size = shape?.Size ?? Vector3.One, Rotation = shape?.RotationDegrees ?? Vector3.Zero
        };
        if (editable && axis >= 0 && _tool == PhysCollmapViewportTool.Rotate && !balance)
            InitializeRingDrag(_drag);
        if (editable && shape is not null)
            EditRequested?.Invoke(new PhysCollmapViewportEdit(_drag.Index, _drag.Position, _drag.Size, _drag.Rotation, PhysCollmapViewportPhase.Begin));
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Point point = e.GetPosition(this);
        if (_drag is not { } drag)
        {
            int hot = HitGrip(point);
            if (_hotAxis != hot) { _hotAxis = hot; InvalidateVisual(); }
            return;
        }
        if (e.Pointer != drag.Pointer) return;
        MoveDrag(drag, point);
    }

    private void MoveDrag(Drag drag, Point point)
    {
        Vector delta = point - drag.Start;
        if (!drag.Moved && delta.Length < 2) return;
        drag.Moved = true;
        if (drag.Orbit)
        {
            OrbitDegrees = drag.StartOrbit + delta.X * 0.58;
            return;
        }
        if (!drag.Editable || _layer == PhysCollmapViewportLayer.Model) return;
        drag.Position = drag.Shape.Position;
        drag.Size = drag.Shape.Size;
        drag.Rotation = drag.Shape.RotationDegrees;
        if (drag.Axis >= 0)
            ApplyGripDrag(drag, point);
        else if (_tool == PhysCollmapViewportTool.Move)
        {
            Point o = Project(Vector3.Zero), x = Project(Vector3.UnitX), y = Project(Vector3.UnitY);
            double a = x.X - o.X, b = y.X - o.X, c = x.Y - o.Y, d = y.Y - o.Y;
            double determinant = a * d - b * c;
            if (Math.Abs(determinant) > 1e-8)
                drag.Position += new Vector3((float)((d * delta.X - b * delta.Y) / determinant),
                    (float)((a * delta.Y - c * delta.X) / determinant), 0);
        }
        EditRequested?.Invoke(new PhysCollmapViewportEdit(drag.Index, drag.Position, drag.Size, drag.Rotation, PhysCollmapViewportPhase.Update));
        InvalidateVisual();
    }

    private void ApplyGripDrag(Drag drag, Point point)
    {
        int axis = drag.Axis;
        if (_tool == PhysCollmapViewportTool.Rotate && !drag.Balance)
        {
            double angle = Math.Abs(drag.RingDeterminant) > 1e-6
                ? RingAngle(point, drag.RingCenter, drag.RingA, drag.RingB, drag.RingDeterminant)
                : Math.Atan2(point.Y - drag.RingCenter.Y, point.X - drag.RingCenter.X);
            double step = angle - drag.PreviousRingAngle;
            if (step > Math.PI) step -= Math.PI * 2;
            if (step < -Math.PI) step += Math.PI * 2;
            drag.RingAccumulated += step;
            drag.PreviousRingAngle = angle;
            Vector3 localAxis = axis == 0 ? Vector3.UnitX : axis == 1 ? Vector3.UnitY : Vector3.UnitZ;
            Matrix4x4 turn = Matrix4x4.CreateFromAxisAngle(localAxis, (float)drag.RingAccumulated);
            drag.Rotation = PhysCollmapViewportGeometry.Degrees(turn * PhysCollmapViewportGeometry.Rotation(drag.Shape.RotationDegrees));
            return;
        }
        Vector3 worldAxis = _tool == PhysCollmapViewportTool.Resize && !drag.Balance
            ? PhysCollmapViewportGeometry.Basis(drag.Shape.RotationDegrees)[axis]
            : axis == 0 ? Vector3.UnitX : axis == 1 ? Vector3.UnitY : Vector3.UnitZ;
        Point centerPoint = Project(drag.Shape.Position);
        Vector projectedAxis = Project(drag.Shape.Position + worldAxis) - centerPoint;
        Vector pointer = point - drag.Start;
        double denominator = projectedAxis.X * projectedAxis.X + projectedAxis.Y * projectedAxis.Y;
        float amount = denominator > 1e-8 ? (float)((pointer.X * projectedAxis.X + pointer.Y * projectedAxis.Y) / denominator) : 0;
        if (_tool == PhysCollmapViewportTool.Resize && !drag.Balance)
        {
            float oldSize = Component(drag.Shape.Size, axis);
            float newSize = Math.Max(0.1f, oldSize + amount);
            float change = newSize - oldSize;
            drag.Size = SetComponent(drag.Size, axis, newSize);
            if (drag.Shape.Shape == PhysGeomShape.Cylinder && axis < 2)
                drag.Size = new Vector3(newSize, newSize, drag.Size.Z);
            drag.Position += worldAxis * change * 0.5f;
        }
        else
            drag.Position += worldAxis * amount;
    }

    private void InitializeRingDrag(Drag drag)
    {
        Vector3[] basis = PhysCollmapViewportGeometry.Basis(drag.Shape.RotationDegrees);
        Vector3 origin = drag.Shape.Position;
        drag.RingCenter = Project(origin);
        drag.RingA = Project(origin + basis[(drag.Axis + 1) % 3]) - drag.RingCenter;
        drag.RingB = Project(origin + basis[(drag.Axis + 2) % 3]) - drag.RingCenter;
        double determinant = drag.RingA.X * drag.RingB.Y - drag.RingA.Y * drag.RingB.X;
        drag.RingDeterminant = Math.Abs(determinant) > drag.RingA.Length * drag.RingB.Length * 0.12
            ? determinant : 0;
        drag.PreviousRingAngle = drag.RingDeterminant != 0
            ? RingAngle(drag.Start, drag.RingCenter, drag.RingA, drag.RingB, drag.RingDeterminant)
            : Math.Atan2(drag.Start.Y - drag.RingCenter.Y, drag.Start.X - drag.RingCenter.X);
    }

    private static double RingAngle(Point point, Point center, Vector a, Vector b, double determinant)
    {
        double dx = point.X - center.X, dy = point.Y - center.Y;
        return Math.Atan2((a.X * dy - a.Y * dx) / determinant,
            (dx * b.Y - dy * b.X) / determinant);
    }

    private static float Component(Vector3 value, int axis) => axis == 0 ? value.X : axis == 1 ? value.Y : value.Z;
    private static Vector3 SetComponent(Vector3 value, int axis, float component) => axis == 0 ? value with { X = component }
        : axis == 1 ? value with { Y = component } : value with { Z = component };
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
        if (drag.Orbit && cancel)
            OrbitDegrees = drag.StartOrbit;
        else if (drag.Editable)
            EditRequested?.Invoke(new PhysCollmapViewportEdit(drag.Index, drag.Position, drag.Size, drag.Rotation,
                cancel || !drag.Moved ? PhysCollmapViewportPhase.Cancel : PhysCollmapViewportPhase.Commit));
        if (drag.Pointer.Captured == this)
            drag.Pointer.Capture(null);
        InvalidateVisual();
    }
}

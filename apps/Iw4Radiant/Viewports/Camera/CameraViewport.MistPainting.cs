using System.Numerics;
using Avalonia;
using Avalonia.Input;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Viewports.Camera;

public sealed partial class CameraViewport
{
    private bool _mistPaintingEnabled, _paintingMist, _mistChanged;
    private MapDocument? _mistSurfaceDocument;
    private Vector3? _lastMistStep;

    internal float MistSpacing { get; set; } = 160;
    internal float MistRadius { get; set; } = 80;
    internal bool MistErasing { get; set; }
    internal bool MistPaintingEnabled
    {
        get => _mistPaintingEnabled;
        set
        {
            if (_mistPaintingEnabled == value) return;
            if (_paintingMist) FinishGesture(cancel: true);
            if (value && FoliagePaintingEnabled) FoliagePaintingEnabled = false;
            _mistPaintingEnabled = value;
            if (_dragPointer is null) Cursor = null;
            if (!value) FoliageBrushChanged?.Invoke(null);
        }
    }

    private void BeginMistStroke(EditorSession session, IPointer pointer, Point point)
    {
        _mistSurfaceDocument = session.Scene.Document;
        if (!TryMapHit(point, includeModels: false, out Vector3 hit, out _))
        {
            _mistSurfaceDocument = null;
            InteractionStatusChanged?.Invoke("Point at an existing map surface to paint mist.");
            return;
        }
        session.BeginEdit();
        _paintingMist = true;
        _mistChanged = false;
        _lastMistStep = hit;
        _dragPointer = pointer;
        _dragButton = MouseButton.Left;
        _pressPoint = _lastPointer = point;
        pointer.Capture(this);
        if (ApplyMistStep(session, hit)) session.Refresh();
    }

    private void ContinueMistStroke(Point point)
    {
        if (!_paintingMist || _session is not { } session) return;
        if (!TryMapHit(point, includeModels: false, out Vector3 hit, out _))
        {
            _lastMistStep = null;
            return;
        }
        if (_lastMistStep is not { } previous)
        {
            _lastMistStep = hit;
            if (ApplyMistStep(session, hit)) session.Refresh();
            return;
        }
        float spacing = MistErasing ? MathF.Min(ValidSpacing, ValidRadius * 0.5f) : ValidSpacing;
        Vector3 delta = hit - previous;
        float distance = delta.Length();
        if (!float.IsFinite(distance))
        {
            _lastMistStep = null;
            return;
        }
        if (distance < spacing) return;
        Vector3 direction = delta / distance;
        bool changed = false;
        float surfaceTolerance = MistErasing ? ValidRadius * 0.25f : spacing * 0.25f;
        double traveled = spacing;
        while (traveled <= distance)
        {
            Vector3 candidate = previous + direction * (float)traveled;
            if (candidate == _lastMistStep ||
                !CameraPicking.Project(_navigation, candidate, Bounds.Size, out Point screen, out _) ||
                !TryMapHit(screen, includeModels: false, out Vector3 surface, out _) ||
                Vector3.DistanceSquared(candidate, surface) > surfaceTolerance * surfaceTolerance)
            {
                _lastMistStep = null;
                break;
            }
            changed |= ApplyMistStep(session, surface);
            _lastMistStep = candidate;
            double next = traveled + spacing;
            if (next <= traveled) break;
            traveled = next;
        }
        if (changed) session.Refresh();
    }

    private bool ApplyMistStep(EditorSession session, Vector3 surface)
    {
        if (MistErasing)
        {
            float radiusSquared = MathF.Pow(ValidRadius, 2);
            int removed = session.Document.Entities.RemoveAll(entity => MistPainting.IsPainted(entity) &&
                session.Visibility.CanSelect(session.Document, entity) &&
                entity.TryGetOrigin(out Vector3 origin) &&
                Vector3.DistanceSquared(surface, origin - Vector3.UnitZ * 32) <= radiusSquared);
            _mistChanged |= removed > 0;
            return removed > 0;
        }
        float spacing = ValidSpacing;
        if (session.Document.Entities.Any(entity => MistPainting.Overlaps(entity, surface, spacing)) ||
            _mistSurfaceDocument is { } expanded && expanded.Entities.Any(entity =>
                MistPainting.Overlaps(entity, surface, spacing))) return false;
        session.Document.Entities.Add(MistPainting.Create(surface, spacing));
        _mistChanged = true;
        return true;
    }

    private void UpdateMistBrush(Point point)
    {
        if (!MistPaintingEnabled || !TryMapHit(point, includeModels: false, out Vector3 hit, out Vector3 normal))
        {
            FoliageBrushChanged?.Invoke(null);
            return;
        }
        (Vector3 tangent, Vector3 bitangent) = SurfaceBasis(normal);
        float radius = MistErasing ? ValidRadius : ValidSpacing * 0.5f;
        var points = new List<Point>(48);
        for (int index = 0; index < 48; index++)
        {
            float angle = index * (MathF.Tau / 48);
            Vector3 edge = hit + (tangent * MathF.Cos(angle) + bitangent * MathF.Sin(angle)) * radius;
            if (!CameraPicking.Project(_navigation, edge, Bounds.Size, out Point screen, out _))
            {
                FoliageBrushChanged?.Invoke(null);
                return;
            }
            points.Add(screen);
        }
        FoliageBrushChanged?.Invoke(points);
    }

    private float ValidSpacing => float.IsFinite(MistSpacing) && MistSpacing > 0 ? MathF.Max(1, MistSpacing) : 160;
    private float ValidRadius => float.IsFinite(MistRadius) && MistRadius > 0 ? MathF.Max(1, MistRadius) : 80;
}

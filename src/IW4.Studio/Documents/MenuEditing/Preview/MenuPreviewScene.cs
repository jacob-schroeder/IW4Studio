using IW4.Render.UI.ScreenPlacement;

namespace IW4.Studio.Documents.MenuEditing.Preview;

public readonly record struct MenuPreviewRect(
    float X,
    float Y,
    float Width,
    float Height)
{
    public bool Contains(float x, float y)
    {
        float oppositeX = X + Width;
        float oppositeY = Y + Height;
        return x >= Math.Min(X, oppositeX) &&
            y >= Math.Min(Y, oppositeY) &&
            x <= Math.Max(X, oppositeX) &&
            y <= Math.Max(Y, oppositeY);
    }
}

public readonly record struct MenuPreviewInsets(
    float Left,
    float Top,
    float Right,
    float Bottom);

/// <summary>
/// Retains the native pre-ScreenPlacement rectangle beside its physical PS3
/// output bounds. Renderers use output pixels; text and editing use the
/// authored coordinate system and effective alignment.
/// </summary>
public readonly record struct MenuPreviewPlacement(
    MenuRectangleValue VirtualRectangle,
    MenuPreviewRect OutputBounds);

public sealed record MenuPreviewSettings(UiScreenPlacement ScreenPlacement)
{
    public static MenuPreviewSettings Default { get; } = new(
        UiScreenPlacement.Iw4Ps3Hd);

    public float CanvasWidth => ScreenPlacement.OutputWidth;

    public float CanvasHeight => ScreenPlacement.OutputHeight;

    public MenuPreviewInsets SafeArea
    {
        get
        {
            UiScreenInsets value = ScreenPlacement.ViewableInsets;
            return new MenuPreviewInsets(
                value.Left,
                value.Top,
                value.Right,
                value.Bottom);
        }
    }
}

public abstract record MenuPreviewPrimitive(
    MenuNodeId NodeId,
    MenuPreviewPlacement Placement,
    int ZIndex)
{
    public MenuPreviewRect Bounds => Placement.OutputBounds;
}

public sealed record MenuPreviewFill(
    MenuNodeId NodeId,
    MenuPreviewPlacement Placement,
    int ZIndex,
    MenuColorValue Color) : MenuPreviewPrimitive(NodeId, Placement, ZIndex);

public sealed record MenuPreviewBorder(
    MenuNodeId NodeId,
    MenuPreviewPlacement Placement,
    int ZIndex,
    MenuColorValue Color,
    float ThicknessX,
    float ThicknessY,
    IW4.Assets.Assets.Menu.WindowBorder Border) :
    MenuPreviewPrimitive(NodeId, Placement, ZIndex);

public sealed record MenuPreviewMaterial(
    MenuNodeId NodeId,
    MenuPreviewPlacement Placement,
    int ZIndex,
    string MaterialName,
    MenuColorValue Tint,
    bool FlipHorizontal,
    bool FlipVertical) : MenuPreviewPrimitive(NodeId, Placement, ZIndex);

public sealed record MenuPreviewText(
    MenuNodeId NodeId,
    MenuPreviewPlacement Placement,
    int ZIndex,
    string Text,
    MenuColorValue Color,
    float Scale,
    int Font,
    int Alignment,
    int Style,
    float OffsetX,
    float OffsetY,
    float BorderInset) : MenuPreviewPrimitive(NodeId, Placement, ZIndex);

public sealed record MenuPreviewPlaceholder(
    MenuNodeId NodeId,
    MenuPreviewPlacement Placement,
    int ZIndex,
    string Label) : MenuPreviewPrimitive(NodeId, Placement, ZIndex);

public sealed record MenuPreviewHitRegion(
    MenuNodeId NodeId,
    MenuPreviewPlacement Placement,
    int ZIndex)
{
    public MenuPreviewRect Bounds => Placement.OutputBounds;
}

public enum MenuPreviewFidelitySeverity
{
    Information,
    Warning
}

public sealed record MenuPreviewFidelityIssue(
    MenuNodeId? NodeId,
    string Path,
    string Message,
    MenuPreviewFidelitySeverity Severity);

/// <summary>Immutable renderer-neutral result of Menu preview projection.</summary>
public sealed class MenuPreviewScene
{
    private readonly IReadOnlyList<MenuPreviewPrimitive> _primitives;
    private readonly IReadOnlyList<MenuPreviewHitRegion> _hitRegions;
    private readonly IReadOnlyList<MenuPreviewFidelityIssue> _fidelityIssues;

    internal MenuPreviewScene(
        MenuPreviewSettings settings,
        IEnumerable<MenuPreviewPrimitive> primitives,
        IEnumerable<MenuPreviewHitRegion> hitRegions,
        IEnumerable<MenuPreviewFidelityIssue> fidelityIssues)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _primitives = Array.AsReadOnly(primitives.OrderBy(value => value.ZIndex).ToArray());
        _hitRegions = Array.AsReadOnly(hitRegions.OrderBy(value => value.ZIndex).ToArray());
        _fidelityIssues = Array.AsReadOnly(fidelityIssues.ToArray());
    }

    public MenuPreviewSettings Settings { get; }
    public IReadOnlyList<MenuPreviewPrimitive> Primitives => _primitives;
    public IReadOnlyList<MenuPreviewHitRegion> HitRegions => _hitRegions;
    public IReadOnlyList<MenuPreviewFidelityIssue> FidelityIssues => _fidelityIssues;

    /// <summary>Returns the topmost selectable node at a preview coordinate.</summary>
    public MenuNodeId? HitTest(float x, float y)
    {
        for (int index = _hitRegions.Count - 1; index >= 0; index--)
        {
            if (_hitRegions[index].Bounds.Contains(x, y))
                return _hitRegions[index].NodeId;
        }

        return null;
    }
}

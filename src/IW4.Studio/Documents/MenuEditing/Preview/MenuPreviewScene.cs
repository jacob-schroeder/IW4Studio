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

public sealed record MenuPreviewSettings(
    float CanvasWidth,
    float CanvasHeight,
    MenuPreviewInsets SafeArea)
{
    /// <summary>
    /// The PS3 Menu coordinate system is a canonical 640x480 virtual canvas.
    /// Rendering surfaces should scale this scene after projection rather than
    /// substituting physical display dimensions here.
    /// </summary>
    public static MenuPreviewSettings Default { get; } = new(
        640,
        480,
        new MenuPreviewInsets(0, 0, 0, 0));

    public static MenuPreviewSettings WithSafeArea(
        float horizontalPercent,
        float verticalPercent,
        float canvasWidth = 640,
        float canvasHeight = 480)
    {
        if (horizontalPercent is < 0 or >= 0.5f)
            throw new ArgumentOutOfRangeException(nameof(horizontalPercent));
        if (verticalPercent is < 0 or >= 0.5f)
            throw new ArgumentOutOfRangeException(nameof(verticalPercent));
        return new MenuPreviewSettings(
            canvasWidth,
            canvasHeight,
            new MenuPreviewInsets(
                canvasWidth * horizontalPercent,
                canvasHeight * verticalPercent,
                canvasWidth * horizontalPercent,
                canvasHeight * verticalPercent));
    }
}

public abstract record MenuPreviewPrimitive(
    MenuNodeId NodeId,
    MenuPreviewRect Bounds,
    int ZIndex);

public sealed record MenuPreviewFill(
    MenuNodeId NodeId,
    MenuPreviewRect Bounds,
    int ZIndex,
    MenuColorValue Color) : MenuPreviewPrimitive(NodeId, Bounds, ZIndex);

public sealed record MenuPreviewBorder(
    MenuNodeId NodeId,
    MenuPreviewRect Bounds,
    int ZIndex,
    MenuColorValue Color,
    float Thickness,
    IW4.Assets.Assets.Menu.WindowBorder Border) :
    MenuPreviewPrimitive(NodeId, Bounds, ZIndex);

public sealed record MenuPreviewMaterial(
    MenuNodeId NodeId,
    MenuPreviewRect Bounds,
    int ZIndex,
    string MaterialName,
    MenuColorValue Tint) : MenuPreviewPrimitive(NodeId, Bounds, ZIndex);

public sealed record MenuPreviewText(
    MenuNodeId NodeId,
    MenuPreviewRect Bounds,
    int ZIndex,
    string Text,
    MenuColorValue Color,
    float Scale,
    int Font,
    int Alignment,
    int Style) : MenuPreviewPrimitive(NodeId, Bounds, ZIndex);

public sealed record MenuPreviewPlaceholder(
    MenuNodeId NodeId,
    MenuPreviewRect Bounds,
    int ZIndex,
    string Label) : MenuPreviewPrimitive(NodeId, Bounds, ZIndex);

public sealed record MenuPreviewHitRegion(
    MenuNodeId NodeId,
    MenuPreviewRect Bounds,
    int ZIndex);

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

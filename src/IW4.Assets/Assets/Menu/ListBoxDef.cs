using IW4.Assets.Math;
using IW4.Assets.Assets.Material;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Menu;

public sealed class ListBoxDef
{
    public const int SerializedSize = 0x158;

    // PS3 runtime treats these as per-local-client mutable visible range cursors.
    public IReadOnlyList<int> StartPos { get; init; } = [];
    public IReadOnlyList<int> EndPos { get; init; } = [];

    // Preserved serialized draw-padding slot.
    public int DrawPadding { get; init; }

    public float ElementWidth { get; init; }
    public float ElementHeight { get; init; }
    public int ElementStyle { get; init; }
    public int NumColumns { get; init; }
    public IReadOnlyList<ColumnInfo> ColumnInfo { get; init; } = [];
    public XPointer<MenuEventHandlerSet> DoubleClick { get; init; }
    public MenuEventHandlerSet? DoubleClickSet { get; set; }
    public int NotSelectable { get; init; }
    public int NoScrollbars { get; init; }

    // Preserved serialized paging slot.
    public int UsePaging { get; init; }

    public Vec4 SelectBorder { get; init; } = new();
    public XPointer<MaterialAsset> SelectIcon { get; init; }
    public MaterialAsset? SelectIconMaterial { get; set; }

    /// <summary>Serialized external Material identity, distinct from the
    /// resolved runtime material object.</summary>
    public string? SelectIconMaterialName { get; set; }
}

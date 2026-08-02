using IW4.Assets.Math;
using IW4.Assets.Assets.Material;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Menu;

public sealed class WindowDef
{
    // Serialized PS3 WindowDef root size.
    public const int SerializedSize = 0xB0;

    public XString NamePointer { get; init; }
    public string? Name { get; set; }
    public RectangleDef Rect { get; init; } = new();
    public RectangleDef RectClient { get; init; } = new();
    public XString GroupPointer { get; init; }
    public string? Group { get; set; }
    public WindowStyle Style { get; init; }
    public WindowBorder Border { get; init; }

    /// <summary>
    /// Numeric owner-draw selector. PS3 item paint treats this as a selector value passed into owner-draw render paths.
    /// </summary>
    public WindowOwnerDraw OwnerDraw { get; init; }

    /// <summary>
    /// Numeric owner-draw visibility selector. PS3 item visibility passes this to its owner-draw visibility helper when non-zero.
    /// </summary>
    public int OwnerDrawFlags { get; init; }

    public float BorderSize { get; init; }
    public WindowStaticFlags StaticFlags { get; init; }
    public IReadOnlyList<WindowDynamicFlags> DynamicFlags { get; init; } = [];
    public int NextTime { get; init; }
    public Vec4 ForeColor { get; init; } = new();
    public Vec4 BackColor { get; init; } = new();
    public Vec4 BorderColor { get; init; } = new();
    public Vec4 OutlineColor { get; init; } = new();
    public Vec4 DisableColor { get; init; } = new();
    public XPointer<MaterialAsset> Background { get; init; }

    /// <summary>Resolved only for inspection and detached authoring capture;
    /// emission uses the preserved symbolic Material identity, never this
    /// runtime object or its address.</summary>
    public MaterialAsset? BackgroundMaterial { get; set; }

    /// <summary>Serialized external Material identity captured at load time.
    /// This is deliberately separate from <see cref="BackgroundMaterial"/>,
    /// which is a runtime pool object and must never be retained by a draft.</summary>
    public string? BackgroundMaterialName { get; set; }
}

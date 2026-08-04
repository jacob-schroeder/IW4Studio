using System.ComponentModel;

namespace IW4.Assets.Assets.Menu;

/// <summary>
/// Known values from the native IMAGE_TRACK_* material-registration table.
/// The serialized MenuDef and ItemDef fields remain int32 so unknown values
/// can still be inspected and preserved.
/// </summary>
public enum ImageTrackType : int
{
    [Description("0 · Misc")]
    Misc = 0,

    [Description("1 · Debug")]
    Debug = 1,

    [Description("2 · Texture name")]
    TextureName = 2,

    [Description("3 · UI")]
    UI = 3,

    [Description("4 · Lightmap")]
    Lightmap = 4,

    [Description("5 · Light")]
    Light = 5,

    [Description("6 · FX")]
    FX = 6,

    [Description("7 · HUD")]
    HUD = 7,

    [Description("8 · Model")]
    Model = 8,

    [Description("9 · World")]
    World = 9
}

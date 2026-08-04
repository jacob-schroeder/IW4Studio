using System.ComponentModel;

namespace IW4.Assets.Assets.Menu;

[Flags]
public enum ItemDvarFlags : int
{
    None = 0,

    /// <summary>Enable item interaction when the dvar test matches.</summary>
    [Description("Enable on match")]
    Enable = 0x00000001,

    /// <summary>Disable item interaction when the dvar test matches.</summary>
    [Description("Disable on match")]
    Disable = 0x00000002,

    /// <summary>Show the item when the dvar test matches.</summary>
    [Description("Show on match")]
    Show = 0x00000004,

    /// <summary>Hide the item when the dvar test matches.</summary>
    [Description("Hide on match")]
    Hide = 0x00000008
}

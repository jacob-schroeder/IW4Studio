using System.ComponentModel;

namespace IW4.Assets.Assets.Menu;

/// <summary>
/// Serialized ItemDef font selector consumed by IW4 UI_GetFontHandle.
/// Default and Normal resolve against the effective text scale.
/// </summary>
public enum ItemFont : int
{
    [Description("Default · Scale-dependent")]
    Default = 0,

    [Description("Normal · Scale-dependent")]
    Normal = 1,

    [Description("Big")]
    Big = 2,

    [Description("Small")]
    Small = 3,

    [Description("Bold")]
    Bold = 4,

    [Description("Console")]
    Console = 5,

    [Description("Objective")]
    Objective = 6,

    [Description("Text")]
    Text = 7,

    [Description("Extra Big")]
    ExtraBig = 8,

    [Description("HUD Big")]
    HudBig = 9,

    [Description("HUD Small")]
    HudSmall = 10
}

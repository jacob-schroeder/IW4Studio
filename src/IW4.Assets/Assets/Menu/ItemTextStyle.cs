using System.ComponentModel;

namespace IW4.Assets.Assets.Menu;

/// <summary>Rendering style passed to IW4's text-draw helpers.</summary>
public enum ItemTextStyle : int
{
    [Description("Normal")]
    Normal = 0,

    [Description("Blink")]
    Blink = 1,

    /// <summary>
    /// Legacy selector retained by authored data. MW2 renders it as Normal.
    /// </summary>
    [Description("Legacy 2 · Renders as Normal")]
    LegacyStyle2 = 2,

    [Description("Drop shadow")]
    Shadowed = 3,

    /// <summary>
    /// Legacy selector retained by authored data. MW2 renders it as Normal.
    /// </summary>
    [Description("Legacy 4 · Renders as Normal")]
    LegacyStyle4 = 4,

    /// <summary>
    /// Legacy selector retained by authored data. MW2 renders it as Normal.
    /// </summary>
    [Description("Legacy 5 · Renders as Normal")]
    LegacyStyle5 = 5,

    [Description("Extra drop shadow")]
    ShadowedMore = 6,

    /// <summary>
    /// PS3-only selector that sets renderer flag 0x400. No canonical effect
    /// name is present in the available PS3 or Xbox symbol corpus.
    /// </summary>
    [Description("PS3 Style 7 · Render flag 0x400")]
    Ps3Style7 = 7,

    /// <summary>
    /// PS3-only selector that sets renderer flags 0x400 and 0x800. No
    /// canonical effect names are present in the available symbol corpus.
    /// </summary>
    [Description("PS3 Style 8 · Render flags 0xC00")]
    Ps3Style8 = 8,

    [Description("Monospace")]
    Monospace = 128,

    /// <summary>
    /// Runtime-supported composite that forces monospace glyph advances and
    /// enables the standard drop shadow.
    /// </summary>
    [Description("Monospace + Shadow")]
    MonospaceShadowed = 132
}

using System.ComponentModel;

namespace IW4.Assets.Assets.Menu;

/// <summary>
/// Horizontal alignment passed to an ItemDef owner-draw handler.
/// Ordinary item text uses <c>textAlignMode</c> instead.
/// </summary>
public enum ItemHorizontalAlignment : int
{
    [Description("Left")]
    Left = 0,

    [Description("Center")]
    Center = 1,

    [Description("Right")]
    Right = 2
}

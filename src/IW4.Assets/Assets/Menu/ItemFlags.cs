using System.ComponentModel;

namespace IW4.Assets.Assets.Menu;

[Flags]
public enum ItemFlags : int
{
    None = 0,

    /// <summary>Resolve the item text through the save-game information path.</summary>
    [Description("Save-game text")]
    SaveGameText = 0x00000001,

    /// <summary>Resolve the item text through the cinematic-subtitle path.</summary>
    [Description("Cinematic subtitle")]
    CinematicSubtitle = 0x00000002
}

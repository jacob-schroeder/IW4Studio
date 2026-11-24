namespace IW4.Game.Assets.ColMap;

/// <summary>Runtime state of a dynamic entity in the clip map's client list.</summary>
[Flags]
public enum DynEntityClientFlags : ushort
{
    None = 0,
    Active = 0x01,
    Visible = 0x02
}

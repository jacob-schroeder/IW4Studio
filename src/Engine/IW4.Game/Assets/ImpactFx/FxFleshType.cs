namespace IW4.Game.Assets.ImpactFx;

/// <summary>Flesh slots at impact-entry +0x7C. On PS3 MP, hit location selects
/// the body/head pair and the fatal-impact flag selects the second slot in that pair.</summary>
public enum FxFleshType
{
    BodyNonFatal = 0,
    BodyFatal = 1,
    HeadNonFatal = 2,
    HeadFatal = 3,
    Count = 4
}

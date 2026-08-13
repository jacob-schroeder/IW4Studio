namespace IW4.Assets.Assets.XModel;

/// <summary>
/// UV-tiling classification produced for an XSurface by the console model
/// toolchain. Accidental tiling identifies coordinates just outside the unit
/// range; intentional tiling identifies the broader authored tiling case.
/// </summary>
public enum XSurfaceTileMode : byte
{
    NoTiling = 0,
    AccidentalTiling = 1,
    IntentionalTiling = 2
}

namespace Iw4Radiant.Materials;

internal static class CaulkMaterial
{
    internal const string Name = "caulk";
    internal const int Contents = 0x00000001;
    internal const int SurfaceFlags = 0x000400A0;

    internal static bool IsCaulk(string materialName) => materialName.Equals(Name, StringComparison.Ordinal);
}

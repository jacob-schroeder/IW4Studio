namespace IW4.Render.Shaders;

/// <summary>
/// The six numeric MaterialShaderArgument constant classes consumed by the
/// PS3 backend. This deliberately excludes the CodePrimBegin/CodePrimEnd enum
/// aliases so one raw value cannot acquire two meanings.
/// </summary>
public enum SelectedPassConstantKind : ushort
{
    MaterialVertex = 0,
    LiteralVertex = 1,
    CodeVertex = 3,
    CodePixel = 5,
    MaterialPixel = 6,
    LiteralPixel = 7
}

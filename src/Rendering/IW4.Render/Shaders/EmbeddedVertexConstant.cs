namespace IW4.Render.Shaders;

/// <summary>
/// One compiler-owned vertex constant recovered from the selected Cg binary
/// program. These constants are part of the program image, not material-pass
/// arguments or live code-constant rows.
/// </summary>
public sealed record EmbeddedVertexConstant(
    int ParameterOrdinal,
    ushort Destination,
    uint RawResourceIndex,
    string ParameterName,
    uint DefaultValueOffset,
    ShaderConstantValue Value,
    bool IsOperationallyResolved);

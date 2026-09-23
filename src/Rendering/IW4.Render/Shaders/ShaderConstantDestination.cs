namespace IW4.Render.Shaders;

public sealed record ShaderConstantDestination(
    int ArgumentIndex,
    string ArgumentType,
    ushort Destination,
    uint Argument,
    string ResourceIdentity,
    bool IsOperationallyResolved,
    ShaderConstantValue? Value = null,
    ShaderCodeMatrixBinding? CodeMatrix = null,
    ushort? CodeConstantSourceRow = null);

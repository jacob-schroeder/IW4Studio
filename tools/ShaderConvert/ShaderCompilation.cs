using IW4.Game.Assets.TechniqueSet;
using IW4.Linker.Plans;

namespace ShaderConvert;

/// <summary>A compiled PS3 material shader and the parameters its material must bind.</summary>
public sealed record ShaderCompilation(
    MaterialShaderAsset Asset,
    IReadOnlyList<ShaderParameter> Parameters,
    bool WorldPositionLowered,
    int NativeWorldMatrixRegister,
    string CompilerIdentity)
{
    public byte[] Program => Asset.Data ?? throw new InvalidDataException("The compiler returned no Cg program.");
    public MaterialShaderVertexCommandMetrics? VertexCommands { get; internal init; }
}

public enum ShaderParameterKind { Constant, Sampler }

public sealed record ShaderParameter(
    string Name,
    uint Resource,
    uint ResourceIndex,
    uint RegisterCount,
    ShaderParameterKind Kind,
    bool IsReferenced,
    ushort Class,
    ushort Rows,
    ushort Columns,
    ushort Elements);

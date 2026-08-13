using IW4.Assets.Assets;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.TechniqueSet;

public sealed record MaterialShaderArgumentAsset(
    int Offset,
    MaterialShaderArgumentType Type,
    ushort Dest,
    int ArgumentRaw,
    MaterialShaderLiteralConstant? LiteralConstant,
    XPointerReference ArgumentPointer = default)
{
    public MaterialCodeConstantArgument CodeConstant => Type switch
    {
        MaterialShaderArgumentType.CodeVertexConst or
        MaterialShaderArgumentType.CodePixelConst =>
            MaterialCodeConstantArgument.FromRaw(ArgumentRaw),
        _ => throw new InvalidOperationException(
            $"Shader argument type {Type} does not contain a code constant.")
    };

    public MaterialTextureSource CodeTextureSource => Type ==
        MaterialShaderArgumentType.CodePixelSampler
            ? (MaterialTextureSource)unchecked((uint)ArgumentRaw)
            : throw new InvalidOperationException(
                $"Shader argument type {Type} does not contain a code texture source.");

    public uint MaterialNameHash => Type switch
    {
        MaterialShaderArgumentType.MaterialVertexConst or
        MaterialShaderArgumentType.MaterialPixelSampler or
        MaterialShaderArgumentType.MaterialPixelConst =>
            unchecked((uint)ArgumentRaw),
        _ => throw new InvalidOperationException(
            $"Shader argument type {Type} does not contain a material name hash.")
    };

}

using IW4.Game.Assets;
using IW4.Game.Pointers;

namespace IW4.Game.Assets.TechniqueSet;

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

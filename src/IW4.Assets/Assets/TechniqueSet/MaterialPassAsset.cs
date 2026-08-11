using IW4.Assets.Assets;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.TechniqueSet;

public sealed class MaterialPassAsset
{
    public const int SerializedSize = 0x18;

    public int Offset { get; init; }
    public XPointer<MaterialVertexDeclarationAsset> VertexDeclPointer { get; init; }
    public XPointer<MaterialShaderAsset> VertexShaderPointer { get; init; }
    public XPointer<MaterialShaderAsset> PixelShaderPointer { get; init; }
    public byte PerPrimArgCount { get; init; }
    public byte PerObjArgCount { get; init; }
    public byte StableArgCount { get; init; }
    public byte CustomSamplerFlags { get; init; }
    public byte PrecompiledIndex { get; init; }
    public XPointer<MaterialShaderArgumentAsset[]> ArgsPointer { get; init; }
    public MaterialVertexDeclarationAsset? VertexDeclaration { get; set; }
    public MaterialShaderAsset? VertexShader { get; set; }
    public MaterialShaderAsset? PixelShader { get; set; }
    public IReadOnlyList<MaterialShaderArgumentAsset> Args { get; set; } = [];
}

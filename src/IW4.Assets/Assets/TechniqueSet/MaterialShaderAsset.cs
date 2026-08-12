using IW4.Assets.Assets;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.TechniqueSet;

public sealed class MaterialShaderAsset : BaseAsset
{
    public const int PixelShaderSerializedSize = 0x18;
    public const int VertexShaderSerializedSize = 0x0c;
    public const int ShaderLoadDefSerializedSize = 0x08;

    // Managed discriminator for the two native XAsset pool families; not a
    // serialized root field.
    public MaterialShaderKind Kind { get; init; }
    public override XAssetType SerializedAssetType
    {
        get
        {
            try
            {
                return GetAssetType(Kind);
            }
            catch (ArgumentOutOfRangeException)
            {
                throw new InvalidOperationException(
                    $"Unsupported material shader kind {Kind}.");
            }
        }
    }

    public static XAssetType GetAssetType(MaterialShaderKind kind) => kind switch
    {
        MaterialShaderKind.Pixel => XAssetType.PixelShader,
        MaterialShaderKind.Vertex => XAssetType.VertexShader,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    public static int GetSerializedSize(MaterialShaderKind kind) => kind switch
    {
        MaterialShaderKind.Pixel => PixelShaderSerializedSize,
        MaterialShaderKind.Vertex => VertexShaderSerializedSize,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    public static int GetProgramByteCount(MaterialShaderKind kind) => kind switch
    {
        MaterialShaderKind.Pixel => PixelShaderSerializedSize -
            (sizeof(int) + ShaderLoadDefSerializedSize),
        MaterialShaderKind.Vertex => VertexShaderSerializedSize -
            (sizeof(int) + ShaderLoadDefSerializedSize),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    // 0x00: XString. Both PS3 bodies push LARGE and call Load_XString.
    public XString NamePointer { get; init; }
    public string? Name { get; init; }
    public override string? SerializedAssetName => Name;

    // 0x04: GfxShaderLoadDef bytecode pointer; packed values use alias-cell
    // conversion. 0x08: unsigned byte count.
    public XPointer<MaterialShaderBytecode> DataPointer { get; init; }
    public uint DataSize { get; init; }

    // Pixel only, 0x0C..0x17. Preserve these GPU program bytes verbatim.
    // Vertex shaders have no trailing program bytes.
    public byte[] ProgramBytes { get; init; } = [];

    // Materialized GfxShaderLoadDef payload, not an additional root field.
    public byte[]? Data { get; init; }
}

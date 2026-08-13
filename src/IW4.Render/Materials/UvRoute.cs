
using IW4.Assets.Assets.TechniqueSet;

namespace IW4.Render.Materials;

public sealed record UvRoute(
    string Label,
    string WorldVertexFormat,
    MaterialStreamSource TexCoordSource,
    byte StreamIndex,
    int Stride,
    int Offset,
    byte FormatByte0,
    RsxVertexElementType FormatByte1,
    UvBaseMode BaseMode,
    int ComponentA,
    int ComponentB,
    float ScaleU,
    float ScaleV,
    float AddU,
    float AddV)
{
    public string TexCoordSourceName => StreamSourceName(TexCoordSource);

    public string FormatName => FormatByte1 switch
    {
        RsxVertexElementType.Float32 => "V32_FLOAT",
        RsxVertexElementType.Float16 => "V16_FLOAT",
        _ => $"RSX_TYPE_0x{(byte)FormatByte1:X2}"
    };

    public string ComponentText => ComponentA == 0 && ComponentB == 1
        ? "xy"
        : $"{ComponentName(ComponentA)}{ComponentName(ComponentB)}";

    public string Formula
    {
        get
        {
            string stride = Hex(Stride);
            string offset = Hex(Offset);
            string stream = $"stream{StreamIndex}";
            return BaseMode switch
            {
                UvBaseMode.Stream0BaseVertexSourceStride =>
                    $"{stream} + baseVertex*{stride} + localIndex*{stride} + {offset}",
                UvBaseMode.Stream0GlobalIndexSourceStride =>
                    $"{stream} + (baseVertex+localIndex)*{stride} + {offset}",
                UvBaseMode.Stream0LocalIndexOnly =>
                    $"{stream} + localIndex*{stride} + {offset}",
                UvBaseMode.Stream1ZeroBase =>
                    $"{stream} + localIndex*{stride} + {offset}",
                _ when StreamIndex == 1 =>
                    $"{stream} + surface.vertexLayerData + localIndex*{stride} + {offset}",
                _ =>
                    $"{stream} + baseVertex*0x10 + localIndex*{stride} + {offset}"
            };
        }
    }

    public string TransformText => ScaleU == 1f && ScaleV == 1f && AddU == 0f && AddV == 0f
        ? "none"
        : $"uv*({ScaleU.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}," +
          $"{ScaleV.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)})+" +
          $"({AddU.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}," +
          $"{AddV.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)})";

    public string BatchKey =>
        $"{WorldVertexFormat}_{(byte)TexCoordSource:X2}_{StreamIndex}_{Stride}_{Offset}_{FormatByte0:X2}_{(byte)FormatByte1:X2}_" +
        $"{BaseMode}_{ComponentA}_{ComponentB}_{ScaleU.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}_" +
        $"{ScaleV.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}_{AddU.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}_" +
        AddV.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

    public static UvRoute StaticModel(MaterialStreamSource texCoordSource) =>
        new(
            "static model",
            "STATIC_XSURFACE",
            texCoordSource,
            0,
            0x10,
            0,
            0,
            RsxVertexElementType.Disabled,
            UvBaseMode.Engine,
            0,
            1,
            1f,
            1f,
            0f,
            0f);

    private static string ComponentName(int component) => component switch
    {
        0 => "x",
        1 => "y",
        2 => "z",
        3 => "w",
        _ => component.ToString(System.Globalization.CultureInfo.InvariantCulture)
    };

    public static string StreamSourceName(MaterialStreamSource source) =>
        source switch
    {
        MaterialStreamSource.Position => "POSITION",
        MaterialStreamSource.Color => "COLOR",
        MaterialStreamSource.TexCoord0 => "TEXCOORD_0",
        MaterialStreamSource.Normal => "NORMAL",
        MaterialStreamSource.Tangent => "TANGENT",
        MaterialStreamSource.TexCoord1 => "TEXCOORD_1",
        MaterialStreamSource.TexCoord2 => "TEXCOORD_2",
        MaterialStreamSource.NormalTransform0 => "NORMAL_TRANSFORM_0",
        MaterialStreamSource.NormalTransform1 => "NORMAL_TRANSFORM_1",
        _ => $"SOURCE_0x{(byte)source:X2}"
    };

    private static string Hex(int value) => value < 0 ? $"-0x{-value:X}" : $"0x{value:X}";
}

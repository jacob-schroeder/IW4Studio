
namespace IW4.Render.Materials;

public sealed record UvRoute(
    string Label,
    string WorldVertexFormat,
    byte TexCoordSource,
    byte StreamIndex,
    int Stride,
    int Offset,
    byte FormatByte0,
    byte FormatByte1,
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
        0x02 => "V32_FLOAT",
        0x03 => "V16_FLOAT",
        _ => $"RSX_TYPE_0x{FormatByte1:X2}"
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
        $"{WorldVertexFormat}_{TexCoordSource:X2}_{StreamIndex}_{Stride}_{Offset}_{FormatByte0:X2}_{FormatByte1:X2}_" +
        $"{BaseMode}_{ComponentA}_{ComponentB}_{ScaleU.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}_" +
        $"{ScaleV.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}_{AddU.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}_" +
        AddV.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

    public static UvRoute StaticModel(byte texCoordSource) =>
        new(
            "static model",
            "STATIC_XSURFACE",
            texCoordSource,
            0,
            0x10,
            0,
            0,
            0,
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

    public static string StreamSourceName(byte source) => source switch
    {
        0x00 => "POSITION",
        0x01 => "COLOR",
        0x02 => "TEXCOORD_0",
        0x03 => "NORMAL",
        0x04 => "TANGENT",
        0x05 => "TEXCOORD_1",
        0x06 => "TEXCOORD_2",
        0x07 => "NORMAL_TRANSFORM_0",
        0x08 => "NORMAL_TRANSFORM_1",
        _ => $"SOURCE_0x{source:X2}"
    };

    private static string Hex(int value) => value < 0 ? $"-0x{-value:X}" : $"0x{value:X}";
}

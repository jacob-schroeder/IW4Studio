using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using MapConverter.Game.IW3.PC.Readers;
using ShaderConvert.Game.IW3.PC.Shaders;

namespace MapConverter.Game.IW3.PC.Assets;

internal static class Iw3PcShaderArgumentReader
{
    private static readonly string[] Constants =
    [
        "lightPosition", "lightDiffuse", "lightSpecular", "lightSpotDir", "lightSpotFactors",
        "nearPlaneOrg", "nearPlaneDx", "nearPlaneDy", "shadowParms", "shadowmapPolygonOffset",
        "renderTargetSize", "lightFalloffPlacement", "dofEquationViewModelAndFarBlur",
        "dofEquationScene", "dofLerpScale", "dofLerpBias", "dofRowDelta", "particleCloudColor",
        "gameTime", "pixelCostFracs", "pixelCostDecode", "filterTap[0]", "filterTap[1]",
        "filterTap[2]", "filterTap[3]", "filterTap[4]", "filterTap[5]", "filterTap[6]",
        "filterTap[7]", "colorMatrixR", "colorMatrixG", "colorMatrixB",
        "shadowmapSwitchPartition", "shadowmapScale", "zNear", "sunPosition", "sunDiffuse",
        "sunSpecular", "lightingLookupScale", "debugBumpmap", "materialColor", "fogConsts",
        "fogColor", "glowSetup", "glowApply", "colorBias", "colorTintBase", "colorTintDelta",
        "outdoorFeatherParms", "envMapParms", "spotShadowmapPixelAdjust", "clipSpaceLookupScale",
        "clipSpaceLookupOffset", "particleCloudMatrix", "depthFromClip", "codeMeshArg[0]",
        "codeMeshArg[1]", "baseLightingCoords", "worldMatrix", "inverseWorldMatrix",
        "transposeWorldMatrix", "inverseTransposeWorldMatrix", "viewMatrix", "inverseViewMatrix",
        "transposeViewMatrix", "inverseTransposeViewMatrix", "projectionMatrix", "inverseProjectionMatrix",
        "transposeProjectionMatrix", "inverseTransposeProjectionMatrix", "worldViewMatrix",
        "inverseWorldViewMatrix", "transposeWorldViewMatrix", "inverseTransposeWorldViewMatrix",
        "viewProjectionMatrix", "inverseViewProjectionMatrix", "transposeViewProjectionMatrix",
        "inverseTransposeViewProjectionMatrix", "worldViewProjectionMatrix",
        "inverseWorldViewProjectionMatrix", "transposeWorldViewProjectionMatrix",
        "inverseTransposeWorldViewProjectionMatrix", "shadowLookupMatrix",
        "inverseShadowLookupMatrix", "transposeShadowLookupMatrix", "inverseTransposeShadowLookupMatrix",
        "worldOutdoorLookupMatrix", "inverseWorldOutdoorLookupMatrix",
        "transposeWorldOutdoorLookupMatrix", "inverseTransposeWorldOutdoorLookupMatrix"
    ];

    private static readonly string[] Samplers =
    [
        "black", "white", "identityNormalMap", "modelLightingSampler", "lightmapSamplerPrimary",
        "lightmapSamplerSecondary", "shadowCookieSampler", "shadowmapSamplerSun", "shadowmapSamplerSpot",
        "feedbackSampler", "resolvedPostSun", "resolvedScene", "postEffect0", "postEffect1", "sky",
        "attenuationSampler", "dynamicShadowSampler", "outdoor", "floatZSampler",
        "processedFloatZSampler", "rawFloatZSampler", "caseTexture", "cinematicYSampler",
        "cinematicCrSampler", "cinematicCbSampler", "cinematicASampler", "reflectionProbeSampler"
    ];

    private static readonly string[] RouteSources =
        ["position", "color", "texcoord[0]", "normal", "tangent", "texcoord[1]", "texcoord[2]",
            "normalTransform[0]", "normalTransform[1]"];
    private static readonly string[] RouteDestinations =
        ["position", "normal", "color[0]", "color[1]", "texcoord[0]", "texcoord[1]",
            "texcoord[2]", "texcoord[3]", "texcoord[4]", "texcoord[5]", "texcoord[6]", "texcoord[7]"];

    internal static string ReadArguments(Iw3PcZone zone, int pass, int shader, bool vertex)
    {
        int count = zone.Bytes(pass + 12, 3).ToArray().Sum(value => value);
        if (count == 0) return string.Empty;
        int table = zone.Pointer(pass + 16);
        if (table == 0) throw new InvalidDataException("IW3 technique pass has no shader arguments.");
        int words = U16(zone.Bytes(shader + 12, 2));
        int program = zone.Pointer(shader + 8);
        IReadOnlyList<Iw3PcD3d9Constant> constants = Iw3PcD3d9ConstantTable.Read(
            zone.Bytes(program, checked(words * 4)), zone.String(shader));
        var text = new StringBuilder();
        for (int i = 0; i < count; i++)
        {
            int arg = table + i * 8;
            int type = U16(zone.Bytes(arg, 2));
            if (vertex != (type is 0 or 1 or 3)) continue;
            bool sampler = type is 2 or 4;
            int register = U16(zone.Bytes(arg + 2, 2));
            Iw3PcD3d9Constant? found = constants.Where(value =>
                    value.RegisterSet == (sampler ? 3 : 2) && value.RegisterIndex <= register &&
                    register < value.RegisterIndex + value.RegisterCount)
                .FirstOrDefault();
            if (found is null)
                throw new NotSupportedException($"IW3 shader has no CTAB parameter for register {register}.");
            Iw3PcD3d9Constant target = found;
            // Source emission keeps OAT's per-register array accessor. A set
            // selected for conversion is validated separately by the compiler.
            string destination = target.Elements > 1
                ? target.Name + "[" + (register - target.RegisterIndex).ToString(CultureInfo.InvariantCulture) + "]"
                : target.Name;
            string value = type switch
            {
                0 or 2 or 6 => "material.#0x" + U32(zone.Bytes(arg + 4, 4)).ToString("x", CultureInfo.InvariantCulture),
                1 or 7 => Literal(zone, arg),
                3 or 5 => CodeConstant(zone, arg, target),
                4 => "sampler." + Indexed(Samplers, checked((int)U32(zone.Bytes(arg + 4, 4)))),
                _ => throw new NotSupportedException($"Unsupported IW3 shader argument type {type}.")
            };
            if (value.StartsWith("constant.", StringComparison.Ordinal) && destination == value[9..] ||
                value.StartsWith("sampler.", StringComparison.Ordinal) && destination == value[8..])
                continue;
            text.Append("    ").Append(destination).Append(" = ").Append(value).Append(";\n");
        }
        return text.ToString();
    }

    internal static string ReadRouting(Iw3PcZone zone, int pass)
    {
        int declaration = zone.Pointer(pass);
        if (declaration == 0) return string.Empty;
        int count = zone.Bytes(declaration, 1)[0];
        if (count > 16) throw new InvalidDataException("IW3 technique has too many vertex routes.");
        var text = new StringBuilder();
        for (int i = 0; i < count; i++)
        {
            int row = declaration + 4 + i * 2;
            int source = zone.Bytes(row, 1)[0], destination = zone.Bytes(row + 1, 1)[0];
            text.Append("  vertex.").Append(Indexed(RouteDestinations, destination))
                .Append(" = code.").Append(Indexed(RouteSources, source)).Append(";\n");
        }
        return text.ToString();
    }

    private static string CodeConstant(Iw3PcZone zone, int arg, Iw3PcD3d9Constant target)
    {
        int index = U16(zone.Bytes(arg + 4, 2));
        int firstRow = zone.Bytes(arg + 6, 1)[0];
        int rowCount = zone.Bytes(arg + 7, 1)[0];
        if (target.Class == 3 && index >= 0x3a && index <= 0x59)
            index = 0x3a + ((index - 0x3a) ^ 2); // Matrix groups start at 0x3a, not a four-aligned index.
        string value = "constant." + Indexed(Constants, index);
        int impliedRows = target.Class is 2 or 3 ? target.RegisterCount : 1;
        // OAT's normal syntax carries the default rows implicitly. Keep
        // nondefault serialized slices for the selected-set compiler to reject
        // explicitly rather than silently lowering a different binding.
        return firstRow == 0 && rowCount == impliedRows ? value :
            value + " rows(" + firstRow.ToString(CultureInfo.InvariantCulture) + ", " +
            rowCount.ToString(CultureInfo.InvariantCulture) + ")";
    }

    private static string Literal(Iw3PcZone zone, int arg)
    {
        int offset = zone.Pointer(arg + 4);
        if (offset == 0) throw new InvalidDataException("IW3 technique literal is absent.");
        return "float4(" + string.Join(", ", Enumerable.Range(0, 4).Select(i =>
            BinaryPrimitives.ReadSingleLittleEndian(zone.Bytes(offset + i * 4, 4))
                .ToString("R", CultureInfo.InvariantCulture))) + ")";
    }

    private static string Indexed(string[] values, int index) =>
        index >= 0 && index < values.Length ? values[index] :
            throw new NotSupportedException($"Unsupported IW3 shader binding source {index}.");
    private static int U16(ReadOnlySpan<byte> data) => BinaryPrimitives.ReadUInt16LittleEndian(data);
    private static uint U32(ReadOnlySpan<byte> data) => BinaryPrimitives.ReadUInt32LittleEndian(data);
}

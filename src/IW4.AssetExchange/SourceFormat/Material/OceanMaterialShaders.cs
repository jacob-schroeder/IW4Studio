using System.Numerics;
using IW4.Assets.Assets.TechniqueSet;

namespace IW4.AssetExchange.SourceFormat.Material;

internal static class OceanMaterialShaders
{
    private static readonly MaterialShaderAsset Vertex = Load("ocean.vert.cg", MaterialShaderKind.Vertex);
    private static readonly MaterialShaderAsset Pixel = Load("ocean.frag.cg", MaterialShaderKind.Pixel);
    private static readonly MaterialShaderAsset SunPixel = Load("ocean-sun.frag.cg", MaterialShaderKind.Pixel);

    internal static MaterialTechniqueSetAsset Create(MaterialTechniqueSetAsset source, WaterMaterialDefinition definition)
    {
        OceanWaveSettings? ocean = definition.Ocean;
        var routing = new MaterialVertexStreamRouting[MaterialVertexDeclarationAsset.RoutingCount];
        routing[0] = new(MaterialStreamSource.Position, MaterialStreamDestination.Position);
        routing[1] = new(MaterialStreamSource.Color, MaterialStreamDestination.Color0);
        routing[2] = new(MaterialStreamSource.TexCoord0, MaterialStreamDestination.TexCoord0);
        // The native wc_water declaration. Color holds the static envelope, not a tint.
        var declaration = new MaterialVertexDeclarationAsset { StreamCount = 3, HasOptionalSourceRaw = 1, Routing = routing };
        // Flat water shares the corrected underside shading with zero displacement.
        var (first, second) = ocean?.GetWaves() ?? (Vector4.Zero, Vector4.Zero);
        MaterialTechniqueAsset noSun = CreateTechnique(false);
        MaterialTechniqueAsset sun = CreateTechnique(true);
        return new MaterialTechniqueSetAsset
        {
            Name = "iw4r_ocean_" + definition.Name[(definition.Name.LastIndexOf('_') + 1)..],
            WorldVertexFormat = source.WorldVertexFormat,
            TechniqueSlots = source.TechniqueSlots.Select(slot => new MaterialTechniqueSlot(slot.Type, default,
                slot.Type == MaterialTechniqueType.Unlit ? noSun :
                slot.Type is >= MaterialTechniqueType.Lit and <= MaterialTechniqueType.LitInstancedSunDfog
                    ? slot.Type is MaterialTechniqueType.LitSun or MaterialTechniqueType.LitSunDfog or
                        MaterialTechniqueType.LitSunShadow or MaterialTechniqueType.LitSunShadowDfog or
                        MaterialTechniqueType.LitInstancedSun or MaterialTechniqueType.LitInstancedSunDfog ? sun : noSun
                    : slot.Technique)).ToArray()
        };

        MaterialTechniqueAsset CreateTechnique(bool sunlight)
        {
            MaterialTechniqueType type = sunlight ? MaterialTechniqueType.LitSun : MaterialTechniqueType.Lit;
            MaterialTechniqueAsset original = source.TechniqueSlots.Single(slot => slot.Type == type).Technique ??
                throw new InvalidDataException($"Source water has no {type} technique.");
            if (original.Passes.Count != 1 || original.Passes[0].CustomSamplerFlags != MaterialCustomSamplerFlags.ReflectionProbe)
                throw new NotSupportedException("Ocean waves require the native single-pass reflection-probe water technique.");
            // PS destinations are Cg parameter ordinals; VS destinations are registers.
            var stable = new List<MaterialShaderArgumentAsset>
            {
                Literal(9, first), Literal(10, second), Literal(11, new(ocean is null ? 0 : 1 / ocean.FadeWidth, 0, 0, 0)),
                new(0, MaterialShaderArgumentType.MaterialPixelSampler, 5, unchecked((int)MaterialExchange.HashSourcePropertyName("normalMap")), null),
                Code(MaterialShaderArgumentType.CodeVertexConst, 8, MaterialConstantSource.GameTime),
                Code(MaterialShaderArgumentType.CodeVertexConst, 21, MaterialConstantSource.Fog),
                Code(MaterialShaderArgumentType.CodePixelConst, (ushort)(sunlight ? 5 : 3), MaterialConstantSource.FogColorLinear),
                new(0, MaterialShaderArgumentType.MaterialPixelConst, (ushort)(sunlight ? 6 : 4), unchecked((int)MaterialExchange.HashSourcePropertyName("envMapParms")), null),
                new(0, MaterialShaderArgumentType.MaterialPixelConst, (ushort)(sunlight ? 9 : 7), unchecked((int)MaterialExchange.HashSourcePropertyName("waterColor")), null)
            };
            if (sunlight)
            {
                stable.Add(Code(MaterialShaderArgumentType.CodePixelConst, 3, MaterialConstantSource.LightPosition));
                stable.Add(Code(MaterialShaderArgumentType.CodePixelConst, 4, MaterialConstantSource.LightSpecular));
            }
            return new MaterialTechniqueAsset
            {
                Name = sunlight ? "iw4r_ocean_sun" : "iw4r_ocean",
                Flags = original.Flags,
                PassCount = 1,
                Passes = [new MaterialPassAsset
                {
                    VertexDeclaration = declaration,
                    VertexShader = Vertex,
                    PixelShader = sunlight ? SunPixel : Pixel,
                    PerPrimArgCount = 1,
                    PerObjArgCount = 1,
                    StableArgCount = checked((byte)stable.Count),
                    CustomSamplerFlags = MaterialCustomSamplerFlags.ReflectionProbe,
                    PrecompiledVertexShader = MaterialPrecompiledVertexShader.None,
                    Args = [Code(MaterialShaderArgumentType.CodeVertexConst, 4, MaterialConstantSource.WorldMatrix0, 4),
                        Code(MaterialShaderArgumentType.CodeVertexConst, 0, MaterialConstantSource.ViewProjectionMatrix, 4),
                        .. stable.OrderBy(argument => argument.Type).ThenBy(argument => argument.Dest)]
                }]
            };
        }
    }

    private static MaterialShaderArgumentAsset Code(MaterialShaderArgumentType type, ushort destination,
        MaterialConstantSource source, byte rows = 1) => new(0, type, destination, new MaterialCodeConstantArgument(source, 0, rows).Raw, null);

    private static MaterialShaderArgumentAsset Literal(ushort destination, Vector4 value) =>
        new(0, MaterialShaderArgumentType.LiteralVertexConst, destination, 0, new(value.X, value.Y, value.Z, value.W));

    private static MaterialShaderAsset Load(string resource, MaterialShaderKind kind)
    {
        using Stream stream = typeof(OceanMaterialShaders).Assembly.GetManifestResourceStream("IW4.Ocean." + resource) ??
            throw new InvalidDataException($"Missing packaged ocean shader '{resource}'.");
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        byte[] data = bytes.ToArray();
        return new MaterialShaderAsset
        {
            Name = "iw4r_" + resource,
            Kind = kind,
            Data = data,
            DataSize = checked((uint)data.Length),
            ProgramBytes = new byte[MaterialShaderAsset.GetProgramByteCount(kind)]
        };
    }
}

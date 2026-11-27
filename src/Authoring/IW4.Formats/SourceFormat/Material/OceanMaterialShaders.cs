using System.Buffers.Binary;
using System.Text;
using IW4.Game.Assets.TechniqueSet;

namespace IW4.Formats.SourceFormat.Material;

internal static class OceanMaterialShaders
{
    private static readonly MaterialShaderAsset Vertex = Load("ocean.vert.cg", MaterialShaderKind.Vertex);
    private static readonly MaterialShaderAsset Pixel = Load("ocean.frag.cg", MaterialShaderKind.Pixel);
    private static readonly MaterialShaderAsset SunPixel = Load("ocean-sun.frag.cg", MaterialShaderKind.Pixel);
    private static readonly MaterialShaderAsset FlatPixel = Load("water.frag.cg", MaterialShaderKind.Pixel);
    private static readonly MaterialShaderAsset FlatSunPixel = Load("water-sun.frag.cg", MaterialShaderKind.Pixel);
    private static readonly IReadOnlyDictionary<string, ushort> PixelConstants = ReadPixelConstants(Pixel);
    private static readonly IReadOnlyDictionary<string, ushort> SunPixelConstants = ReadPixelConstants(SunPixel);

    internal static MaterialTechniqueSetAsset Create(MaterialTechniqueSetAsset source, WaterMaterialDefinition definition)
    {
        var routing = new MaterialVertexStreamRouting[MaterialVertexDeclarationAsset.RoutingCount];
        routing[0] = new(MaterialStreamSource.Position, MaterialStreamDestination.Position);
        routing[1] = new(MaterialStreamSource.Color, MaterialStreamDestination.Color0);
        routing[2] = new(MaterialStreamSource.TexCoord0, MaterialStreamDestination.TexCoord0);
        // The native wc_water declaration: RGB is the wave envelope/gradient;
        // ocean alpha is signed seabed depth, flat-water alpha is contact distance.
        var declaration = new MaterialVertexDeclarationAsset { StreamCount = 3, HasOptionalSourceRaw = 1, Routing = routing };
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
                // SPU draw path does not relocate literal vertex pointers, so use material constants.
                new(0, MaterialShaderArgumentType.MaterialVertexConst, 9,
                    unchecked((int)MaterialExchange.HashSourcePropertyName("oceanShape")), null),
                new(0, MaterialShaderArgumentType.MaterialVertexConst, 10,
                    unchecked((int)MaterialExchange.HashSourcePropertyName("oceanMotion")), null),
                new(0, MaterialShaderArgumentType.MaterialPixelSampler, 5, unchecked((int)MaterialExchange.HashSourcePropertyName("normalMap")), null),
                Code(MaterialShaderArgumentType.CodeVertexConst, 8, MaterialConstantSource.GameTime),
                Code(MaterialShaderArgumentType.CodeVertexConst, 21, MaterialConstantSource.Fog)
            };
            bool ocean = definition.Ocean is not null;
            if (ocean)
            {
                // Pixel numeric destinations are ordinals in the selected Cg parameter table.
                IReadOnlyDictionary<string, ushort> pixelConstants = sunlight ? SunPixelConstants : PixelConstants;
                ushort Ordinal(string name) => pixelConstants.TryGetValue(name, out ushort ordinal) ? ordinal
                    : throw new InvalidDataException($"Ocean pixel shader '{(sunlight ? SunPixel : Pixel).Name}' has no patchable uniform '{name}'.");

                stable.Add(new(0, MaterialShaderArgumentType.MaterialPixelConst, Ordinal("envMapParms"),
                    unchecked((int)MaterialExchange.HashSourcePropertyName("envMapParms")), null));
                stable.Add(new(0, MaterialShaderArgumentType.MaterialPixelSampler, 6,
                    unchecked((int)MaterialExchange.HashSourcePropertyName("foamMap")), null));
                stable.Add(Code(MaterialShaderArgumentType.CodePixelConst, Ordinal("gameTime"), MaterialConstantSource.GameTime));
                stable.Add(Code(MaterialShaderArgumentType.CodePixelConst, Ordinal("oceanFogColor"), MaterialConstantSource.FogColorLinear));
                stable.Add(new(0, MaterialShaderArgumentType.MaterialPixelConst, Ordinal("oceanShape"),
                    unchecked((int)MaterialExchange.HashSourcePropertyName("oceanShape")), null));
                stable.Add(new(0, MaterialShaderArgumentType.MaterialPixelConst, Ordinal("waterColor"),
                    unchecked((int)MaterialExchange.HashSourcePropertyName("waterColor")), null));
                if (sunlight)
                {
                    stable.Add(Code(MaterialShaderArgumentType.CodePixelConst, Ordinal("oceanSunColor"), MaterialConstantSource.LightSpecular));
                    stable.Add(Code(MaterialShaderArgumentType.CodePixelConst, Ordinal("oceanSunDirection"), MaterialConstantSource.LightPosition));
                }
            }
            else
            {
                stable.Add(Code(MaterialShaderArgumentType.CodePixelConst, (ushort)(sunlight ? 5 : 3), MaterialConstantSource.FogColorLinear));
                stable.Add(new(0, MaterialShaderArgumentType.MaterialPixelConst, (ushort)(sunlight ? 6 : 4),
                    unchecked((int)MaterialExchange.HashSourcePropertyName("envMapParms")), null));
                stable.Add(new(0, MaterialShaderArgumentType.MaterialPixelConst, (ushort)(sunlight ? 9 : 7),
                    unchecked((int)MaterialExchange.HashSourcePropertyName("waterColor")), null));
                if (sunlight)
                {
                    stable.Add(Code(MaterialShaderArgumentType.CodePixelConst, 3, MaterialConstantSource.LightPosition));
                    stable.Add(Code(MaterialShaderArgumentType.CodePixelConst, 4, MaterialConstantSource.LightSpecular));
                }
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
                    PixelShader = ocean ? sunlight ? SunPixel : Pixel : sunlight ? FlatSunPixel : FlatPixel,
                    PerPrimArgCount = 1,
                    PerObjArgCount = 1,
                    StableArgCount = checked((byte)stable.Count),
                    CustomSamplerFlags = MaterialCustomSamplerFlags.ReflectionProbe,
                    PrecompiledVertexShader = MaterialPrecompiledVertexShader.None,
                    // Native stored world rows and transposed view-projection rows.
                    Args = [Code(MaterialShaderArgumentType.CodeVertexConst, 4, MaterialConstantSource.WorldMatrix0, 4),
                        Code(MaterialShaderArgumentType.CodeVertexConst, 0, MaterialConstantSource.TransposeViewProjectionMatrix, 4),
                        .. stable.OrderBy(argument => argument.Type).ThenBy(argument => argument.Dest)]
                }]
            };
        }
    }

    private static MaterialShaderArgumentAsset Code(MaterialShaderArgumentType type, ushort destination,
        MaterialConstantSource source, byte rows = 1) => new(0, type, destination, new MaterialCodeConstantArgument(source, 0, rows).Raw, null);

    private static IReadOnlyDictionary<string, ushort> ReadPixelConstants(MaterialShaderAsset shader)
    {
        ReadOnlySpan<byte> program = shader.Data ?? throw new InvalidDataException($"Missing packaged ocean shader '{shader.Name}'.");
        if (program.Length < 0x20 || Word(program, 0) != 7004 || Word(program, 4) != 6 || Word(program, 8) != program.Length)
            throw new InvalidDataException($"Packaged ocean shader '{shader.Name}' has an invalid pixel Cg header.");

        uint count = Word(program, 12);
        uint table = Word(program, 16);
        if (table < 0x20 || table > program.Length || count > ushort.MaxValue || count > (program.Length - table) / 0x30)
            throw new InvalidDataException($"Packaged ocean shader '{shader.Name}' has an invalid Cg parameter table.");

        var ordinals = new Dictionary<string, ushort>(StringComparer.Ordinal);
        for (uint ordinal = 0; ordinal < count; ordinal++)
        {
            int entry = checked((int)(table + ordinal * 0x30));
            // Match referenced half/float input uniforms that the RSX constant patcher can update.
            uint type = Word(program, entry);
            if (type is not (>= 0x401 and <= 0x404 or >= 0x415 and <= 0x418) ||
                Word(program, entry + 4) != 0x882 || Word(program, entry + 8) != 0x1006 ||
                Word(program, entry + 24) == 0 || Word(program, entry + 32) != 0x1001 || Word(program, entry + 40) == 0)
                continue;

            uint nameOffset = Word(program, entry + 16);
            if (nameOffset == 0 || nameOffset >= program.Length)
                throw new InvalidDataException($"Packaged ocean shader '{shader.Name}' has an invalid Cg parameter name.");
            ReadOnlySpan<byte> nameBytes = program[(int)nameOffset..];
            int terminator = nameBytes.IndexOf((byte)0);
            if (terminator < 0)
                throw new InvalidDataException($"Packaged ocean shader '{shader.Name}' has an unterminated Cg parameter name.");
            string name = Encoding.Latin1.GetString(nameBytes[..terminator]);
            if (!ordinals.TryAdd(name, (ushort)ordinal))
                throw new InvalidDataException($"Packaged ocean shader '{shader.Name}' has duplicate Cg parameter '{name}'.");
        }
        return ordinals;
    }

    private static uint Word(ReadOnlySpan<byte> program, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(program.Slice(offset, 4));

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
            LoadDefTail = new byte[MaterialShaderAsset.GetLoadDefTailSize(kind)]
        };
    }
}

using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using IW4.Assets.Assets.TechniqueSet;
using IW4.FastFiles.Pointers;
using MapConverter.Game.IW3.PC.Techniques;

namespace MapConverter.Game.IW3.PC.Shaders;

/// <summary>
/// Lowers a recovered IW3 Direct3D 9 shader to RSX instructions without
/// copying a target-game shader payload. OpenAssetTools is only the extractor;
/// this compiler consumes its recovered Direct3D bytecode as source input.
/// </summary>
internal sealed class Iw3PcShaderCompiler
{
    private const uint CgVertexProfile = 7003;
    private const uint CgPixelProfile = 7004;
    private const uint CgConstantRegister = 0x882;
    private const uint CgTextureUnitBase = 0x800;
    private const uint CgUniformVariability = 0x1006;
    private const uint CgInDirection = 0x1001;
    private const uint CgFloat4 = 0x418;
    private const uint CgSampler1D = 0x429;
    private const uint CgSampler2D = 0x42A;
    private const uint CgSampler3D = 0x42B;
    private const uint CgSamplerCube = 0x42D;
    private const ushort Direct3DFloat = 3;
    private const ushort Direct3DSampler1D = 11;
    private const ushort Direct3DSampler2D = 12;
    private const ushort Direct3DSampler3D = 13;
    private const ushort Direct3DSamplerCube = 14;
    private const int CgHeaderSize = 0x20;
    private const int CgParameterSize = 0x30;
    private const int CgDescriptorSize = 0x18;
    private const int RsxVertexHeaderSize = 0x24;
    private const int RsxFragmentHeaderSize = 0x30;

    internal Iw3ShaderCompilation Compile(
        Iw3ShaderSource source,
        string shaderProgramPath)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(shaderProgramPath);

        string fullPath = Path.GetFullPath(shaderProgramPath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("The extracted shader program was not found.", fullPath);

        byte[] direct3D = ExtractSingleDirect3DProgram(File.ReadAllBytes(fullPath), fullPath);
        ValidateDirect3DStage(direct3D, source.Stage, fullPath);
        IReadOnlyList<Direct3DConstant> constants = ReadConstantTable(direct3D, fullPath);
        ValidateSourceArguments(source, constants, fullPath);

        string scratchDirectory = Path.Combine(Path.GetTempPath(), "mapconverter-shaders", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratchDirectory);
        try
        {
            string inputPath = Path.Combine(scratchDirectory, "input.cso");
            string assemblyPath = Path.Combine(scratchDirectory, "program.asm");
            string binaryPath = Path.Combine(scratchDirectory, "program.rsx");
            File.WriteAllBytes(inputPath, direct3D);

            string mojoProfile = TranslateWithMojoShader(inputPath, assemblyPath, source.Stage, fullPath);
            NormalizeAssembly(assemblyPath, source.Stage);
            Assemble(assemblyPath, binaryPath, source.Stage, fullPath);

            RsxProgram program = ReadRsxProgram(File.ReadAllBytes(binaryPath), source.Stage, fullPath);
            byte[] data = BuildCgProgram(source.Stage, constants, program, fullPath);
            MaterialShaderKind kind = source.Stage == Iw3ShaderStage.Vertex
                ? MaterialShaderKind.Vertex
                : MaterialShaderKind.Pixel;
            var asset = new MaterialShaderAsset
            {
                Kind = kind,
                Name = source.ProgramName,
                DataPointer = default,
                DataSize = checked((uint)data.Length),
                Data = data,
                ProgramBytes = kind == MaterialShaderKind.Pixel ? new byte[12] : []
            };
            return new Iw3ShaderCompilation(
                asset,
                constants.Select(CreateParameter).ToArray(),
                $"MojoShader ad5dff84830c2863c841f4b1f4e3df78c705b383 ({mojoProfile}) + PSL1GHT cgcomp f649a08fd536a9e27c08c7db2d93a2d7ee4c3bbe");
        }
        finally
        {
            if (Directory.Exists(scratchDirectory))
                Directory.Delete(scratchDirectory, recursive: true);
        }
    }

    private static string TranslateWithMojoShader(
        string inputPath,
        string assemblyPath,
        Iw3ShaderStage stage,
        string sourcePath)
    {
        string tool = FindNativeTool("mapconverter-mojoshader");
        string preferred = stage == Iw3ShaderStage.Vertex ? "nv3" : "nv3";
        if (Run(tool, [preferred, inputPath, assemblyPath], out string error))
            return preferred;

        // NV4 is needed by known IW3 pixel programs that contain TEXLDL.
        if (stage == Iw3ShaderStage.Pixel &&
            Run(tool, ["nv4", inputPath, assemblyPath], out string fallbackError))
        {
            return "nv4";
        }

        throw new InvalidDataException(
            $"Shader '{sourcePath}' could not be translated from Direct3D 9 bytecode. {error}");
    }

    private static void NormalizeAssembly(string assemblyPath, Iw3ShaderStage stage)
    {
        string assembly = File.ReadAllText(assemblyPath, Encoding.ASCII);
        string requiredHeader = stage == Iw3ShaderStage.Vertex ? "!!ARBvp1.0" : "!!ARBfp1.0";
        int firstLine = assembly.IndexOf('\n');
        if (firstLine < 0)
            throw new InvalidDataException("MojoShader produced assembly without a program header.");

        string header = assembly[..firstLine].TrimEnd('\r');
        bool validVertex = stage == Iw3ShaderStage.Vertex &&
            (header == "!!ARBvp1.0" || header == "!!NVvp3.0" || header == "!!NVvp4.0");
        bool validPixel = stage == Iw3ShaderStage.Pixel &&
            (header == "!!ARBfp1.0" || header == "!!NVfp3.0" || header == "!!NVfp4.0");
        if (!validVertex && !validPixel)
            throw new InvalidDataException($"MojoShader produced unsupported assembly header '{header}'.");

        // cgcomp assembles the shared ARB syntax and accepts the NV extension
        // instructions generated by MojoShader; only the profile declaration differs.
        assembly = requiredHeader + assembly[firstLine..];
        assembly = NormalizeCgCompSyntax(assembly, stage);
        File.WriteAllText(assemblyPath, assembly, Encoding.ASCII);
    }

    private static string NormalizeCgCompSyntax(string assembly, Iw3ShaderStage stage)
    {
        var inputAliases = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match attribute in Regex.Matches(
                     assembly,
                     @"(?m)^ATTRIB\s+(v\d+)\s*=\s*([^;]+);\s*$"))
        {
            string target = attribute.Groups[2].Value;
            string expectedPrefix = stage == Iw3ShaderStage.Vertex ? "vertex." : "fragment.";
            if (!target.StartsWith(expectedPrefix, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"MojoShader emitted unsupported {stage} input alias '{attribute.Value}'.");
            }
            inputAliases.Add(
                attribute.Groups[1].Value,
                stage == Iw3ShaderStage.Vertex ? $"v[{target}]" : $"f[{target}]");
        }

        assembly = Regex.Replace(assembly, @"(?m)^ATTRIB\s+v\d+\s*=\s*[^;]+;\s*$", string.Empty);
        foreach ((string alias, string target) in inputAliases)
            assembly = Regex.Replace(assembly, $@"\b{Regex.Escape(alias)}\b", target);

        int nextTemporary = 0;
        var temporaryAliases = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match temporary in Regex.Matches(
                     assembly,
                     @"(?m)^(?:FLOAT\s+)?TEMP\s+([A-Za-z_]\w*)\s*;\s*$"))
        {
            string alias = temporary.Groups[1].Value;
            if (alias.Length > 1 && alias[0] == 'r' &&
                int.TryParse(alias.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int index))
            {
                temporaryAliases.Add(alias, $"R{index}");
                nextTemporary = Math.Max(nextTemporary, checked(index + 1));
            }
        }
        foreach (Match temporary in Regex.Matches(
                     assembly,
                     @"(?m)^(?:FLOAT\s+)?TEMP\s+([A-Za-z_]\w*)\s*;\s*$"))
        {
            string alias = temporary.Groups[1].Value;
            if (!temporaryAliases.ContainsKey(alias))
                temporaryAliases.Add(alias, $"R{nextTemporary++}");
        }
        foreach ((string alias, string target) in temporaryAliases)
            assembly = Regex.Replace(assembly, $@"\b{Regex.Escape(alias)}\b", target);

        assembly = Regex.Replace(
            assembly,
            @"(?m)^PARAM\s+c(\d+)\s*=\s*\{\s*([^}]*)\s*\};\s*$",
            match => $"#const c[{match.Groups[1].Value}] = {NormalizeLiteralVector(match.Groups[2].Value)}");
        assembly = Regex.Replace(
            assembly,
            @"(?m)^PARAM\s+c(\d+)\s*=\s*program\.local\[(\d+)\];\s*$",
            string.Empty);

        var integerAliases = new Dictionary<string, string>(StringComparer.Ordinal);
        int nextIntegerConstant = 255;
        foreach (Match integer in Regex.Matches(
                     assembly,
                     @"(?m)^PARAM\s+(i\d+)\s*=\s*\{\s*([^}]*)\s*\};\s*$"))
        {
            if (nextIntegerConstant < 0)
                throw new InvalidDataException("The shader contains more integer constants than the RSX assembly lowering supports.");
            string constant = $"c[{nextIntegerConstant--}]";
            integerAliases.Add(integer.Groups[1].Value, constant);
            assembly = assembly.Replace(
                integer.Value,
                $"#const {constant} = {NormalizeLiteralVector(integer.Groups[2].Value)}",
                StringComparison.Ordinal);
        }
        foreach ((string alias, string constant) in integerAliases)
            assembly = Regex.Replace(assembly, $@"\b{Regex.Escape(alias)}\b", constant);

        assembly = Regex.Replace(assembly, @"\bc(\d+)\b", "c[$1]");
        assembly = Regex.Replace(
            assembly,
            @"\{\s*(c\[\d+\])\.([xyzw])\s*,\s*\1\.([xyzw])\s*,\s*\1\.([xyzw])\s*,\s*\1\.([xyzw])\s*\}",
            match => $"{match.Groups[1].Value}.{match.Groups[2].Value}{match.Groups[3].Value}{match.Groups[4].Value}{match.Groups[5].Value}");
        if (Regex.IsMatch(assembly, @"\b(?:c|i|v|r)\d+\b"))
        {
            throw new InvalidDataException(
                "MojoShader emitted a register alias that the bundled RSX assembler cannot represent safely.");
        }

        // The bundled parser does not skip empty lines after its program header.
        return Regex.Replace(assembly, @"(?m)^\s*\r?\n", string.Empty);
    }

    private static string NormalizeLiteralVector(string values)
    {
        string[] components = values.Split(',', StringSplitOptions.TrimEntries);
        if (components.Length != 4 || components.Any(component =>
                !float.TryParse(component, NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
        {
            throw new InvalidDataException($"MojoShader emitted an unsupported literal vector '{{ {values} }}'.");
        }
        return string.Join(' ', components);
    }

    private static void Assemble(string assemblyPath, string binaryPath, Iw3ShaderStage stage, string sourcePath)
    {
        string tool = FindNativeTool("mapconverter-cgcomp");
        string flag = stage == Iw3ShaderStage.Vertex ? "-v" : "-f";
        if (!Run(tool, [flag, "-a", assemblyPath, binaryPath], out string error))
        {
            throw new InvalidDataException(
                $"Shader '{sourcePath}' could not be assembled for RSX. {error}");
        }
    }

    private static bool Run(string executable, IReadOnlyList<string> arguments, out string error)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            }
        };
        foreach (string argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        string standardError = process.StandardError.ReadToEnd();
        string standardOutput = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        error = string.Join(Environment.NewLine, new[] { standardError, standardOutput }
            .Where(value => !string.IsNullOrWhiteSpace(value))).Trim();
        if (process.ExitCode != 0 && string.IsNullOrEmpty(error))
            error = $"The local shader tool exited with code {process.ExitCode}.";
        return process.ExitCode == 0;
    }

    private static string FindNativeTool(string name)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Native", name);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"The local MapConverter shader tool '{name}' is missing at '{path}'. Build MapConverter before running it.");
        }
        return path;
    }

    private static byte[] ExtractSingleDirect3DProgram(ReadOnlySpan<byte> source, string path)
    {
        if (source.Length < 8 || (source.Length & 3) != 0)
            throw new InvalidDataException($"Shader '{path}' is not dword-aligned Direct3D bytecode.");

        uint version = ReadLittleEndian(source, 0);
        uint stage = version >> 16;
        int major = (int)((version >> 8) & 0xff);
        if (stage is not (0xfffeu or 0xffffu) || major is < 1 or > 3)
        {
            throw new InvalidDataException(
                $"Shader '{path}' has unsupported Direct3D shader model token 0x{version:X8}; supported inputs are SM1/SM2/SM3.");
        }

        int endOffset = FindDirect3DEnd(source, path);
        return source[..endOffset].ToArray();
    }

    private static int FindDirect3DEnd(
        ReadOnlySpan<byte> source,
        string path)
    {
        for (int offset = 4; offset + 4 <= source.Length;)
        {
            uint token = ReadLittleEndian(source, offset);
            ushort opcode = unchecked((ushort)token);
            if (opcode == 0xffff)
                return checked(offset + 4);

            if (opcode == 0xfffe)
            {
                int payloadWords = checked((int)(token >> 16));
                int next = checked(offset + 4 + payloadWords * 4);
                if (next > source.Length)
                    break;
                offset = next;
                continue;
            }

            // The recovered source program ends at its first aligned END.
            // SM1 has no instruction-length field, while the known SM2/SM3
            // containers append opaque data after END; attempting to follow
            // their token lengths would treat this non-program data as code.
            // CTAB comments are explicitly skipped so their metadata cannot
            // provide a false terminator.
            offset += 4;
        }
        throw new InvalidDataException($"Shader '{path}' has no safely delimited Direct3D END token.");
    }

    private static void ValidateDirect3DStage(
        ReadOnlySpan<byte> program,
        Iw3ShaderStage expected,
        string path)
    {
        uint token = ReadLittleEndian(program, 0);
        bool vertex = token >> 16 == 0xfffe;
        if (vertex != (expected == Iw3ShaderStage.Vertex))
        {
            throw new InvalidDataException(
                $"Shader '{path}' has a Direct3D {(vertex ? "vertex" : "pixel")} stage token but is referenced as {expected}.");
        }
    }

    private static IReadOnlyList<Direct3DConstant> ReadConstantTable(ReadOnlySpan<byte> data, string path)
    {
        for (int offset = 4; offset + 8 <= data.Length; offset += 4)
        {
            uint token = ReadLittleEndian(data, offset);
            if ((token & 0xffff) != 0xfffe)
                continue;
            int byteLength = checked((int)(token >> 16) * 4);
            int payload = offset + 4;
            if (byteLength < 28 || payload + byteLength > data.Length ||
                !data.Slice(payload, 4).SequenceEqual("CTAB"u8))
            {
                continue;
            }
            return ParseConstantTable(data.Slice(payload, byteLength), path);
        }
        return [];
    }

    private static IReadOnlyList<Direct3DConstant> ParseConstantTable(ReadOnlySpan<byte> table, string path)
    {
        const int ctabSignatureSize = 4;
        if (ReadLittleEndian(table, 4) != 28)
            throw new InvalidDataException($"Shader '{path}' has an unsupported CTAB header.");
        int count = checked((int)ReadLittleEndian(table, 16));
        if (count == 0)
            return [];
        int infoOffset = checked(ctabSignatureSize + (int)ReadLittleEndian(table, 20));
        if (count > 256 || infoOffset < 28 || infoOffset + checked(count * 20) > table.Length)
            throw new InvalidDataException($"Shader '{path}' has an invalid CTAB constant table.");

        var result = new List<Direct3DConstant>(count);
        for (int index = 0; index < count; index++)
        {
            int entry = infoOffset + index * 20;
            string name = ReadCString(table, checked(ctabSignatureSize + (int)ReadLittleEndian(table, entry)), path);
            ushort registerSet = ReadLittleEndian16(table, entry + 4);
            ushort registerIndex = ReadLittleEndian16(table, entry + 6);
            ushort registerCount = ReadLittleEndian16(table, entry + 8);
            int typeOffset = checked(ctabSignatureSize + (int)ReadLittleEndian(table, entry + 12));
            if (registerCount == 0 || typeOffset < 0 || typeOffset + 16 > table.Length)
                throw new InvalidDataException($"Shader '{path}' has an invalid CTAB constant '{name}'.");
            ushort type = ReadLittleEndian16(table, typeOffset + 2);
            result.Add(new Direct3DConstant(name, registerSet, registerIndex, registerCount, type));
        }
        return result;
    }

    private static void ValidateSourceArguments(
        Iw3ShaderSource source,
        IReadOnlyList<Direct3DConstant> constants,
        string path)
    {
        foreach (Iw3ShaderArgumentSource argument in source.Arguments)
        {
            if (constants.Any(constant => string.Equals(constant.Name, argument.Destination.Name, StringComparison.Ordinal)))
                continue;
            throw new InvalidDataException(
                $"Shader '{path}' has no CTAB destination named '{argument.Destination.Name}'. Donor-free lowering will not guess shader argument registers.");
        }
    }

    private static RsxProgram ReadRsxProgram(ReadOnlySpan<byte> data, Iw3ShaderStage stage, string path)
    {
        bool vertex = stage == Iw3ShaderStage.Vertex;
        int headerSize = vertex ? RsxVertexHeaderSize : RsxFragmentHeaderSize;
        if (data.Length < headerSize)
            throw new InvalidDataException($"RSX compiler output for '{path}' is truncated.");
        string magic = Encoding.ASCII.GetString(data[..2]);
        if (magic != (vertex ? "VP" : "FP"))
            throw new InvalidDataException($"RSX compiler output for '{path}' has stage '{magic}', expected {(vertex ? "VP" : "FP")}.");
        int instructionCount = ReadBigEndian16(data, 10);
        int uploadOffset = checked((int)ReadBigEndian(data, 20));
        int uploadLength = checked(instructionCount * 16);
        if (instructionCount == 0 || uploadOffset < headerSize || uploadOffset + uploadLength > data.Length)
            throw new InvalidDataException($"RSX compiler output for '{path}' has an invalid instruction payload.");
        byte[] upload = data.Slice(uploadOffset, uploadLength).ToArray();

        if (vertex)
        {
            return new RsxProgram(
                instructionCount,
                ReadBigEndian16(data, 4),
                ReadBigEndian(data, 24),
                ReadBigEndian(data, 28),
                0,
                0,
                0,
                0,
                [],
                upload);
        }

        ushort texCoords = ReadBigEndian16(data, 28);
        ushort texCoords2D = ReadBigEndian16(data, 30);
        ushort texCoords3D = ReadBigEndian16(data, 32);
        if (((texCoords2D | texCoords3D) & ~texCoords) != 0 ||
            (texCoords2D & texCoords3D) != 0)
        {
            throw new InvalidDataException(
                $"RSX compiler output for '{path}' has contradictory texture-coordinate metadata.");
        }

        uint relocationCountValue = ReadBigEndian(data, 40);
        uint relocationOffsetValue = ReadBigEndian(data, 44);
        if (relocationCountValue > int.MaxValue || relocationOffsetValue > int.MaxValue)
            throw new InvalidDataException($"RSX compiler output for '{path}' has an invalid relocation table.");
        int relocationCount = (int)relocationCountValue;
        int relocationOffset = (int)relocationOffsetValue;
        if (relocationCount > data.Length / 8)
            throw new InvalidDataException($"RSX compiler output for '{path}' has an invalid relocation table.");
        int relocationBytes = relocationCount * 8;
        if (relocationOffset < RsxFragmentHeaderSize ||
            relocationOffset > data.Length - relocationBytes)
        {
            throw new InvalidDataException($"RSX compiler output for '{path}' has an invalid relocation table.");
        }

        var relocations = new RsxFragmentRelocation[relocationCount];
        for (int index = 0; index < relocations.Length; index++)
        {
            int entry = relocationOffset + index * 8;
            uint constantRegister = ReadBigEndian(data, entry);
            uint constantOffset = ReadBigEndian(data, entry + 4);
            if (constantOffset > ushort.MaxValue ||
                (constantOffset & 0xf) != 0 ||
                (ulong)constantOffset + 16 > (ulong)uploadLength)
            {
                throw new InvalidDataException(
                    $"RSX compiler output for '{path}' has an invalid fragment constant relocation.");
            }
            relocations[index] = new RsxFragmentRelocation(constantRegister, constantOffset);
        }

        return new RsxProgram(
            instructionCount,
            ReadBigEndian16(data, 4),
            ReadBigEndian(data, 36),
            0,
            ReadBigEndian(data, 24),
            texCoords,
            texCoords2D,
            texCoords3D,
            relocations,
            upload);
    }

    private static byte[] BuildCgProgram(
        Iw3ShaderStage stage,
        IReadOnlyList<Direct3DConstant> constants,
        RsxProgram rsx,
        string path)
    {
        if (rsx.Upload.Length > ushort.MaxValue)
            throw new InvalidDataException($"Shader '{path}' exceeds IW4's supported RSX program size.");

        IReadOnlyList<uint>[] patchLists = BuildFragmentPatchLists(stage, constants, rsx, path);
        int parameterOffset = CgHeaderSize;
        int cursor = checked(parameterOffset + constants.Count * CgParameterSize);
        var patchOffsets = new int[constants.Count];
        for (int index = 0; index < patchLists.Length; index++)
        {
            if (patchLists[index].Count == 0)
                continue;
            patchOffsets[index] = cursor;
            cursor = checked(cursor + sizeof(uint) + patchLists[index].Count * sizeof(uint));
        }

        var names = new int[constants.Count];
        foreach ((Direct3DConstant constant, int index) in constants.Select((value, index) => (value, index)))
        {
            names[index] = cursor;
            cursor = checked(cursor + Encoding.ASCII.GetByteCount(constant.Name) + 1);
        }
        cursor = Align(cursor, 16);
        int descriptorOffset = cursor;
        cursor = checked(cursor + CgDescriptorSize);
        int uploadOffset = Align(cursor, 16);
        int totalSize = checked(uploadOffset + rsx.Upload.Length);
        var data = new byte[totalSize];
        WriteBigEndian(data, 0, stage == Iw3ShaderStage.Vertex ? CgVertexProfile : CgPixelProfile);
        WriteBigEndian(data, 4, 6);
        WriteBigEndian(data, 8, checked((uint)totalSize));
        WriteBigEndian(data, 12, checked((uint)constants.Count));
        WriteBigEndian(data, 16, checked((uint)parameterOffset));
        WriteBigEndian(data, 20, checked((uint)descriptorOffset));
        WriteBigEndian(data, 24, checked((uint)rsx.Upload.Length));
        WriteBigEndian(data, 28, checked((uint)uploadOffset));

        for (int index = 0; index < constants.Count; index++)
        {
            Direct3DConstant constant = constants[index];
            int entry = parameterOffset + index * CgParameterSize;
            bool sampler = constant.RegisterSet == 3;
            if (constant.RegisterSet is not (2 or 3))
                throw new InvalidDataException($"Shader '{path}' uses unsupported Direct3D CTAB register set {constant.RegisterSet} for '{constant.Name}'.");
            WriteBigEndian(data, entry, GetCgParameterType(constant, path));
            WriteBigEndian(data, entry + 4, sampler
                ? checked(CgTextureUnitBase + constant.RegisterIndex)
                : CgConstantRegister);
            WriteBigEndian(data, entry + 8, CgUniformVariability);
            WriteBigEndian(data, entry + 12, sampler ? uint.MaxValue : constant.RegisterIndex);
            WriteBigEndian(data, entry + 16, checked((uint)names[index]));
            WriteBigEndian(data, entry + 24, checked((uint)patchOffsets[index]));
            WriteBigEndian(data, entry + 32, CgInDirection);
            WriteBigEndian(data, entry + 36, uint.MaxValue);
            WriteBigEndian(data, entry + 40, 1);
            WriteBigEndian(data, entry + 44, sampler ? 0u : 1u);
            Encoding.ASCII.GetBytes(constant.Name).CopyTo(data, names[index]);

            if (patchOffsets[index] == 0)
                continue;
            WriteBigEndian(data, patchOffsets[index], checked((uint)patchLists[index].Count));
            for (int patchIndex = 0; patchIndex < patchLists[index].Count; patchIndex++)
            {
                WriteBigEndian(
                    data,
                    patchOffsets[index] + sizeof(uint) + patchIndex * sizeof(uint),
                    patchLists[index][patchIndex]);
            }
        }

        WriteBigEndian(data, descriptorOffset, checked((uint)rsx.InstructionCount));
        if (stage == Iw3ShaderStage.Vertex)
        {
            WriteBigEndian(data, descriptorOffset + 4, 0);
            WriteBigEndian(data, descriptorOffset + 8, checked((uint)rsx.RegisterCount));
            WriteBigEndian(data, descriptorOffset + 12, rsx.InputMask);
            WriteBigEndian(data, descriptorOffset + 16, rsx.OutputMask);
        }
        else
        {
            WriteBigEndian(data, descriptorOffset + 4, rsx.InputMask);
            WriteBigEndian(data, descriptorOffset + 8, 0);
            WriteBigEndian16(data, descriptorOffset + 0x0c, rsx.TexCoords);
            WriteBigEndian16(
                data,
                descriptorOffset + 0x0e,
                unchecked((ushort)(~rsx.TexCoords | rsx.TexCoords2D)));
            WriteBigEndian16(data, descriptorOffset + 0x10, 0);
            data[descriptorOffset + 0x12] = checked((byte)rsx.RegisterCount);
            data[descriptorOffset + 0x13] = (rsx.FragmentControl & 0x40) == 0 ? (byte)1 : (byte)0;
            data[descriptorOffset + 0x14] = (rsx.FragmentControl & 0x0e) != 0 ? (byte)1 : (byte)0;
            data[descriptorOffset + 0x15] = (rsx.FragmentControl & 0x80) != 0 ? (byte)1 : (byte)0;
        }
        rsx.Upload.CopyTo(data, uploadOffset);
        return data;
    }

    private static uint GetCgParameterType(Direct3DConstant constant, string path)
    {
        if (constant.RegisterSet == 2)
        {
            if (constant.Type == Direct3DFloat)
                return CgFloat4;
        }
        else if (constant.RegisterSet == 3 && constant.RegisterCount == 1)
        {
            return constant.Type switch
            {
                Direct3DSampler1D => CgSampler1D,
                Direct3DSampler2D => CgSampler2D,
                Direct3DSampler3D => CgSampler3D,
                Direct3DSamplerCube => CgSamplerCube,
                _ => throw new InvalidDataException(
                    $"Shader '{path}' uses unsupported Direct3D sampler type {constant.Type} " +
                    $"for '{constant.Name}'.")
            };
        }

        throw new InvalidDataException(
            $"Shader '{path}' uses unsupported Direct3D CTAB type {constant.Type} " +
            $"for '{constant.Name}'.");
    }

    private static IReadOnlyList<uint>[] BuildFragmentPatchLists(
        Iw3ShaderStage stage,
        IReadOnlyList<Direct3DConstant> constants,
        RsxProgram rsx,
        string path)
    {
        var result = new IReadOnlyList<uint>[constants.Count];
        var offsets = new List<uint>[constants.Count];
        for (int index = 0; index < constants.Count; index++)
        {
            offsets[index] = [];
            result[index] = offsets[index];
        }

        if (stage != Iw3ShaderStage.Pixel)
        {
            if (rsx.FragmentRelocations.Count != 0)
                throw new InvalidDataException($"Vertex shader '{path}' unexpectedly contains fragment relocations.");
            return result;
        }

        foreach (Direct3DConstant constant in constants)
        {
            if (constant.RegisterSet == 2 && constant.RegisterCount != 1)
            {
                throw new InvalidDataException(
                    $"Shader '{path}' pixel constant '{constant.Name}' spans {constant.RegisterCount} registers; " +
                    "the target Cg array-parameter encoding is not established.");
            }
        }

        var registerByOffset = new Dictionary<uint, uint>();
        foreach (RsxFragmentRelocation relocation in rsx.FragmentRelocations)
        {
            if (registerByOffset.TryGetValue(
                    relocation.UploadOffset,
                    out uint existingRegister))
            {
                if (existingRegister != relocation.RegisterIndex)
                {
                    throw new InvalidDataException(
                        $"Shader '{path}' assigns fragment constant slot 0x{relocation.UploadOffset:X} " +
                        $"to both c{existingRegister} and c{relocation.RegisterIndex}.");
                }
                continue;
            }
            registerByOffset.Add(relocation.UploadOffset, relocation.RegisterIndex);

            int owner = -1;
            for (int index = 0; index < constants.Count; index++)
            {
                Direct3DConstant constant = constants[index];
                if (constant.RegisterSet != 2)
                    continue;
                uint first = constant.RegisterIndex;
                uint end = checked(first + constant.RegisterCount);
                if (relocation.RegisterIndex < first || relocation.RegisterIndex >= end)
                    continue;
                if (owner >= 0)
                {
                    throw new InvalidDataException(
                        $"Shader '{path}' has overlapping CTAB registers for fragment constant c{relocation.RegisterIndex}.");
                }
                owner = index;
            }

            if (owner < 0)
            {
                throw new InvalidDataException(
                    $"Shader '{path}' uses fragment constant c{relocation.RegisterIndex} without CTAB metadata.");
            }
            offsets[owner].Add(relocation.UploadOffset);
        }

        for (int index = 0; index < constants.Count; index++)
        {
            if (constants[index].RegisterSet == 2 && offsets[index].Count == 0)
            {
                throw new InvalidDataException(
                    $"Shader '{path}' has no RSX patch location for pixel constant " +
                    $"'{constants[index].Name}'.");
            }
        }

        return result;
    }

    private static Iw3ShaderParameter CreateParameter(Direct3DConstant value) => new(
        value.Name,
        value.RegisterSet == 3 ? CgTextureUnitBase + value.RegisterIndex : CgConstantRegister,
        value.RegisterSet == 3 ? uint.MaxValue : value.RegisterIndex,
        value.RegisterCount,
        value.RegisterSet == 3 ? Iw3ShaderParameterKind.Sampler : Iw3ShaderParameterKind.Constant,
        true);

    private static string ReadCString(ReadOnlySpan<byte> source, int offset, string path)
    {
        if (offset < 0 || offset >= source.Length)
            throw new InvalidDataException($"Shader '{path}' contains an out-of-range CTAB string.");
        int length = source[offset..].IndexOf((byte)0);
        if (length <= 0)
            throw new InvalidDataException($"Shader '{path}' contains an invalid CTAB string.");
        return Encoding.ASCII.GetString(source.Slice(offset, length));
    }

    private static int Align(int value, int alignment) => checked((value + alignment - 1) & -alignment);
    private static uint ReadLittleEndian(ReadOnlySpan<byte> value, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(value.Slice(offset, 4));
    private static ushort ReadLittleEndian16(ReadOnlySpan<byte> value, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(value.Slice(offset, 2));
    private static uint ReadBigEndian(ReadOnlySpan<byte> value, int offset) => BinaryPrimitives.ReadUInt32BigEndian(value.Slice(offset, 4));
    private static ushort ReadBigEndian16(ReadOnlySpan<byte> value, int offset) => BinaryPrimitives.ReadUInt16BigEndian(value.Slice(offset, 2));
    private static void WriteBigEndian(Span<byte> value, int offset, uint integer) => BinaryPrimitives.WriteUInt32BigEndian(value.Slice(offset, 4), integer);
    private static void WriteBigEndian16(Span<byte> value, int offset, ushort integer) => BinaryPrimitives.WriteUInt16BigEndian(value.Slice(offset, 2), integer);

    private sealed record Direct3DConstant(string Name, ushort RegisterSet, ushort RegisterIndex, ushort RegisterCount, ushort Type);
    private sealed record RsxFragmentRelocation(uint RegisterIndex, uint UploadOffset);
    private sealed record RsxProgram(
        int InstructionCount,
        int RegisterCount,
        uint InputMask,
        uint OutputMask,
        uint FragmentControl,
        ushort TexCoords,
        ushort TexCoords2D,
        ushort TexCoords3D,
        IReadOnlyList<RsxFragmentRelocation> FragmentRelocations,
        byte[] Upload);
}

internal sealed record Iw3ShaderCompilation(
    MaterialShaderAsset Asset,
    IReadOnlyList<Iw3ShaderParameter> Parameters,
    string CompilerIdentity);

internal enum Iw3ShaderParameterKind
{
    Constant,
    Sampler,
}

internal sealed record Iw3ShaderParameter(
    string Name,
    uint Resource,
    uint ResourceIndex,
    uint RegisterCount,
    Iw3ShaderParameterKind Kind,
    bool IsReferenced);

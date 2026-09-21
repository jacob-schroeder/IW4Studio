using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using MapConverter.Game.IW3.PC.Shaders;
using MapConverter.Game.IW3.PC.Techniques;

return await RunAsync(args);

static async Task<int> RunAsync(string[] arguments)
{
    if (arguments.Length == 0 || arguments is ["--help"] or ["-h"])
    {
        Console.WriteLine("""
            ShaderConvert: Direct3D 9 shaders to PS3 RSX Cg programs

            --input PATH --output PATH
            [--format bytecode|hlsl] [--stage vertex|pixel]
            [--entry NAME] [--profile vs_3_0|ps_3_0] [--wine PATH] [--d3dcompiler PATH]
            [--technique PATH] [--pass ZERO_BASED_INDEX]
            [--signed-normal-input-mask UINT16] [--decoded-texcoord-input-mask UINT16]

            .hlsl and .fx inputs default to HLSL; other inputs default to bytecode.
            Bytecode stage/model are inferred. HLSL requires --stage, defaults to
            entry main and shader model 3.0, and resolves includes beside the source.
            Masks use the existing MapConverter RSX input-slot bits (decimal or 0x hex).
            Output is a native Cg blob; stdout lists its required parameter bindings.
            """);
        return 0;
    }

    string? scratch = null;
    string? temporaryOutput = null;
    try
    {
        var options = ParseOptions(arguments);
        string input = Path.GetFullPath(Required(options, "--input"));
        string output = Path.GetFullPath(Required(options, "--output"));
        if (!File.Exists(input))
            throw new FileNotFoundException("The input shader does not exist.", input);
        StringComparison pathComparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string inputTarget = new FileInfo(input).ResolveLinkTarget(true)?.FullName ?? input;
        string outputTarget = File.Exists(output) ? new FileInfo(output).ResolveLinkTarget(true)?.FullName ?? output : output;
        if (string.Equals(inputTarget, outputTarget, pathComparison))
            throw new ArgumentException("Input and output must be different files.");

        string extension = Path.GetExtension(input);
        string format = options.GetValueOrDefault("--format") ??
            (extension.Equals(".hlsl", StringComparison.OrdinalIgnoreCase) ||
             extension.Equals(".fx", StringComparison.OrdinalIgnoreCase) ? "hlsl" : "bytecode");
        if (format is not ("hlsl" or "bytecode"))
            throw new ArgumentException("--format must be hlsl or bytecode.");
        Iw3ShaderStage? requestedStage = options.TryGetValue("--stage", out string? stageText)
            ? ParseStage(stageText) : null;
        ushort normalMask = ParseMask(options, "--signed-normal-input-mask");
        ushort texCoordMask = ParseMask(options, "--decoded-texcoord-input-mask");
        string programPath = input;
        if (format == "hlsl")
        {
            if (requestedStage is null)
                throw new ArgumentException("HLSL input requires --stage vertex or --stage pixel.");
            string prefix = requestedStage == Iw3ShaderStage.Vertex ? "vs_" : "ps_";
            string profile = options.GetValueOrDefault("--profile") ?? prefix + "3_0";
            if (!profile.StartsWith(prefix, StringComparison.Ordinal) ||
                profile is not ("vs_1_1" or "vs_2_0" or "vs_2_a" or "vs_3_0" or
                                "ps_1_1" or "ps_1_2" or "ps_1_3" or "ps_1_4" or
                                "ps_2_0" or "ps_2_a" or "ps_2_b" or "ps_3_0"))
                throw new ArgumentException("--profile must be a Direct3D 9 profile matching --stage.");
            scratch = Path.Combine(Path.GetTempPath(), "shaderconvert", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratch);
            programPath = Path.Combine(scratch, "source.cso");
            await CompileHlslAsync(input, programPath, options.GetValueOrDefault("--entry") ?? "main",
                profile, options.GetValueOrDefault("--wine"), options.GetValueOrDefault("--d3dcompiler"));
        }
        else if (options.ContainsKey("--entry") || options.ContainsKey("--profile") ||
                 options.ContainsKey("--wine") || options.ContainsKey("--d3dcompiler"))
        {
            throw new ArgumentException("--entry, --profile, --wine and --d3dcompiler apply only to HLSL input.");
        }

        Iw3ShaderSource source = Iw3PcShaderCompiler.ReadSource(programPath) with
        {
            ProgramName = Path.GetFileNameWithoutExtension(input)
        };
        if (requestedStage is not null && requestedStage != source.Stage)
            throw new InvalidDataException($"The bytecode is {source.Stage}, but --stage requested {requestedStage}.");
        if (source.Stage != Iw3ShaderStage.Vertex && (normalMask != 0 || texCoordMask != 0))
            throw new ArgumentException("Input decode masks apply only to vertex shaders.");
        if (options.TryGetValue("--technique", out string? techniquePath))
        {
            Iw3TechniqueSource technique = Iw3TechniqueFormatParser.ParseTechnique(techniquePath);
            string passText = options.GetValueOrDefault("--pass") ?? "0";
            if (!int.TryParse(passText, NumberStyles.None, CultureInfo.InvariantCulture, out int passIndex) ||
                passIndex < 0 || passIndex >= technique.Passes.Count)
                throw new ArgumentException($"--pass must select one of the {technique.Passes.Count} technique passes (starting at 0).");
            Iw3TechniquePassSource pass = technique.Passes[passIndex];
            Iw3ShaderSource declared = source.Stage == Iw3ShaderStage.Vertex ? pass.VertexShader : pass.PixelShader;
            if (declared.ShaderModel != source.ShaderModel)
                throw new InvalidDataException("The technique shader model does not match the input bytecode.");
            source = source with { Arguments = declared.Arguments };
        }
        else if (options.ContainsKey("--pass"))
        {
            throw new ArgumentException("--pass requires --technique.");
        }

        Iw3ShaderCompilation compiled = new Iw3PcShaderCompiler().Compile(source, programPath, normalMask, texCoordMask);
        byte[] data = compiled.Asset.Data ?? throw new InvalidDataException("The compiler returned no Cg program.");
        string directory = Path.GetDirectoryName(output) ?? throw new ArgumentException("Output needs a parent directory.");
        Directory.CreateDirectory(directory);
        temporaryOutput = Path.Combine(directory, $".{Path.GetFileName(output)}.{Guid.NewGuid():N}.tmp");
        await File.WriteAllBytesAsync(temporaryOutput, data);
        File.Move(temporaryOutput, output, overwrite: true);
        temporaryOutput = null;

        Console.WriteLine($"Converted {source.Stage} SM{source.ShaderModel.Major}.{source.ShaderModel.Minor}: {input}");
        Console.WriteLine($"PS3 Cg: {output} ({data.Length} bytes)");
        Console.WriteLine(compiled.CompilerIdentity);
        Console.WriteLine($"Native world position lowering: {compiled.WorldPositionLowered}; world matrix register: {compiled.NativeWorldMatrixRegister}");
        foreach (Iw3ShaderParameter parameter in compiled.Parameters)
        {
            Console.WriteLine($"{parameter.Kind} {parameter.Name}: resource=0x{parameter.Resource:X}, " +
                $"index={parameter.ResourceIndex}, count={parameter.RegisterCount}, referenced={parameter.IsReferenced}, " +
                $"class={parameter.Class}, rows={parameter.Rows}, columns={parameter.Columns}, elements={parameter.Elements}");
        }
        return 0;
    }
    catch (Exception exception) when (exception is ArgumentException or IOException or InvalidDataException or
                                     InvalidOperationException or NotSupportedException or UnauthorizedAccessException or
                                     Win32Exception or OverflowException)
    {
        Console.Error.WriteLine($"ShaderConvert: {exception.Message}");
        return 1;
    }
    finally
    {
        if (temporaryOutput is not null && File.Exists(temporaryOutput)) File.Delete(temporaryOutput);
        if (scratch is not null && Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true);
    }
}

static Dictionary<string, string> ParseOptions(string[] arguments)
{
    var options = new Dictionary<string, string>(StringComparer.Ordinal);
    for (int index = 0; index < arguments.Length; index += 2)
    {
        string name = arguments[index];
        if (name is not ("--input" or "--output" or "--format" or "--stage" or "--entry" or "--profile" or
                        "--wine" or "--d3dcompiler" or "--technique" or "--pass" or
                        "--signed-normal-input-mask" or "--decoded-texcoord-input-mask"))
            throw new ArgumentException($"Unknown option '{name}'. Use --help for usage.");
        if (index + 1 == arguments.Length || string.IsNullOrWhiteSpace(arguments[index + 1]) ||
            arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException($"{name} requires a value.");
        if (!options.TryAdd(name, arguments[index + 1]))
            throw new ArgumentException($"{name} was specified more than once.");
    }
    return options;
}

static string Required(Dictionary<string, string> options, string name) =>
    options.TryGetValue(name, out string? value) ? value : throw new ArgumentException($"{name} is required.");

static Iw3ShaderStage ParseStage(string value) => value switch
{
    "vertex" => Iw3ShaderStage.Vertex,
    "pixel" => Iw3ShaderStage.Pixel,
    _ => throw new ArgumentException("--stage must be vertex or pixel.")
};

static ushort ParseMask(Dictionary<string, string> options, string name)
{
    if (!options.TryGetValue(name, out string? value)) return 0;
    bool hex = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
    if (!ushort.TryParse(hex ? value.AsSpan(2) : value.AsSpan(),
        hex ? NumberStyles.AllowHexSpecifier : NumberStyles.None, CultureInfo.InvariantCulture, out ushort mask))
        throw new ArgumentException($"{name} must be an unsigned 16-bit integer (decimal or 0x hex).");
    return mask;
}

static async Task CompileHlslAsync(string input, string output, string entry, string profile, string? wine,
    string? compiler)
{
    string frontend = Path.Combine(AppContext.BaseDirectory, "Native", "shaderconvert-hlsl.exe");
    if (!File.Exists(frontend))
        throw new FileNotFoundException("The HLSL frontend is missing. Build ShaderConvert with LLVM installed.", frontend);
    bool windows = OperatingSystem.IsWindows();
    if (compiler is not null)
    {
        compiler = Path.GetFullPath(compiler);
        if (!File.Exists(compiler))
            throw new FileNotFoundException("The selected D3DCompiler DLL does not exist.", compiler);
    }
    var start = new ProcessStartInfo(windows ? frontend : wine ?? "wine")
    {
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true
    };
    if (!windows)
    {
        start.ArgumentList.Add(frontend);
        start.Environment["WINEDEBUG"] = "-all";
    }
    start.ArgumentList.Add(windows ? input : "Z:" + input.Replace('/', '\\'));
    start.ArgumentList.Add(windows ? output : "Z:" + output.Replace('/', '\\'));
    start.ArgumentList.Add(entry);
    start.ArgumentList.Add(profile);
    start.ArgumentList.Add(compiler is null ? "d3dcompiler_47.dll" :
        windows ? compiler : "Z:" + compiler.Replace('/', '\\'));
    using var process = new Process { StartInfo = start };
    try
    {
        process.Start();
    }
    catch (Win32Exception exception) when (!windows)
    {
        throw new InvalidOperationException("HLSL conversion requires Wine with d3dcompiler_47.dll. " +
            "Install Wine or select its executable with --wine.", exception);
    }
    Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
    Task<string> standardError = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    string messages = string.Join(Environment.NewLine, new[] { await standardOutput, await standardError }
        .Where(message => !string.IsNullOrWhiteSpace(message))).Trim();
    if (!string.IsNullOrEmpty(messages)) Console.Error.WriteLine(messages);
    if (process.ExitCode != 0 || !File.Exists(output))
        throw new InvalidDataException($"HLSL compilation failed (exit {process.ExitCode}).");
}

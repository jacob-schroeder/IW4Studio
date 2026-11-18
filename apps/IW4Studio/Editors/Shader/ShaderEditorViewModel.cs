using System.Text;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.TechniqueSet;
using IW4.Render.Shaders;
using IW4.Studio.Desktop.Persistence;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Editors.Shader;

public sealed class ShaderEditorViewModel : ObservableObject, IAssetEditorStagingState, IDisposable
{
    private readonly AssetEditorSession _session;
    private readonly CancellationTokenSource _lifetime = new();
    private ShaderDraft _draft;
    private string _appliedSource;
    private string _source;
    private string _messages;
    private string _parameters;
    private ShaderPassChoice? _selectedPass;
    private MaterialAsset? _selectedMaterial;
    private bool _compiling;
    private bool _sourceNeedsRetention;
    private ShaderCompilerChoice _compiler;

    public ShaderEditorViewModel(AssetEditorSession session)
    {
        _session = session;
        Compilers = CgSourceCompiler.Locate();
        _compiler = Compilers[0];
        _draft = session.OpenDraft<ShaderDraft>();
        bool vertex = _draft.Kind == MaterialShaderKind.Vertex;
        RsxCgSource recovered = _draft.Bytecode.IsEmpty && session.CanEdit
            ? new RsxCgSource(vertex
                ? "void main(float4 clipPosition : ATTR0, out float4 position : POSITION)\n{\n    position = clipPosition;\n}\n"
                : "void main(out half4 color : COLOR0)\n{\n    color = half4(1.0, 1.0, 1.0, 1.0);\n}\n", [], [])
                { PrimaryOutput = vertex ? "position" : "color" }
            : RsxCgSourceDecompiler.Decompile(_draft.Bytecode.ToArray(), vertex);
        _source = ShaderSourceStore.Read(_draft) ?? recovered.Source;
        PrimaryOutput = recovered.PrimaryOutput;
        _appliedSource = _source;
        _messages = string.Empty;
        _parameters = FormatParameters(recovered.Parameters);
        RefreshPasses();
    }

    public string Name => _draft.Name ?? _session.Entry.OriginalName ?? "Shader";
    public IReadOnlyList<ShaderCompilerChoice> Compilers { get; }
    public ShaderCompilerChoice SelectedCompiler
    {
        get => _compiler;
        set
        {
            if (value is null || _compiling || !SetProperty(ref _compiler, value)) return;
            OnPropertyChanged(nameof(EditingHint));
        }
    }

    public string ProgramSummary => $"{_draft.Kind} shader   ·   {_draft.Bytecode.Length:N0} program bytes";
    public bool IsReadOnly => !_session.CanEdit || _compiling;
    private bool HasSourceEdits => _source != _appliedSource;
    private bool IsRetentionRetry => _sourceNeedsRetention && !HasSourceEdits;
    public bool HasUnappliedChanges => _session.CanEdit && (_draft.Bytecode.IsEmpty || HasSourceEdits || _sourceNeedsRetention);
    public bool CanCompile => HasUnappliedChanges && !_compiling;
    public bool CanRestoreSource => !IsReadOnly && HasSourceEdits;
    public string CompileAction => IsRetentionRetry ? "Retry saving source" : "Compile and apply";
    public string? PrimaryOutput { get; }
    public bool CanFindOutput => PrimaryOutputOffset >= 0;
    internal int PrimaryOutputOffset
    {
        get
        {
            if (PrimaryOutput is not { Length: > 0 } name) return -1;
            ReadOnlySpan<char> text = Source.AsSpan();
            int end = text.Length;
            while (end > 0)
            {
                int offset = text[..end].LastIndexOf(name, StringComparison.Ordinal);
                if (offset < 0) return -1;
                int next = offset + name.Length;
                if ((offset == 0 || !IsIdentifierChar(text[offset - 1])) &&
                    (next == text.Length || !IsIdentifierChar(text[next]))) return offset;
                end = offset;
            }
            return -1;
        }
    }
    private static bool IsIdentifierChar(char c) => char.IsAsciiLetterOrDigit(c) || c == '_';
    public bool HasRecoveryGaps => Source.Contains(RsxCgSourceDecompiler.RecoveryDirective, StringComparison.Ordinal);
    private string ReadOnlyHint => _session.Entry.ResolvedProviderZone is { IsTarget: false, LogicalZoneName: { Length: > 0 } zone }
        ? $"Read-only reference supplied by {zone}. Open its owning fastfile to edit the definition."
        : "Read-only workspace shader. Editing requires a uniquely owned shader definition in the target fastfile.";
    public string EditingHint => !_session.CanEdit ? ReadOnlyHint :
        IsRetentionRetry ? (_compiling ? "Saving Cg source locally…" : "Retry saving the applied Cg source to your local user data.") :
        _compiling ? "Compiling the edited Cg program…" :
        HasRecoveryGaps ? "Partial reconstruction. Each marked operation needs an implementation before this source can compile." :
        (_compiler.Kind is CgSourceCompilerKind.Managed or CgSourceCompilerKind.ShaderConvert) &&
            Source.Contains(RsxCgSourceDecompiler.ReciprocalWCompilerError, StringComparison.Ordinal)
            ? "This shader reads WPOS.w. Select PS3DK or sce-cgc to compile it." :
        _draft.Bytecode.IsEmpty ? "Compile and apply builds this new shader. Save As writes it to the fastfile." :
        HasUnappliedChanges ? "Source edited. Compile and apply updates this shader in the workspace." :
        "Edit the calculation in main. Save As compiles your changes into the fastfile.";
    public string ExampleTitle => _draft.Kind == MaterialShaderKind.Pixel ? "Tint the pixel output" : "Scale the clip-space position";
    public string ExampleInstruction => _draft.Kind == MaterialShaderKind.Pixel
        ? "Find the output tagged COLOR0 in main. After its final assignment, multiply the green and blue channels by 0.5 to give the result a red tint. Alpha stays the same."
        : "Find the output tagged POSITION in main. After its final assignment, scale x and y to draw the geometry closer to the screen center. Depth and w stay the same.";
    public string ExampleCode => _draft.Kind == MaterialShaderKind.Pixel
        ? $"{(CanFindOutput ? PrimaryOutput : "color")}.rgb *= half3(1.0, 0.5, 0.5);"
        : $"{(CanFindOutput ? PrimaryOutput : "position")}.xy *= 0.9;";
    public string ExampleNameHint => CanFindOutput ? "The example uses this program’s recovered output name." :
        "Use your shader’s output variable. If main returns the output directly, apply the same change to the returned value.";
    public string Source
    {
        get => _source;
        set
        {
            if (IsReadOnly || !SetProperty(ref _source, value)) return;
            NotifyStaging();
            OnPropertyChanged(nameof(HasRecoveryGaps));
            OnPropertyChanged(nameof(CanFindOutput));
            OnPropertyChanged(nameof(ExampleCode));
            OnPropertyChanged(nameof(ExampleNameHint));
        }
    }
    public string Messages { get => _messages; internal set => SetProperty(ref _messages, value); }
    public string Parameters { get => _parameters; private set => SetProperty(ref _parameters, value); }
    public IReadOnlyList<ShaderPassChoice> Passes { get; private set; } = [];
    public string PassCount => Passes.Count == 0 ? "No resolved technique passes in this workspace." :
        $"{Passes.Count} technique {(Passes.Count == 1 ? "pass" : "passes")}";
    public ShaderPassChoice? SelectedPass
    {
        get => _selectedPass;
        set
        {
            if (!SetProperty(ref _selectedPass, value)) return;
            OnPropertyChanged(nameof(Materials));
            SelectedMaterial = Materials.FirstOrDefault();
            OnPropertyChanged(nameof(PassDetails));
        }
    }
    public IReadOnlyList<MaterialAsset> Materials => SelectedPass?.Usage.Materials ?? [];
    public MaterialAsset? SelectedMaterial
    {
        get => _selectedMaterial;
        set { if (SetProperty(ref _selectedMaterial, value)) OnPropertyChanged(nameof(PassDetails)); }
    }

    public string PassDetails => SelectedPass is { } choice
        ? DescribePass(choice.Usage.Pass, _draft.Kind, SelectedMaterial) : string.Empty;

    public void RefreshPasses()
    {
        ShaderPassChoice? previous = SelectedPass;
        Passes = _session.CaptureShaderUsages().Select(use => new ShaderPassChoice(use)).ToArray();
        OnPropertyChanged(nameof(Passes));
        OnPropertyChanged(nameof(PassCount));
        SelectedPass = Passes.FirstOrDefault(choice => previous is not null &&
            ReferenceEquals(choice.Usage.TechniqueSet, previous.Usage.TechniqueSet) &&
            choice.Usage.TechniqueSlot == previous.Usage.TechniqueSlot &&
            choice.Usage.PassIndex == previous.Usage.PassIndex) ?? Passes.FirstOrDefault();
    }

    public void RestoreSource() => Source = _appliedSource;

    public Task CompileAndApplyAsync() => CompilePendingAsync([this], _lifetime.Token);

    // Finish every compile and check the live drafts before publishing any of them.
    internal static async Task<string?> CompilePendingAsync(
        IEnumerable<ShaderEditorViewModel> editors, CancellationToken cancellation)
    {
        ShaderEditorViewModel[] pending = editors.Where(editor => editor.HasUnappliedChanges).Distinct().ToArray();
        if (pending.Any(editor => editor._compiling)) return "A shader compilation is already in progress.";
        var results = new List<(ShaderEditorViewModel Editor, byte[]? Program, string Log)>();
        ShaderEditorViewModel? active = null;
        foreach (var editor in pending) editor.SetCompiling(true);
        try
        {
            foreach (var editor in pending)
            {
                active = editor;
                if (editor.IsRetentionRetry)
                {
                    results.Add((editor, null, string.Empty));
                    continue;
                }
                editor.Messages = "Compiling Cg…";
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, editor._lifetime.Token);
                var result = await CgSourceCompiler.CompileAsync(editor.Source, editor._draft.Kind, editor._compiler, linked.Token);
                results.Add((editor, result.Program, result.Messages));
                editor.Messages = "Compiled.";
            }
            cancellation.ThrowIfCancellationRequested();
            foreach (var result in results)
            {
                active = result.Editor;
                if (active._lifetime.IsCancellationRequested) throw new OperationCanceledException();
                if (result.Program is { } program)
                    active._session.ValidateCompiledShader(active._draft, program);
            }
            foreach (var result in results)
            {
                active = result.Editor;
                if (result.Program is { } program)
                {
                    active._session.ApplyCompiledShader(active._draft, program);
                    active._draft = active._session.ReadDraft<ShaderDraft>();
                    active._appliedSource = active.Source;
                    active._sourceNeedsRetention = true;
                    var reflection = RsxCgSourceDecompiler.Decompile(active._draft.Bytecode.ToArray(),
                        active._draft.Kind == MaterialShaderKind.Vertex);
                    active.Parameters = FormatParameters(reflection.Parameters);
                    active.OnPropertyChanged(nameof(ProgramSummary));
                }
                active.RetainAppliedSource();
                active.Messages = string.IsNullOrWhiteSpace(result.Log) ? "Cg source saved locally." : result.Log;
            }
            return null;
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or IOException
            or UnauthorizedAccessException or OperationCanceledException or ArgumentException or KeyNotFoundException)
        {
            string message = ex is OperationCanceledException ? "Compilation cancelled." : ex.Message;
            if (active is not null) active.Messages = message;
            return active is null ? message : $"{active.Name} — {message}";
        }
        finally
        {
            foreach (var editor in pending) editor.SetCompiling(false);
        }
    }

    private void RetainAppliedSource()
    {
        try
        {
            ShaderSourceStore.Write(_draft, _appliedSource);
            _sourceNeedsRetention = false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new IOException($"The shader was applied, but Studio could not save its Cg source locally. {exception.Message}", exception);
        }
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private void SetCompiling(bool value)
    {
        _compiling = value;
        OnPropertyChanged(nameof(IsReadOnly));
        NotifyStaging();
    }

    private void NotifyStaging()
    {
        OnPropertyChanged(nameof(HasUnappliedChanges));
        OnPropertyChanged(nameof(CanCompile));
        OnPropertyChanged(nameof(CanRestoreSource));
        OnPropertyChanged(nameof(CompileAction));
        OnPropertyChanged(nameof(EditingHint));
    }

    private static string FormatParameters(IReadOnlyList<string> parameters) =>
        parameters.Count > 0 ? string.Join(Environment.NewLine, parameters) : "No reflected parameters.";

    private static string DescribePass(MaterialPassAsset pass, MaterialShaderKind stage, MaterialAsset? material)
    {
        var lines = new List<string>
        {
            $"Vertex shader   {pass.VertexShader?.Name ?? "(unresolved)"}",
            $"Pixel shader    {pass.PixelShader?.Name ?? "(unresolved)"}",
            ""
        };
        for (int i = 0; i < pass.Args.Count; i++)
        {
            var arg = pass.Args[i];
            bool vertex = arg.Type is MaterialShaderArgumentType.MaterialVertexConst or
                MaterialShaderArgumentType.CodeVertexConst or MaterialShaderArgumentType.LiteralVertexConst;
            if (vertex != (stage == MaterialShaderKind.Vertex)) continue;
            bool sampler = arg.Type is MaterialShaderArgumentType.CodePixelSampler or MaterialShaderArgumentType.MaterialPixelSampler;
            string dest = vertex ? $"c{arg.Dest}" : sampler ? $"s{arg.Dest}" : $"parameter [{arg.Dest}]";
            string frequency = i < pass.PerPrimArgCount ? "per primitive" :
                i < pass.PerPrimArgCount + pass.PerObjArgCount ? "per object" : "stable";
            lines.Add($"{dest}  ←  {ArgumentValue(arg, material)}   ({frequency})");
        }
        if (lines.Count == 3) lines.Add("No pass arguments for this stage.");
        if (stage == MaterialShaderKind.Vertex && pass.VertexDeclaration is { } decl)
        {
            lines.Add("");
            lines.AddRange(decl.Routing.Take(decl.StreamCount).Select(route => $"{route.Source} → {route.Dest}"));
        }
        if (stage == MaterialShaderKind.Pixel && pass.CustomSamplerFlags != 0)
            lines.Add($"Custom samplers   {pass.CustomSamplerFlags}");
        return string.Join(Environment.NewLine, lines);
    }

    private static string ArgumentValue(MaterialShaderArgumentAsset arg, MaterialAsset? material)
    {
        switch (arg.Type)
        {
            case MaterialShaderArgumentType.CodeVertexConst:
            case MaterialShaderArgumentType.CodePixelConst:
                var code = arg.CodeConstant;
                return $"{code.SourceName}, row {code.FirstRow} × {code.RowCount}";
            case MaterialShaderArgumentType.CodePixelSampler:
                return arg.CodeTextureSource.ToString();
            case MaterialShaderArgumentType.LiteralVertexConst:
            case MaterialShaderArgumentType.LiteralPixelConst:
                return arg.LiteralConstant is { } literal
                    ? FormattableString.Invariant($"({literal.X:R}, {literal.Y:R}, {literal.Z:R}, {literal.W:R})")
                    : "(unresolved literal)";
            case MaterialShaderArgumentType.MaterialPixelSampler:
                var texture = material?.Textures.FirstOrDefault(item => item.NameHash == arg.MaterialNameHash);
                return texture is null ? $"material sampler 0x{arg.MaterialNameHash:X8}" :
                    $"{texture.Semantic}  {texture.Image?.Name ?? "(unresolved image)"}  [0x{arg.MaterialNameHash:X8}]";
            case MaterialShaderArgumentType.MaterialVertexConst:
            case MaterialShaderArgumentType.MaterialPixelConst:
                var constant = material?.Constants.FirstOrDefault(item => item.NameHash == arg.MaterialNameHash);
                if (constant is null) return $"material constant 0x{arg.MaterialNameHash:X8}";
                var value = constant.Literal;
                string name = Encoding.ASCII.GetString(constant.NameBytes.TakeWhile(b => b != 0).ToArray());
                return FormattableString.Invariant($"{name} [0x{arg.MaterialNameHash:X8}] = ({value.X:R}, {value.Y:R}, {value.Z:R}, {value.W:R})");
            default:
                return $"{arg.Type}  0x{arg.ArgumentRaw:X8}";
        }
    }
}

public sealed record ShaderPassChoice(ShaderUsage Usage)
{
    public override string ToString() =>
        $"{Usage.TechniqueSet.Name} / {(MaterialTechniqueType)Usage.TechniqueSlot} / {Usage.Technique.Name} / pass {Usage.PassIndex}";
}

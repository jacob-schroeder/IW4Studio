using IW4.Game.Assets.TechniqueSet;
using IW4.Game.Zone;
using IW4.Linker.Plans;

namespace IW4.Studio.Documents;

public sealed class ShaderDraft
{
    private readonly MaterialShaderAsset _baseline;
    private readonly byte[]? _replacement;

    internal ShaderDraft(MaterialShaderAsset definition)
        : this(Copy(definition, null), null, new object())
    {
    }

    private ShaderDraft(MaterialShaderAsset baseline, byte[]? replacement, object revision)
    {
        _baseline = baseline;
        _replacement = replacement;
        Revision = revision;
    }

    public string? Name => _baseline.Name;
    public MaterialShaderKind Kind => _baseline.Kind;
    public ReadOnlyMemory<byte> Bytecode => _replacement ?? _baseline.Data;
    public ReadOnlyMemory<byte> ProgramBytes => _baseline.ProgramBytes;

    internal object Revision { get; }

    internal ShaderDraft Clone() =>
        new(Copy(_baseline, null), _replacement?.ToArray(), Revision);

    internal ShaderDraft WithNewRevision() =>
        new(_baseline, _replacement, new object());

    internal ShaderDraft ReplaceBytecode(
        ReadOnlyMemory<byte> bytecode,
        IReadOnlyCollection<ushort> constantDestinations)
    {
        byte[] replacement = bytecode.ToArray();
        string fieldPath = $"Shader '{Name}'";
        MaterialShaderVertexReservation.ValidateCompiledProgram(
            Kind, replacement, fieldPath);
        if (_baseline.Data is { Length: > 0 } original)
        {
            if (replacement.AsSpan().SequenceEqual(original))
                return new ShaderDraft(_baseline, null, Revision);

            replacement = MaterialShaderVertexReservation.PrepareReplacementInterface(
                Kind, original, replacement, constantDestinations, fieldPath);
        }

        return new ShaderDraft(_baseline, replacement, Revision);
    }

    internal MaterialShaderAsset ToAsset() => Copy(_baseline, _replacement);

    internal bool SemanticallyEquals(ShaderDraft other) =>
        Kind == other.Kind &&
        string.Equals(Name, other.Name, StringComparison.Ordinal) &&
        Bytecode.Span.SequenceEqual(other.Bytecode.Span) &&
        ProgramBytes.Span.SequenceEqual(other.ProgramBytes.Span);

    private static MaterialShaderAsset Copy(MaterialShaderAsset baseline, byte[]? replacement) => new()
    {
        Offset = baseline.Offset,
        RuntimeAddress = baseline.RuntimeAddress,
        Kind = baseline.Kind,
        Name = baseline.Name,
        NamePointer = baseline.NamePointer,
        // A changed program needs its own native alias cell. Other shaders
        // referencing the imported cell must keep their original bytecode.
        DataPointer = replacement is null ? baseline.DataPointer : default,
        DataSize = replacement is null ? baseline.DataSize : checked((uint)replacement.Length),
        Data = (replacement ?? baseline.Data)?.ToArray(),
        ProgramBytes = baseline.ProgramBytes.ToArray()
    };
}

internal sealed class ShaderAdapter(XAssetType stage) :
    AssetAuthoringAdapter<MaterialShaderAsset, ShaderDraft>
{
    public override XAssetType AssetType => stage;

    public override ShaderDraft CreateDraft(MaterialShaderAsset definition) => new(definition);

    public override ShaderDraft CloneDraft(ShaderDraft draft) => draft.Clone();

    public override MaterialShaderAsset CreateDefinition(ShaderDraft draft) => draft.ToAsset();

    public override bool SemanticallyEquals(ShaderDraft left, ShaderDraft right) =>
        left.SemanticallyEquals(right);
}

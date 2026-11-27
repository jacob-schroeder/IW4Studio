namespace ShaderConvert.Game.IW3.PC.Techniques;

internal enum Iw3ShaderStage
{
    Vertex,
    Pixel,
}

internal enum Iw3CodeShaderValueKind
{
    Constant,
    Sampler,
}

internal sealed record Iw3TechniqueSetSource(
    string Name,
    IReadOnlyList<Iw3TechniqueSlotSource> Slots);

internal sealed record Iw3TechniqueSlotSource(
    Iw3TechniqueSlot Slot,
    string DisplayName,
    string? TechniqueName,
    Iw3TechniqueSource? Technique);

internal sealed record Iw3TechniqueSource(
    string Name,
    IReadOnlyList<Iw3TechniquePassSource> Passes);

internal sealed record Iw3TechniquePassSource(
    string StateMapName,
    Iw3ShaderSource VertexShader,
    Iw3ShaderSource PixelShader,
    IReadOnlyList<Iw3VertexStreamRoutingSource> VertexRouting);

internal sealed record Iw3ShaderSource(
    Iw3ShaderStage Stage,
    Iw3ShaderModel ShaderModel,
    string ProgramName,
    IReadOnlyList<Iw3ShaderArgumentSource> Arguments);

internal readonly record struct Iw3ShaderModel(int Major, int Minor);

internal sealed record Iw3ShaderArgumentSource(
    Iw3IndexedName Destination,
    Iw3ShaderValueSource Value);

internal abstract record Iw3ShaderValueSource;

internal sealed record Iw3CodeShaderValueSource(
    Iw3CodeShaderValueKind Kind,
    string Accessor,
    int? ElementIndex) : Iw3ShaderValueSource;

internal sealed record Iw3CodeShaderRowsSource(
    string Accessor, int? ElementIndex, int FirstRow, int RowCount) : Iw3ShaderValueSource;

internal sealed record Iw3LiteralShaderValueSource(
    float X,
    float Y,
    float Z,
    float W) : Iw3ShaderValueSource;

internal sealed record Iw3MaterialShaderValueSource(
    string? PropertyName,
    uint? PropertyHash) : Iw3ShaderValueSource;

internal sealed record Iw3VertexStreamRoutingSource(
    Iw3IndexedName Destination,
    Iw3IndexedName Source);

internal readonly record struct Iw3IndexedName(string Name, int? Index);

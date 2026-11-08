namespace MapConverter.Game.IW3.PS3.Techniques;

// The reader resolves the native pointers and retains shared object identity.
// Root byte spans are the serialized big-endian IW3 PS3 structures.
internal sealed record Iw3Ps3TechniqueSetSource(
    string Name,
    ReadOnlyMemory<byte> Root,
    IReadOnlyList<Iw3Ps3TechniqueSource?> Slots);

internal sealed record Iw3Ps3TechniqueSource(
    string Name,
    ReadOnlyMemory<byte> Root,
    IReadOnlyList<Iw3Ps3PassSource> Passes);

internal sealed record Iw3Ps3PassSource(
    ReadOnlyMemory<byte> Root,
    ReadOnlyMemory<byte> Declaration,
    Iw3Ps3ShaderSource? VertexShader,
    Iw3Ps3ShaderSource? PixelShader,
    IReadOnlyList<Iw3Ps3ArgumentSource> Arguments);

internal sealed record Iw3Ps3ShaderSource(
    string Name,
    bool IsVertex,
    ReadOnlyMemory<byte> Root,
    ReadOnlyMemory<byte> Program);

internal sealed record Iw3Ps3ArgumentSource(
    ReadOnlyMemory<byte> Root,
    ReadOnlyMemory<byte> Literal);

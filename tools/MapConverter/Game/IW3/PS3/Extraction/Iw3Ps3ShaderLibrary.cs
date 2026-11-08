using System.Buffers.Binary;
using System.Text;
using MapConverter.Game.IW3.PS3.Materials;
using MapConverter.Game.IW3.PS3.Techniques;

namespace MapConverter.Game.IW3.PS3.Extraction;

internal sealed class Iw3Ps3ShaderLibrary
{
    private readonly Dictionary<(bool Vertex, string Name), Iw3Ps3ShaderSource> _shaders = [];
    private readonly Dictionary<string, Iw3Ps3TechniqueSetSource> _sets =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, Iw3Ps3MaterialSource> _materials =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, (byte[] Root, string? SoundPrefix)> _presets =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, Iw3Ps3ModelSource> _models =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _unsupportedModels =
        new(StringComparer.Ordinal);
    private readonly Dictionary<Iw3Ps3TechniqueSource, Iw3Ps3TechniqueSource> _techniques =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Iw3Ps3PassSource, Iw3Ps3PassSource> _passes =
        new(ReferenceEqualityComparer.Instance);
    private readonly List<string> _prefixStops = [];

    internal void AddFastFile(string path, CancellationToken token)
    {
        var (data, logicalEnd) = Iw3Ps3FastFileCatalog.ReadDecoded(path, token);
        AddInlineNamedShaders(data, logicalEnd, token);
        string? stop = Iw3Ps3FastFileCatalog.CaptureShaderPrefix(
            path, data, logicalEnd, this, token);
        // The copied graph preserves sharing within this fastfile. Drop the
        // source-record keys so they do not pin its complete decoded zone.
        _techniques.Clear();
        _passes.Clear();
        if (stop is not null) _prefixStops.Add(stop);
    }

    private void AddInlineNamedShaders(byte[] data, int logicalEnd, CancellationToken token)
    {
        for (int offset = 36; offset <= logicalEnd - 32; offset++)
        {
            if ((offset & 0xffff) == 0) token.ThrowIfCancellationRequested();
            ReadOnlySpan<byte> header = data.AsSpan(offset, 32);
            uint profile = BinaryPrimitives.ReadUInt32BigEndian(header);
            if (profile is not (7003 or 7004) ||
                BinaryPrimitives.ReadUInt32BigEndian(header[4..]) != 6)
                continue;
            uint length = BinaryPrimitives.ReadUInt32BigEndian(header[8..]);
            if (length < 32 || length > logicalEnd - offset || data[offset - 1] != 0)
                continue;

            bool vertex = profile == 7003;
            int wrapperSize = vertex ? 12 : 20;
            for (int start = offset - 2;
                start >= Math.Max(36 + wrapperSize, offset - 513); start--)
            {
                byte character = data[start];
                if (character < 0x20 || character > 0x7e) break;
                int wrapper = start - wrapperSize;
                if (BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(wrapper)) != uint.MaxValue ||
                    BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(wrapper + 4)) is not
                        (uint.MaxValue or 0xfffffffe) ||
                    BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(wrapper + 8)) != length)
                    continue;

                ReadOnlySpan<byte> program = data.AsSpan(offset, (int)length);
                uint count = BinaryPrimitives.ReadUInt32BigEndian(program[12..]);
                uint parameters = BinaryPrimitives.ReadUInt32BigEndian(program[16..]);
                uint descriptor = BinaryPrimitives.ReadUInt32BigEndian(program[20..]);
                uint codeLength = BinaryPrimitives.ReadUInt32BigEndian(program[24..]);
                uint code = BinaryPrimitives.ReadUInt32BigEndian(program[28..]);
                if ((ulong)parameters + count * 48UL > length ||
                    (ulong)descriptor + 24 > length ||
                    (ulong)code + codeLength > length)
                    throw new InvalidDataException("An inline-named COD4 PS3 shader has an invalid Cg container.");
                string name = Encoding.ASCII.GetString(data, start, offset - start - 1);
                if (name.Length != 0 && name[0] != ',')
                    Add(new(name, vertex, data.AsMemory(wrapper, wrapperSize),
                        data.AsMemory(offset, (int)length)));
                break;
            }
            offset += (int)length - 1;
        }
    }

    internal void Add(Iw3Ps3ShaderSource source)
    {
        if (source.Name.StartsWith(',') || source.Program.IsEmpty) return;
        var key = (source.IsVertex, source.Name);
        if (_shaders.TryGetValue(key, out Iw3Ps3ShaderSource? previous))
        {
            if (!previous.Program.Span.SequenceEqual(source.Program.Span))
                throw new InvalidDataException($"Conflicting native shader '{source.Name}'.");
            return;
        }
        // Retain only shader bytes, not each complete decoded stock fastfile.
        _shaders.Add(key, source with
        {
            Root = source.Root.ToArray(),
            Program = source.Program.ToArray()
        });
    }

    internal void AddSet(Iw3Ps3TechniqueSetSource source)
    {
        if (source.Name.StartsWith(',')) return;
        var copy = new Iw3Ps3TechniqueSetSource(source.Name, source.Root.ToArray(),
            source.Slots.Select(CopyTechnique).ToArray());
        if (_sets.TryGetValue(source.Name, out Iw3Ps3TechniqueSetSource? previous))
        {
            if (!SameSet(previous, copy))
                throw new InvalidDataException($"Conflicting native technique set '{source.Name}'.");
            return;
        }
        _sets.Add(source.Name, copy);
    }

    internal Iw3Ps3TechniqueSetSource ResolveSet(Iw3Ps3TechniqueSetSource source)
    {
        if (!source.Name.StartsWith(',')) return source;
        return _sets.TryGetValue(source.Name[1..], out var owned)
            ? owned
            : throw new InvalidDataException($"Missing COD4 PS3 technique set '{source.Name[1..]}'.");
    }

    internal void AddMaterial(Iw3Ps3MaterialSource source)
    {
        if (source.Name.StartsWith(',')) return;
        Iw3Ps3TechniqueSetSource? set = source.TechniqueSet;
        if (set is not null)
        {
            if (set.Name.StartsWith(','))
            {
                if (set.Slots.Any(slot => slot is not null))
                    throw new InvalidDataException(
                        $"External COD4 PS3 technique set '{set.Name}' has owned slots.");
                set = new Iw3Ps3TechniqueSetSource(set.Name, set.Root.ToArray(),
                    set.Slots.ToArray());
            }
            else if (!_sets.TryGetValue(set.Name, out var ownedSet))
                throw new InvalidDataException(
                    $"Material '{source.Name}' has an uncaptured technique set '{set.Name}'.");
            else set = ownedSet;
        }
        var copy = new Iw3Ps3MaterialSource(source.Name, source.Root.ToArray(), set,
            source.Textures.Select(texture => new Iw3Ps3TextureSource(
                texture.Root.ToArray(), null, texture.ImageName)).ToArray(),
            source.Constants.Select(bytes => (ReadOnlyMemory<byte>)bytes.ToArray()).ToArray(),
            source.States.Select(bytes => (ReadOnlyMemory<byte>)bytes.ToArray()).ToArray());
        if (_materials.TryGetValue(source.Name, out Iw3Ps3MaterialSource? previous))
        {
            if (!SameMaterial(previous, copy))
                throw new InvalidDataException($"Conflicting native material '{source.Name}'.");
            return;
        }
        _materials.Add(source.Name, copy);
    }

    internal Iw3Ps3MaterialSource ResolveMaterial(Iw3Ps3MaterialSource source)
    {
        if (!source.Name.StartsWith(',')) return source;
        return _materials.TryGetValue(source.Name[1..], out var owned)
            ? owned
            : throw new InvalidDataException($"Missing COD4 PS3 material '{source.Name[1..]}'.");
    }

    internal Iw3Ps3MaterialSource ResolveMaterial(string name) =>
        _materials.TryGetValue(name, out var source) ? source :
        throw new InvalidDataException($"Missing COD4 PS3 material '{name}'.");

    internal void AddModel(Iw3Ps3ModelSource source)
    {
        if (source.Name.StartsWith(',')) return;
        if (source.HasPhysCollmap)
        {
            AddUnsupportedModel(source.Name, "source PhysCollmap geometry");
            return;
        }
        if (_models.ContainsKey(source.Name))
            throw new InvalidDataException($"Duplicate native model '{source.Name}'.");
        _models.Add(source.Name, source with
        {
            Root = source.Root.ToArray(),
            BoneNames = source.BoneNames.ToArray(),
            BoneNameValues = source.BoneNameValues.ToArray(),
            Parents = source.Parents.ToArray(),
            Quats = source.Quats.ToArray(),
            Translations = source.Translations.ToArray(),
            Classification = source.Classification.ToArray(),
            BaseMatrices = source.BaseMatrices.ToArray(),
            Surfaces = source.Surfaces.Select(surface => surface with
            {
                Root = surface.Root.ToArray(),
                Blend = surface.Blend.ToArray(),
                Verts0 = surface.Verts0.ToArray(),
                Verts1 = surface.Verts1.ToArray(),
                Rigids = surface.Rigids.Select(rigid => rigid with
                {
                    Root = rigid.Root.ToArray(),
                    Tree = rigid.Tree is null ? null : rigid.Tree with
                    {
                        Root = rigid.Tree.Root.ToArray(),
                        Nodes = rigid.Tree.Nodes.ToArray(),
                        Leaves = rigid.Tree.Leaves.ToArray()
                    }
                }).ToArray(),
                Triangles = surface.Triangles.ToArray()
            }).ToArray(),
            MaterialNames = source.MaterialNames.ToArray(),
            Collision = source.Collision.ToArray(),
            BoneInfo = source.BoneInfo.ToArray()
        });
    }

    internal Iw3Ps3ModelSource ResolveModel(string name) =>
        _models.TryGetValue(name, out var source) ? source :
        throw new InvalidDataException(_unsupportedModels.TryGetValue(name, out var reason)
            ? $"COD4 PS3 model '{name}' has unsupported source data: {reason}."
            : $"Missing COD4 PS3 model '{name}'.");

    internal void AddUnsupportedModel(string name, string reason)
    {
        if (!name.StartsWith(',')) _unsupportedModels.TryAdd(name, reason);
    }

    internal void AddPreset(string name, ReadOnlyMemory<byte> root, string? soundPrefix)
    {
        if (name.StartsWith(',')) return;
        if (root.Length != 0x2c)
            throw new InvalidDataException($"COD4 PS3 PhysPreset '{name}' has an invalid root.");
        if (_presets.TryGetValue(name, out var previous))
        {
            ReadOnlySpan<byte> old = previous.Root, current = root.Span;
            if (previous.SoundPrefix != soundPrefix ||
                !old[4..0x1c].SequenceEqual(current[4..0x1c]) ||
                !old[0x20..0x29].SequenceEqual(current[0x20..0x29]))
                throw new InvalidDataException($"Conflicting native PhysPreset '{name}'.");
            return;
        }
        _presets.Add(name, (root.ToArray(), soundPrefix));
    }

    internal (ReadOnlyMemory<byte> Root, string? SoundPrefix) ResolvePreset(string name)
    {
        if (_presets.TryGetValue(name, out var source))
            return (source.Root, source.SoundPrefix);
        throw new InvalidDataException($"Missing COD4 PS3 PhysPreset '{name}'.");
    }

    private static bool SameMaterial(Iw3Ps3MaterialSource left, Iw3Ps3MaterialSource right)
    {
        // Native pointer words differ across fastfiles; the authored scalar fields
        // and resolved children establish whether definitions are equivalent.
        if (left.Root.Length != 0x80 || right.Root.Length != 0x80 ||
            !left.Root.Span[4..0x6c].SequenceEqual(right.Root.Span[4..0x6c]) ||
            left.TechniqueSet?.Name != right.TechniqueSet?.Name ||
            left.Textures.Count != right.Textures.Count ||
            left.Constants.Count != right.Constants.Count ||
            left.States.Count != right.States.Count)
            return false;
        for (int index = 0; index < left.Textures.Count; index++)
        {
            Iw3Ps3TextureSource a = left.Textures[index], b = right.Textures[index];
            if (a.Root.Length != 12 || b.Root.Length != 12 ||
                !a.Root.Span[..8].SequenceEqual(b.Root.Span[..8]) ||
                a.ImageName != b.ImageName) return false;
        }
        for (int index = 0; index < left.Constants.Count; index++)
            if (!left.Constants[index].Span.SequenceEqual(right.Constants[index].Span)) return false;
        for (int index = 0; index < left.States.Count; index++)
            if (!left.States[index].Span.SequenceEqual(right.States[index].Span)) return false;
        return true;
    }

    private Iw3Ps3TechniqueSource? CopyTechnique(Iw3Ps3TechniqueSource? source)
    {
        if (source is null) return null;
        if (_techniques.TryGetValue(source, out Iw3Ps3TechniqueSource? copy)) return copy;
        copy = new Iw3Ps3TechniqueSource(source.Name, source.Root.ToArray(),
            source.Passes.Select(CopyPass).ToArray());
        _techniques.Add(source, copy);
        return copy;
    }

    private Iw3Ps3PassSource CopyPass(Iw3Ps3PassSource source)
    {
        if (_passes.TryGetValue(source, out Iw3Ps3PassSource? copy)) return copy;
        copy = new Iw3Ps3PassSource(source.Root.ToArray(), source.Declaration.ToArray(),
            CopyShader(source.VertexShader), CopyShader(source.PixelShader),
            source.Arguments.Select(argument => new Iw3Ps3ArgumentSource(
                argument.Root.ToArray(), argument.Literal.ToArray())).ToArray());
        _passes.Add(source, copy);
        return copy;
    }

    private Iw3Ps3ShaderSource? CopyShader(Iw3Ps3ShaderSource? source)
    {
        if (source is null) return null;
        if (source.Name.StartsWith(','))
            return new Iw3Ps3ShaderSource(source.Name, source.IsVertex,
                source.Root.ToArray(), source.Program.ToArray());
        if (!_shaders.TryGetValue((source.IsVertex, source.Name), out var owned))
            throw new InvalidDataException($"Missing native shader '{source.Name}' in technique set.");
        return owned;
    }

    private static bool SameSet(Iw3Ps3TechniqueSetSource left, Iw3Ps3TechniqueSetSource right)
    {
        if (left.Root.Length != 112 || right.Root.Length != 112 ||
            left.Root.Span[4] != right.Root.Span[4] ||
            left.Slots.Count != right.Slots.Count)
            return false;
        for (int index = 0; index < left.Slots.Count; index++)
        {
            Iw3Ps3TechniqueSource? a = left.Slots[index], b = right.Slots[index];
            if (a is null || b is null)
            {
                if (a is not null || b is not null) return false;
                continue;
            }
            if (a.Name != b.Name || a.Root.Length != 8 || b.Root.Length != 8 ||
                !a.Root.Span[4..8].SequenceEqual(b.Root.Span[4..8]) ||
                a.Passes.Count != b.Passes.Count) return false;
            for (int pass = 0; pass < a.Passes.Count; pass++)
            {
                Iw3Ps3PassSource x = a.Passes[pass], y = b.Passes[pass];
                if (x.Root.Length != 24 || y.Root.Length != 24 ||
                    !x.Root.Span[12..20].SequenceEqual(y.Root.Span[12..20]) ||
                    !x.Declaration.Span.SequenceEqual(y.Declaration.Span) ||
                    x.VertexShader?.Name != y.VertexShader?.Name ||
                    x.PixelShader?.Name != y.PixelShader?.Name ||
                    x.Arguments.Count != y.Arguments.Count) return false;
                for (int argument = 0; argument < x.Arguments.Count; argument++)
                {
                    Iw3Ps3ArgumentSource lhs = x.Arguments[argument];
                    Iw3Ps3ArgumentSource rhs = y.Arguments[argument];
                    if (lhs.Root.Length != 8 || rhs.Root.Length != 8 ||
                        !lhs.Root.Span[..4].SequenceEqual(rhs.Root.Span[..4]) ||
                        !lhs.Literal.Span.SequenceEqual(rhs.Literal.Span) ||
                        (lhs.Literal.IsEmpty &&
                            !lhs.Root.Span[4..8].SequenceEqual(rhs.Root.Span[4..8])))
                        return false;
                }
            }
        }
        return true;
    }

    internal Iw3Ps3ShaderSource Resolve(Iw3Ps3ShaderSource source)
    {
        if (!source.Name.StartsWith(',')) return source;
        if (_shaders.TryGetValue((source.IsVertex, source.Name[1..]), out var shader))
            return shader;
        string stops = _prefixStops.Count == 0 ? "" :
            " Shader-prefix traversal stopped at: " + string.Join(" | ", _prefixStops);
        throw new InvalidDataException(
            $"Missing COD4 PS3 {(source.IsVertex ? "vertex" : "pixel")} shader '{source.Name[1..]}'.{stops}");
    }
}

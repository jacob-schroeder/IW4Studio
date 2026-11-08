using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using IW4.Game.Assets;
using IW4.Game.Assets.Physics;
using IW4.Game.Assets.RawFile;
using IW4.Game.Assets.Image;
using MapConverter.Game.IW3.PC.Images;
using MapConverter.Game.IW3.PC.Extraction;
using MapConverter.Game.IW3.PS3.Materials;
using MapConverter.Game.IW3.PS3.Images;
using MapConverter.Game.IW3.PS3.Techniques;
using MapConverter.Game.IW3.PS3.FastFiles;
using static MapConverter.Game.IW3.PC.Extraction.Iw3PcSourceLibrary;

namespace MapConverter.Game.IW3.PS3.Extraction;

/// <summary>Catalogs the recovered COD4 PS3 asset layouts without converting their payloads.</summary>
internal static partial class Iw3Ps3FastFileCatalog
{
    private const int MaximumRetainedBytes = 256 * 1024 * 1024;
    private const int XFileHeaderSize = 36;

    internal static bool IsFastFile(string path) => Iw3Ps3FastFileFrames.IsFastFile(path);

    internal static Catalog Read(string path, CancellationToken cancellationToken)
    {
        var (data, logicalEnd) = ReadDecoded(path, cancellationToken);
        return new Reader(path, data, logicalEnd, cancellationToken).ReadCatalog();
    }

    internal static IReadOnlyList<BaseAsset> ReadMapAssets(
        string path, Iw3Ps3TechniqueCompiler techniques, Iw3Ps3ImageLibrary images,
        Iw3Ps3ShaderLibrary stockSources,
        CancellationToken token)
    {
        var (data, logicalEnd) = ReadDecoded(path, token);
        var reader = new Reader(path, data, logicalEnd, token, techniques, images,
            stockSources: stockSources);
        reader.ReadCatalog(selectedMapAssets: true);
        return reader.Definitions.Concat(techniques.CompiledShaders).ToArray();
    }

    internal static string? CaptureShaderPrefix(
        string path, byte[] data, int logicalEnd,
        Iw3Ps3ShaderLibrary shaders, CancellationToken token)
    {
        return new Reader(path, data, logicalEnd, token, shaderLibrary: shaders)
            .CaptureShaderPrefix();
    }

    internal static RawFileAsset? FindInlineRawFile(string path, string name, CancellationToken token)
    {
        var (data, end) = ReadDecoded(path, token);
        byte[] needle = Encoding.Latin1.GetBytes(name + '\0');
        int cursor = XFileHeaderSize + 12;
        while (cursor < end)
        {
            token.ThrowIfCancellationRequested();
            int relative = data.AsSpan(cursor, end - cursor).IndexOf(needle);
            if (relative < 0) break;
            int nameOffset = cursor + relative;
            int root = nameOffset - 12;
            cursor = nameOffset + needle.Length;
            uint length = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(root + 4));
            if (BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(root)) != uint.MaxValue ||
                BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(root + 8)) != uint.MaxValue ||
                length >= end - cursor || data[cursor + (int)length] != 0)
                continue;
            return new RawFileAsset
            {
                Name = name, Len = (int)length,
                Buffer = data.AsSpan(cursor, (int)length + 1).ToArray()
            };
        }
        return null;
    }

    internal static (byte[] Data, int LogicalEnd) ReadDecoded(string path, CancellationToken cancellationToken)
    {
        using var decoded = new MemoryStream();
        long maximumDecodedSize = MaximumRetainedBytes;
        int logicalEnd = 0;
        Iw3Ps3FastFileFrames.Read(path, frame =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (decoded.Length + frame.Length > maximumDecodedSize)
                throw new InvalidDataException($"IW3 PS3 fastfile '{path}' exceeds its decoded size limit.");
            decoded.Write(frame.Span);
            if (logicalEnd == 0 && decoded.Length >= XFileHeaderSize)
            {
                long end = (long)BinaryPrimitives.ReadUInt32BigEndian(decoded.GetBuffer()) + XFileHeaderSize;
                if (end > MaximumRetainedBytes || end < XFileHeaderSize + 16)
                    throw new InvalidDataException($"IW3 PS3 fastfile '{path}' has an invalid XFile size.");
                logicalEnd = (int)end;
                maximumDecodedSize = Math.Min(MaximumRetainedBytes,
                    end + Iw3Ps3FastFileFrames.DecodedFrameSize - 1);
                if (decoded.Length > maximumDecodedSize)
                    throw new InvalidDataException($"IW3 PS3 fastfile '{path}' exceeds its XFile size and frame padding.");
            }
        }, cancellationToken);
        if (logicalEnd == 0 || decoded.Length < logicalEnd)
            throw new InvalidDataException($"IW3 PS3 fastfile '{path}' has truncated decoded data.");
        if (decoded.GetBuffer().AsSpan(logicalEnd, (int)decoded.Length - logicalEnd).IndexOfAnyExcept((byte)0) >= 0)
            throw new InvalidDataException($"IW3 PS3 fastfile '{path}' has nonzero data after its XFile content.");
        return (decoded.ToArray(), logicalEnd);
    }

    private readonly record struct Address(int Block, int Offset)
    {
        internal Address Add(int offset) => new(Block, checked(Offset + offset));
    }

    private readonly record struct Region(Address Address, int FileOffset, int Length);

    // These are transient pointer-resolution facts, not a second catalog or asset model.
    private sealed record Pointer(string Kind, Region Region, int[] Dependencies, object? Value = null);

    private sealed partial class Reader
    {
        private readonly string _path;
        private readonly byte[] _data;
        private readonly int _end;
        private readonly CancellationToken _token;
        private readonly int[] _limits = new int[7];
        private readonly int[] _cursors = new int[7];
        private readonly Stack<(int Previous, int Saved)> _stack = new();
        private readonly Dictionary<Address, Pointer> _direct = [];
        private readonly Dictionary<Address, Pointer?> _aliases = [];
        private readonly List<Region> _strings = [];
        private readonly List<int> _deferred = [];
        private readonly List<CatalogAsset> _assets = [];
        private readonly HashSet<string> _skipped = new(StringComparer.Ordinal);
        private readonly bool _converting;
        private readonly bool _captureRenderSources;
        private readonly string _ownedNamePrefix;
        private readonly List<string?> _scriptStrings = [];
        private readonly List<BaseAsset> _definitions = [];
        internal IReadOnlyList<BaseAsset> Definitions => _definitions;
        private readonly Iw3Ps3TechniqueCompiler? _techniqueCompiler;
        private readonly Iw3Ps3MaterialCompiler? _materialCompiler;
        private readonly Iw3Ps3ImageLibrary? _imageLibrary;
        private readonly Iw3Ps3ShaderLibrary? _shaderLibrary;
        private readonly Iw3Ps3ShaderLibrary? _stockSources;
        private int _position = XFileHeaderSize;
        private int _block;
        private int _root = -1;
        private string? _shaderPrefixStopReason;
        private long _retainedBytes;

        internal Reader(string path, byte[] data, int end, CancellationToken token,
            Iw3Ps3TechniqueCompiler? techniqueCompiler = null,
            Iw3Ps3ImageLibrary? imageLibrary = null,
            Iw3Ps3ShaderLibrary? shaderLibrary = null,
            Iw3Ps3ShaderLibrary? stockSources = null)
        {
            _path = path;
            _data = data;
            _end = end;
            _token = token;
            _retainedBytes = data.Length;
            _converting = techniqueCompiler is not null;
            _captureRenderSources = _converting || shaderLibrary is not null;
            _ownedNamePrefix = techniqueCompiler?.OwnedNamePrefix ?? "";
            _techniqueCompiler = techniqueCompiler;
            _imageLibrary = imageLibrary;
            _shaderLibrary = shaderLibrary;
            _stockSources = stockSources;
            _materialCompiler = techniqueCompiler is null ? null : new(
                techniqueCompiler,
                (stockSources ?? throw new ArgumentNullException(nameof(stockSources))).ResolveMaterial,
                (imageLibrary ?? throw new ArgumentNullException(nameof(imageLibrary))).Resolve);
            for (int index = 0; index < 7; index++)
            {
                uint limit = U32(8 + index * 4);
                if (limit > int.MaxValue)
                    throw Invalid("XFile block size exceeds the supported address range");
                _limits[index] = (int)limit;
            }
        }

        internal string? CaptureShaderPrefix()
        {
            ReadCatalog(shaderPrefix: true);
            return _shaderPrefixStopReason;
        }

        internal Catalog ReadCatalog(bool selectedMapAssets = false, bool shaderPrefix = false)
        {
            Region list = ReadBytes(16);
            int stringCount = Count(U32(list.FileOffset));
            uint stringPointer = U32(list.FileOffset + 4);
            int assetCount = Count(U32(list.FileOffset + 8));
            uint assetPointer = U32(list.FileOffset + 12);
            Push(4);
            // Actual 0xb3e68 custom root path; ScriptStringList 0xd52d0 pushes block4.
            Push(4);
            if (stringPointer != 0)
            {
                Align(4);
                Region strings = ReadBytes(Size(stringCount, 4));
                for (int index = 0; index < stringCount; index++)
                    _scriptStrings.Add(ReadString(U32(strings.FileOffset + index * 4)));
            }
            else if (stringCount != 0)
                throw Invalid("script-string count has no pointer array");
            Pop();
            Pop();
            Push(4);
            if (assetPointer != 0)
            {
                Align(4);
                Region roots = ReadBytes(Size(assetCount, 8));
                for (_root = 0; _root < assetCount; _root++)
                {
                    _token.ThrowIfCancellationRequested();
                    int entry = roots.FileOffset + _root * 8;
                    uint type = U32(entry);
                    uint raw = U32(entry + 4);
                    Address cell = roots.Address.Add(_root * 8 + 4);
                    bool supportedRoot = true;
                    try
                    {
                        switch (type)
                        {
                            case 1: PhysPreset(raw, cell); break;
                            case 2: Animation(raw, cell); break;
                            case 3: Model(raw, cell); break;
                            case 4: Material(raw, cell); break;
                            case 5: Shader(raw, cell, vertex: false); break;
                            case 6: Shader(raw, cell, vertex: true); break;
                            case 7: TechniqueSet(raw, cell); break;
                            case 8: Image(raw, cell); break;
                            case 9 when shaderPrefix: SoundPrefix(raw, cell); break;
                            case 10 when shaderPrefix: SoundCurvePrefix(raw, cell); break;
                            case 10: SimpleAsset(raw, cell, "soundcurve", 72); break;
                            case 11 when shaderPrefix: LoadedSoundPrefix(raw, cell); break;
                            case 13 when _converting: ClipMap(raw, cell); break;
                            case 14 when _converting: ComWorld(raw, cell); break;
                            case 16 when _converting: GameWorld(raw, cell); break;
                            case 17 when _converting: MapEnts(raw, cell); break;
                            case 18 when _converting: World(raw, cell); break;
                            case 19 when _converting || shaderPrefix: LightDef(raw, cell); break;
                            case 22 when shaderPrefix: MenuListPrefix(raw, cell); break;
                            case 21 when shaderPrefix: FontPrefix(raw, cell); break;
                            case 27 when selectedMapAssets: supportedRoot = false; break;
                            case 27 when shaderPrefix: Fx(raw, cell); break;
                            case 28 when shaderPrefix: ImpactPrefix(raw, cell); break;
                            case 9 or 28 when selectedMapAssets: supportedRoot = false; break;
                            case 24: SimpleAsset(raw, cell, "localize", 8); break;
                            case 25 when shaderPrefix: WeaponPrefix(raw, cell); break;
                            case 26: _skipped.Add("snddriverglobals"); break; // No case in PS3 0xda6a0.
                            case 33: SimpleAsset(raw, cell, "rawfile", 12); break;
                            case 34: StringTable(raw, cell); break;
                            default:
                                if (!shaderPrefix) throw Unsupported($"XAsset type {type}");
                                _shaderPrefixStopReason = Unsupported($"XAsset type {type}").Message;
                                supportedRoot = false;
                                break;
                        }
                    }
                    catch (NotSupportedException error) when (shaderPrefix)
                    {
                        _shaderPrefixStopReason = error.Message;
                        break;
                    }
                    if (!supportedRoot) break;
                }
            }
            else if (assetCount != 0)
                throw Invalid("asset count has no root array");
            Pop();
            if (shaderPrefix)
                return new Catalog { Assets = _assets.ToArray(), SkippedAssetTypes = [] };
            if (selectedMapAssets)
            {
                if (!_definitions.Any(asset => asset is IW4.Game.Assets.ColMap.ClipMapAsset))
                    throw Invalid("source map has no collision world");
                return new Catalog { Assets = _assets.ToArray(), SkippedAssetTypes = [] };
            }
            // 0xe04c0 drains deferred source reads in insertion order, without padding.
            foreach (int size in _deferred)
            {
                _token.ThrowIfCancellationRequested();
                AdvanceFile(size);
            }
            if (_position != _end)
                throw Invalid($"asset traversal ended before XFile content (0x{_end:X})");
            if (_stack.Count != 0 || _block != 0)
                throw Invalid("unbalanced stream-position stack");
            return new Catalog { Assets = _assets.ToArray(), SkippedAssetTypes = _skipped.Order(StringComparer.Ordinal).ToArray() };
        }

        private Pointer? AssetPointer(uint raw, Address cell, string kind, int size, Func<Region, Pointer> body)
        {
            Push(0);
            try
            {
                Pointer? result;
                if (raw is not (uint.MaxValue or 0xfffffffe))
                    result = raw == 0 ? null : Alias(raw, kind);
                else
                {
                    Align(4);
                    Address? inserted = raw == 0xfffffffe ? InsertPointer() : null;
                    result = body(ReadBytes(size));
                    if (inserted is { } insert)
                        SetAlias(insert, result);
                }
                SetAlias(cell, result);
                return result;
            }
            finally { Pop(); }
        }

        private Pointer Named(string type, string? rawName, Region region, IEnumerable<int>? dependencies = null, object? value = null)
        {
            if (string.IsNullOrEmpty(rawName))
                throw Invalid($"{type} asset has no name");
            bool reference = rawName[0] == ',';
            string name = reference ? rawName[1..] : rawName;
            // Prefix capture retains native names but never writes a zone
            // manifest; stock XAnim names can contain trailing spaces.
            if (_shaderLibrary is null && !Iw3ZoneManifest.IsValidEntry(type, name))
                throw Invalid($"invalid {type} asset identity " +
                    JsonSerializer.Serialize(rawName.Length <= 256
                        ? rawName : rawName[..256]) +
                    $" (source length {rawName.Length})");
            // Native NormalizeAssetName uses bytewise ASCII case folding and slash replacement.
            // Valid catalog names already have no surrounding whitespace.
            string key = string.Create(name.Length, name, static (destination, source) =>
            {
                for (int index = 0; index < source.Length; index++)
                {
                    char value = source[index];
                    destination[index] = value is >= 'A' and <= 'Z' ? (char)(value + ('a' - 'A')) :
                        value == '\\' ? '/' : value;
                }
            });
            int[] edges = dependencies?.Distinct().Order().ToArray() ?? [];
            Retain(512L + name.Length * 4L + edges.Length * 16L);
            var asset = new CatalogAsset
            {
                Id = _assets.Count, Type = type, Name = name, Key = key, IsReference = reference,
                Dependencies = edges, References = [], StreamedSounds = []
            };
            _assets.Add(asset);
            if (_converting && value is BaseAsset definition && definition is not IW4.Game.Assets.MapEnts.MapEntsAsset)
                _definitions.Add(definition);
            return new Pointer(type, region, [asset.Id], value);
        }

        private Pointer? TechniqueSet(uint raw, Address cell) => AssetPointer(raw, cell, "techniqueset", 112, root =>
        {
            Push(4);
            try
            {
                string? name = ReadString(U32(root.FileOffset));
                var dependencies = new HashSet<int>();
                var slots = new Iw3Ps3TechniqueSource?[26];
                for (int slot = 0; slot < 26; slot++)
                {
                    Pointer? technique = Technique(U32(root.FileOffset + 8 + slot * 4));
                    Add(dependencies, technique);
                    slots[slot] = technique?.Value as Iw3Ps3TechniqueSource;
                }
                var source = _captureRenderSources ? new Iw3Ps3TechniqueSetSource(
                    name ?? throw Invalid("technique set has no name"), Bytes(root), slots) : null;
                if (source is not null) _shaderLibrary?.AddSet(source);
                if (_converting && source is not null)
                    _definitions.Add((_techniqueCompiler ?? throw Invalid("conversion has no technique compiler")).Compile(source).TechniqueSet);
                return Named("techniqueset", name, root, dependencies, source);
            }
            finally { Pop(); }
        });

        private Pointer? Technique(uint raw)
        {
            if (raw == 0) return null;
            if (raw != uint.MaxValue) return Direct(raw, "technique");
            Align(4);
            Region root = ReadBytes(8);
            int passCount = U16(root.FileOffset + 6);
            Region passes = ReadBytes(Size(passCount, 24));
            var dependencies = new HashSet<int>();
            var sourcePasses = new List<Iw3Ps3PassSource>();
            for (int index = 0; index < passCount; index++)
            {
                int pass = passes.FileOffset + index * 24;
                Address address = passes.Address.Add(index * 24);
                Pointer? declaration = ReadArray(U32(pass), 4, 34, "vertexdecl");
                if (declaration is not null && _data[declaration.Region.FileOffset] > 16)
                    throw Invalid("vertex declaration exceeds its sixteen source routes");
                Pointer? vertex = Shader(U32(pass + 4), address.Add(4), vertex: true);
                Pointer? pixel = Shader(U32(pass + 8), address.Add(8), vertex: false);
                Add(dependencies, vertex);
                Add(dependencies, pixel);
                var sourceArguments = new List<Iw3Ps3ArgumentSource>();
                if (U32(pass + 20) != 0)
                {
                    Align(4);
                    int count = _data[pass + 12] + _data[pass + 13] + _data[pass + 14];
                    Region arguments = ReadBytes(Size(count, 8));
                    for (int arg = 0; arg < count; arg++)
                    {
                        int argument = arguments.FileOffset + arg * 8;
                        Pointer? literal = U16(argument) is 1 or 7
                            ? ReadArray(U32(argument + 4), 4, 16, "literal") : null;
                        if (_captureRenderSources) sourceArguments.Add(new(
                            _data.AsMemory(argument, 8), literal is null ? default : Bytes(literal.Region)));
                    }
                }
                if (_captureRenderSources) sourcePasses.Add(new(_data.AsMemory(pass, 24),
                    declaration is null ? default : Bytes(declaration.Region),
                    vertex?.Value as Iw3Ps3ShaderSource, pixel?.Value as Iw3Ps3ShaderSource,
                    sourceArguments));
            }
            string? name = ReadString(U32(root.FileOffset)); // Name follows every pass and its children.
            return Remember(new Pointer("technique", root, dependencies.Order().ToArray(),
                _captureRenderSources ? new Iw3Ps3TechniqueSource(name ?? throw Invalid("technique has no name"),
                    Bytes(root), sourcePasses) : null));
        }

        private Pointer? Shader(uint raw, Address cell, bool vertex)
        {
            string type = vertex ? "vertexshader" : "pixelshader";
            return AssetPointer(raw, cell, type, vertex ? 12 : 20, root =>
            {
                Push(4);
                try
                {
                    string? name = ReadString(U32(root.FileOffset));
                    ReadOnlyMemory<byte> program = Program(U32(root.FileOffset + 4), Count(U32(root.FileOffset + 8)), vertex);
                    Iw3Ps3ShaderSource? source = _captureRenderSources
                        ? new Iw3Ps3ShaderSource(name ?? throw Invalid("shader has no name"),
                            vertex, Bytes(root), program)
                        : null;
                    if (source is not null) _shaderLibrary?.Add(source);
                    return Named(type, name, root, value: source);
                }
                finally { Pop(); }
            });
        }

        private ReadOnlyMemory<byte> Program(uint raw, int byteCount, bool vertex)
        {
            Push(0);
            try
            {
                if (raw == 0) return default;
                string kind = vertex ? "vertex-program" : "pixel-program";
                if (raw is not (uint.MaxValue or 0xfffffffe))
                {
                    Pointer? previous = Alias(raw, kind);
                    if (previous is null || previous.Region.Length != byteCount)
                        throw Invalid("aliased Cg program length disagrees with its shader wrapper");
                    return Bytes(previous.Region);
                }
                Align(16);
                Address? inserted = raw == 0xfffffffe ? InsertPointer() : null;
                Region program = ReadBytes(byteCount);
                int start = program.FileOffset;
                if (byteCount < 32 || U32(start) != (vertex ? 7003u : 7004u) || U32(start + 4) != 6 || U32(start + 8) != byteCount)
                    throw Invalid("native Cg header disagrees with its shader wrapper");
                int count = Count(U32(start + 12));
                int parameters = Range(program, U32(start + 16), Size(count, 48));
                Range(program, U32(start + 20), 24);
                Range(program, U32(start + 28), Count(U32(start + 24)));
                for (int index = 0; index < count; index++)
                {
                    _token.ThrowIfCancellationRequested();
                    uint name = U32(parameters + index * 48 + 16);
                    if (name != 0)
                    {
                        int offset = Range(program, name, 1);
                        if (_data.AsSpan(offset, start + byteCount - offset).IndexOf((byte)0) < 0)
                            throw Invalid("unterminated Cg parameter name");
                    }
                }
                if (inserted is { } insert)
                    SetAlias(insert, new Pointer(kind, program, []));
                return Bytes(program);
            }
            finally { Pop(); }
        }

        private Pointer? Material(uint raw, Address cell) => AssetPointer(raw, cell, "material", 128, root =>
        {
            Push(4);
            try
            {
                int start = root.FileOffset;
                string? name = ReadString(U32(start));
                Push(1);
                try
                {
                    if (U32(start + 0x6c) != 0) { Align(2); Reserve(52); }
                }
                finally { Pop(); }
                var dependencies = new HashSet<int>();
                Pointer? techniqueSet = TechniqueSet(U32(start + 0x70), root.Address.Add(0x70));
                Add(dependencies, techniqueSet);
                IReadOnlyList<Iw3Ps3TextureSource> sourceTextures = [];
                uint rawTextures = U32(start + 0x74);
                int textureCount = _data[start + 0x32];
                if (rawTextures != 0)
                {
                    Pointer? previous = rawTextures == uint.MaxValue ? null : Direct(rawTextures, "textures", Size(textureCount, 12));
                    if (previous is not null)
                    {
                        Add(dependencies, previous);
                        sourceTextures = previous.Value as IReadOnlyList<Iw3Ps3TextureSource> ?? [];
                    }
                    else
                    {
                        Align(4);
                        Region textures = ReadBytes(Size(textureCount, 12));
                        var textureDependencies = new HashSet<int>();
                        var sourceRows = new List<Iw3Ps3TextureSource>();
                        for (int index = 0; index < textureCount; index++)
                        {
                            int texture = textures.FileOffset + index * 12;
                            uint child = U32(texture + 8);
                            Pointer? image = null;
                            if (_data[texture + 7] == 11)
                            {
                                if (child != 0) throw Unsupported("material water child (0xd0730)");
                            }
                            else
                            {
                                image = Image(child, textures.Address.Add(index * 12 + 8));
                                Add(textureDependencies, image);
                            }
                            if (_captureRenderSources)
                                sourceRows.Add(new(_data.AsMemory(texture, 12),
                                    image?.Value as GfxImageAsset,
                                    image is null ? null : _assets[image.Dependencies.Single()].Name));
                        }
                        sourceTextures = sourceRows;
                        Pointer table = Remember(new Pointer("textures", textures, textureDependencies.Order().ToArray(), sourceRows));
                        Add(dependencies, table);
                    }
                }
                Pointer? constants = ReadArray(U32(start + 0x78), 16, Size(_data[start + 0x33], 32), "material-constants");
                uint rawStates = U32(start + 0x7c);
                int stateCount = _data[start + 0x34];
                IReadOnlyList<ReadOnlyMemory<byte>> sourceStates = [];
                if (rawStates != 0)
                {
                    if (rawStates != uint.MaxValue)
                        sourceStates = Direct(rawStates, "material-states", Size(stateCount, 4)).Value
                            as IReadOnlyList<ReadOnlyMemory<byte>> ?? [];
                    else
                    {
                        Align(4);
                        Region states = ReadBytes(Size(stateCount, 4));
                        var stateRows = new List<ReadOnlyMemory<byte>>();
                        for (int index = 0; index < stateCount; index++)
                        {
                            Pointer? state = AssetPointer(U32(states.FileOffset + index * 4), states.Address.Add(index * 4), "material-state", 8,
                                bits => new Pointer("material-state", bits, []));
                            if (_captureRenderSources)
                                stateRows.Add(state is null ? default : Bytes(state.Region));
                        }
                        sourceStates = stateRows;
                        Remember(new Pointer("material-states", states, [], stateRows));
                    }
                }
                object? value = null;
                if (_captureRenderSources)
                {
                    var sourceConstants = new List<ReadOnlyMemory<byte>>();
                    if (constants is not null)
                        for (int index = 0; index < _data[start + 0x33]; index++)
                            sourceConstants.Add(_data.AsMemory(constants.Region.FileOffset + index * 32, 32));
                    var source = new Iw3Ps3MaterialSource(name ?? throw Invalid("material has no name"),
                        Bytes(root), techniqueSet?.Value as Iw3Ps3TechniqueSetSource,
                        sourceTextures, sourceConstants, sourceStates);
                    _shaderLibrary?.AddMaterial(source);
                    if (_converting)
                        value = (_materialCompiler ?? throw Invalid("conversion has no material compiler")).Compile(source);
                }
                return Named("material", name, root, dependencies, value);
            }
            finally { Pop(); }
        });

        private Pointer? Image(uint raw, Address cell) => AssetPointer(raw, cell, "image", 52, root =>
        {
            Push(4);
            try
            {
                int start = root.FileOffset;
                string? name = ReadString(U32(start + 0x30));
                int pixelBlock = _data[start + 0x2b] == 0
                    ? (_data[start + 0x12] == 1 ? 5 : 6)
                    : (_data[start + 0x12] == 1 ? 2 : 3);
                Push(pixelBlock);
                try
                {
                    // 0xc9d00 treats every nonnull pixel pointer as a fresh allocation.
                    if (U32(start + 0x2c) != 0)
                    {
                        int size = ImageSize(start);
                        Align(128);
                        if (pixelBlock is 2 or 3)
                        {
                            Reserve(size);
                            if (size != 0) { Retain(16); _deferred.Add(size); }
                        }
                        else ReadBytes(size);
                    }
                }
                finally { Pop(); }
                object? value = null;
                if (_converting)
                {
                    string imageName = name ?? throw Invalid("image has no name");
                    Iw3Ps3ImageLibrary library = _imageLibrary ??
                        throw Invalid("conversion has no image library");
                    value = imageName.StartsWith(',')
                        ? library.Resolve(imageName)
                        : library.ResolveAt(_path, start, imageName);
                }
                return Named("image", name, root, value: value);
            }
            finally { Pop(); }
        });

        private int ImageSize(int root)
        {
            var format = new GfxImageFormat(_data[root + 4]);
            var remap = new GfxImageTextureRemap(U32(root + 8));
            uint key = GfxImagePixelLayout.BuildFormatKey(format, remap);
            if (key is not (0x01AAE486 or 0x01AAE487 or 0x01AAE488 or 0x01A9FF81 or 0x0156FF81 or
                0x01AAE485 or 0x00AAFE9F or 0x01AAE49C or 0x01AAE490 or 0x01AAE49E or 0x01AAE492 or 0x01AAFE8B))
                throw Unsupported($"COD4 image format/remap 0x{key:X8}");
            ushort width = U16(root + 12), height = U16(root + 14), depth = U16(root + 16);
            if (width == 0 || height == 0 || depth == 0 || _data[root + 5] == 0 || _data[root + 5] > 16)
                throw Invalid("invalid image dimensions or mip count");
            // COD4's 0x01AAFE8B and IW4's 0x01AAAB8B both use two bytes per texel.
            // This changes only the sizing key; no source texture or conversion format is changed.
            if (key == 0x01AAFE8B)
                remap = new GfxImageTextureRemap(0x0001AAAB);
            return GfxImagePixelLayout.ComputePayloadByteCount(format, _data[root + 5], _data[root + 7] != 0,
                remap, width, height, depth);
        }

        private Pointer? Animation(uint raw, Address cell) => AssetPointer(raw, cell, "xanim", 0x5c, root =>
        {
            // Current d3f68 follows the source delta before scalar arrays.
            Push(4);
            try
            {
                int start = root.FileOffset;
                string? name = ReadString(U32(start));
                PresenceArray(U32(start + 0x34), 2, Size(_data[start + 0x1d], 2), "animation-bone-names");
                PresenceArray(U32(start + 0x54), 4, Size(_data[start + 0x1e], 8), "animation-notifications");
                AnimationDeltaPrefix(U32(start + 0x58), U16(start + 0xe));
                PresenceArray(U32(start + 0x38), 1, U16(start + 4), "animation-bytes");
                PresenceArray(U32(start + 0x3c), 2, Size(U16(start + 6), 2), "animation-shorts");
                PresenceArray(U32(start + 0x40), 4, Size(U16(start + 8), 4), "animation-ints");
                PresenceArray(U32(start + 0x44), 2, Size(Count(U32(start + 0x24)), 2), "animation-random-shorts");
                PresenceArray(U32(start + 0x48), 1, U16(start + 10), "animation-random-bytes");
                PresenceArray(U32(start + 0x4c), 4, Size(U16(start + 12), 4), "animation-random-ints");
                int indexSize = U16(start + 14) < 256 ? 1 : 2;
                PresenceArray(U32(start + 0x50), indexSize, Size(Count(U32(start + 0x28)), indexSize), "animation-indices");
                return Named("xanim", name, root);
            }
            finally { Pop(); }
        });

        private Pointer? Model(uint raw, Address cell) => AssetPointer(raw, cell, "xmodel", 0xcc, root =>
        {
            Push(4);
            try
            {
                int start = root.FileOffset;
                string? name = ReadString(U32(start));
                int bones = _data[start + 4], rootBones = _data[start + 5], surfaces = _data[start + 6];
                if (rootBones > bones) throw Invalid("model root-bone count exceeds bone count");
                Pointer? boneNames = ReadArray(U32(start + 8), 2, Size(bones, 2), "model-bone-names");
                Pointer? parents = ReadArray(U32(start + 12), 1, bones - rootBones, "model-parents");
                Pointer? quats = ReadArray(U32(start + 16), 2, Size(bones - rootBones, 8), "model-quaternions");
                Pointer? translations = ReadArray(U32(start + 20), 4, Size(bones - rootBones, 16), "model-translations");
                Pointer? classification = ReadArray(U32(start + 24), 1, bones, "model-part-classification");
                Pointer? baseMatrices = ReadArray(U32(start + 28), 4, Size(bones, 32), "model-base-matrices");
                var sourceSurfaces = new List<ModelSurfaceSource>(surfaces);
                if (U32(start + 0x20) != 0)
                {
                    Align(4);
                    Region roots = ReadBytes(Size(surfaces, 0x4c));
                    for (int index = 0; index < surfaces; index++)
                    {
                        ModelSurfaceSource? sourceSurface = Surface(roots, index);
                        if (sourceSurface is not null) sourceSurfaces.Add(sourceSurface);
                    }
                }
                var dependencies = new HashSet<int>();
                var materials = new List<Pointer?>(surfaces);
                if (U32(start + 0x24) != 0)
                {
                    Align(4);
                    Region handles = ReadBytes(Size(surfaces, 4));
                    for (int index = 0; index < surfaces; index++)
                    {
                        Pointer? material = Material(U32(handles.FileOffset + index * 4), handles.Address.Add(index * 4));
                        Add(dependencies, material);
                        materials.Add(material);
                    }
                }
                int collisionCount = Count(U32(start + 0x8c));
                Pointer? collision = ReadArray(U32(start + 0x88), 4,
                    Size(collisionCount, 0x24), "model-collision-surfaces");
                Pointer? boneInfo = PresenceArray(U32(start + 0x94), 4,
                    Size(bones, 40), "model-bone-info");
                Pointer? preset = PhysPreset(U32(start + 0xc4), root.Address.Add(0xc4));
                Add(dependencies, preset);
                PhysCollmapAsset? physCollmap = PhysGeomList(U32(start + 0xc8), name ?? "", root);
                bool captureModel = _converting ||
                    (_shaderLibrary is not null && name?.StartsWith(',') == false);
                Iw3Ps3ModelSource? sourceModel = null;
                if (captureModel)
                {
                    string modelName = name ?? throw Invalid("model has no name");
                    if (!_converting && U32(start + 0xc8) != 0)
                        _shaderLibrary?.AddUnsupportedModel(modelName, "source PhysCollmap geometry");
                    else try
                    {
                        sourceModel = CaptureModelSource(
                            modelName, root, boneNames, parents,
                            quats, translations, classification, baseMatrices, sourceSurfaces,
                            materials, collision, boneInfo, preset);
                    }
                    catch (NotSupportedException error) when (!_converting && _shaderLibrary is not null)
                    {
                        _shaderLibrary.AddUnsupportedModel(modelName, error.Message);
                    }
                }
                if (sourceModel is not null) _shaderLibrary?.AddModel(sourceModel);
                object? value = null;
                if (_converting)
                {
                    if (sourceModel is null) throw Invalid("conversion has no model source");
                    if (name?.StartsWith(',') == true)
                        sourceModel = (_stockSources ?? throw Invalid("conversion has no stock source library"))
                            .ResolveModel(name[1..]);
                    value = CompileModelSource(sourceModel, physCollmap,
                        name?.StartsWith(',') == true ? null : materials,
                        name?.StartsWith(',') == true ? null : preset);
                }
                return Named("xmodel", name, root, dependencies, value);
            }
            finally { Pop(); }
        });

        private ModelSurfaceSource? Surface(Region roots, int index)
        {
            int root = roots.FileOffset + index * 0x4c;
            int blendWords = U16(root + 12) + U16(root + 14) * 3 + U16(root + 16) * 5 + U16(root + 18) * 7;
            Pointer? blend = ReadArray(U32(root + 0x14), 2, Size(blendWords, 2), "surface-blend-indices");
            Pointer? vertices0 = ReadArray(U32(root + 0x18), 16, Size(U16(root + 2), 16), "surface-vertices0");
            bool streamed = _data[root + 1] == 0 && U32(root + 0x30) == 1;
            if (streamed) Push(6);
            Pointer? vertices1;
            try { vertices1 = ReadArray(U32(root + 0x24), 16, Size(U16(root + 2), 16), "surface-vertices1"); }
            finally { if (streamed) Pop(); }
            uint rawLists = U32(root + 0x34);
            int listCount = Count(U32(root + 0x30));
            Pointer? lists = ReadArray(rawLists, 4, Size(listCount, 12), "surface-rigid-lists");
            if (lists is not null && rawLists == uint.MaxValue)
            {
                var rigidRows = new List<ModelRigidSource>(listCount);
                for (int rigidIndex = 0; rigidIndex < listCount; rigidIndex++)
                {
                    uint rawTree = U32(lists.Region.FileOffset + rigidIndex * 12 + 8);
                    Pointer? tree = ReadArray(rawTree, 4, 40, "surface-collision-tree");
                    if (tree is not null && rawTree == uint.MaxValue)
                    {
                        int start = tree.Region.FileOffset;
                        Pointer? nodes = PresenceArray(U32(start + 0x1c), 16,
                            Size(Count(U32(start + 0x18)), 16), "surface-collision-nodes");
                        Pointer? leaves = PresenceArray(U32(start + 0x24), 2,
                            Size(Count(U32(start + 0x20)), 2), "surface-collision-leaves");
                        if (_captureRenderSources) tree = Remember(tree with { Value = new ModelTreeSource(tree.Region, nodes, leaves) });
                    }
                    if (_captureRenderSources) rigidRows.Add(new ModelRigidSource(
                        new Region(lists.Region.Address.Add(rigidIndex * 12), lists.Region.FileOffset + rigidIndex * 12, 12),
                        tree));
                }
                if (_captureRenderSources) lists = Remember(lists with { Value = rigidRows });
            }
            Pointer? triangles = ReadArray(U32(root + 8), 16,
                Size(U16(root + 4), 6), "surface-triangle-indices");
            return !_captureRenderSources ? null : new ModelSurfaceSource(
                new Region(roots.Address.Add(index * 0x4c), root, 0x4c),
                blend, vertices0, vertices1, lists, triangles, streamed);
        }

        // PS3 0xcea90/0xcea20: TEMP asset pointer, 0x2c-byte root, then two block4 XStrings.
        private Pointer? PhysPreset(uint raw, Address cell) => SimpleAsset(raw, cell, "physpreset", 0x2c);

        private Pointer? SimpleAsset(uint raw, Address cell, string kind, int size) => AssetPointer(raw, cell, kind, size, root =>
        {
            Push(4);
            try
            {
                int start = root.FileOffset;
                string? name;
                if (kind == "localize")
                {
                    ReadString(U32(start));
                    name = ReadString(U32(start + 4));
                }
                else name = ReadString(U32(start));
                string? soundPrefix = kind == "physpreset"
                    ? ReadString(U32(start + 0x1c)) : null;
                if (kind == "physpreset" && _shaderLibrary is not null)
                    _shaderLibrary.AddPreset(name ?? throw Invalid("physics preset has no name"),
                        Bytes(root), soundPrefix);
                byte[]? buffer = null;
                if (kind == "rawfile" && U32(start + 8) != 0)
                {
                    int length = Count(U32(start + 4));
                    if (length == int.MaxValue) throw Invalid("rawfile byte count overflows");
                    Region text = ReadBytes(length + 1);
                    if (_data[text.FileOffset + length] != 0) throw Invalid("rawfile buffer has no terminating NUL");
                    if (_converting) buffer = Bytes(text).ToArray();
                }
                object? value = null;
                if (_converting && kind == "physpreset")
                {
                    string sourceName = name ?? throw Invalid("physics preset has no name");
                    ReadOnlyMemory<byte> sourceRoot = Bytes(root);
                    if (sourceName.StartsWith(','))
                    {
                        sourceName = sourceName[1..];
                        (sourceRoot, soundPrefix) = (_stockSources ??
                            throw Invalid("conversion has no stock source library")).ResolvePreset(sourceName);
                    }
                    value = CompilePresetSource(sourceName, sourceRoot, soundPrefix);
                }
                else if (_converting && kind == "rawfile")
                    value = new RawFileAsset { Name = name, Len = Count(U32(start + 4)), Buffer = buffer };
                return Named(kind, name, root, value: value);
            }
            finally { Pop(); }
        });

        private PhysPresetAsset CompilePresetSource(
            string name, ReadOnlyMemory<byte> root, string? soundPrefix)
        {
            ReadOnlySpan<byte> source = root.Span;
            if (source.Length != 0x2c)
                throw Invalid($"physics preset '{name}' has an invalid source root");
            return new PhysPresetAsset
            {
                Name = _ownedNamePrefix + name,
                Type = BinaryPrimitives.ReadInt32BigEndian(source[4..]),
                Mass = BinaryPrimitives.ReadSingleBigEndian(source[8..]),
                Bounce = BinaryPrimitives.ReadSingleBigEndian(source[12..]),
                Friction = BinaryPrimitives.ReadSingleBigEndian(source[16..]),
                BulletForceScale = BinaryPrimitives.ReadSingleBigEndian(source[20..]),
                ExplosiveForceScale = BinaryPrimitives.ReadSingleBigEndian(source[24..]),
                SndAliasPrefix = soundPrefix,
                PiecesSpreadFraction = BinaryPrimitives.ReadSingleBigEndian(source[32..]),
                PiecesUpwardVelocity = BinaryPrimitives.ReadSingleBigEndian(source[36..]),
                TempDefaultToCylinder = source[40]
            };
        }

        private Pointer? StringTable(uint raw, Address cell)
        {
            Pointer? result;
            if (raw == 0) result = null;
            else if (raw != uint.MaxValue) result = Direct(raw, "stringtable");
            else
            {
                Align(4);
                Region root = ReadBytes(16);
                string? name = ReadString(U32(root.FileOffset));
                if (U32(root.FileOffset + 12) != 0)
                {
                    int cells = Size(Count(U32(root.FileOffset + 4)), Count(U32(root.FileOffset + 8)));
                    Align(4);
                    Region pointers = ReadBytes(Size(cells, 4));
                    for (int index = 0; index < cells; index++) ReadString(U32(pointers.FileOffset + index * 4));
                }
                result = Remember(Named("stringtable", name, root));
            }
            SetAlias(cell, result);
            return result;
        }

        private Pointer? ReadArray(uint raw, int alignment, int size, string kind)
        {
            if (raw == 0) return null;
            if (raw != uint.MaxValue) return Direct(raw, kind, size);
            return PresenceArray(raw, alignment, size, kind);
        }

        private Pointer? PresenceArray(uint raw, int alignment, int size, string kind)
        {
            if (raw == 0) return null;
            Align(alignment);
            return Remember(new Pointer(kind, ReadBytes(size), []));
        }

        private Pointer Remember(Pointer pointer)
        {
            // Empty arrays reserve an address but can overlap the next actual object.
            if (pointer.Region.Length != 0)
            {
                if (!_direct.ContainsKey(pointer.Region.Address)) Retain(192L + pointer.Dependencies.Length * 8L);
                _direct[pointer.Region.Address] = pointer;
            }
            return pointer;
        }

        private Pointer Direct(uint raw, string kind, int? size = null)
        {
            Address address = Decode(raw, kind);
            if (!_direct.TryGetValue(address, out Pointer? pointer) || pointer.Kind != kind)
                throw Unsupported($"unresolved direct {kind} pointer 0x{raw:X8}");
            // Reusing a larger dependency-bearing table would introduce extra graph edges.
            if (size is { } length && pointer.Region.Length != length)
                throw Unsupported($"non-exact {kind} array reference span");
            return pointer;
        }

        private Pointer? Alias(uint raw, string kind)
        {
            Address address = Decode(raw, kind);
            if (!_aliases.TryGetValue(address, out Pointer? pointer))
                throw Unsupported($"unresolved alias cell for {kind}: 0x{raw:X8}");
            if (pointer is not null && pointer.Kind != kind)
                throw Invalid($"alias cell for {kind} contains {pointer.Kind}");
            return pointer;
        }

        private void SetAlias(Address address, Pointer? pointer)
        {
            if (!_aliases.ContainsKey(address)) Retain(128);
            _aliases[address] = pointer;
        }

        private string? ReadString(uint raw)
        {
            _token.ThrowIfCancellationRequested();
            if (raw == 0) return null;
            int offset;
            int maximum;
            if (raw == uint.MaxValue)
            {
                offset = _position;
                maximum = _end - offset;
            }
            else
            {
                Address address = Decode(raw, "XString");
                Region? found = null;
                for (int index = _strings.Count - 1; index >= 0; index--)
                {
                    Region region = _strings[index];
                    if (region.Address.Block == address.Block && address.Offset >= region.Address.Offset &&
                        address.Offset - region.Address.Offset < region.Length)
                    {
                        found = region;
                        break;
                    }
                }
                if (found is not { } source)
                {
                    string previous = _strings.Count == 0 ? "none" :
                        $"block {_strings[^1].Address.Block}:0x{_strings[^1].Address.Offset:X}, decoded 0x{_strings[^1].FileOffset:X}";
                    throw Unsupported($"unresolved XString pointer 0x{raw:X8}; last inline string: {previous}");
                }
                int displacement = address.Offset - source.Address.Offset;
                offset = source.FileOffset + displacement;
                maximum = source.Length - displacement;
            }
            int length = _data.AsSpan(offset, maximum).IndexOf((byte)0);
            if (length < 0) throw Invalid("unterminated XString");
            Retain(96L + length * 2L);
            string result = Encoding.Latin1.GetString(_data, offset, length);
            if (raw == uint.MaxValue)
                _strings.Add(ReadBytes(length + 1));
            return result;
        }

        private Address Decode(uint raw, string? kind = null)
        {
            if (raw is 0 or uint.MaxValue or 0xfffffffe) throw Invalid("invalid packed offset sentinel");
            uint value = raw - 1;
            int block = (int)(value >> 29), offset = (int)(value & 0x1fffffff);
            if (block >= 7 || offset >= _limits[block])
                throw Invalid($"packed{(kind is null ? "" : " " + kind)} pointer 0x{raw:X8} is outside its block");
            return new Address(block, offset);
        }

        private Region ReadBytes(int size)
        {
            _token.ThrowIfCancellationRequested();
            if (_block is 1 or 2 or 3) throw Invalid("serialized read attempted in a runtime block");
            Address address = Reserve(size);
            int start = _position;
            AdvanceFile(size);
            return new Region(address, start, size);
        }

        private Address Reserve(int size)
        {
            if (size < 0 || size > _limits[_block] - _cursors[_block])
                throw Invalid($"allocation exceeds block {_block}");
            var address = new Address(_block, _cursors[_block]);
            _cursors[_block] += size;
            return address;
        }

        private void AdvanceFile(int size)
        {
            if (size < 0 || size > _end - _position) throw Invalid("serialized read exceeds XFile content");
            _position += size;
        }

        private void Align(int alignment)
        {
            long aligned = ((long)_cursors[_block] + alignment - 1) & -(long)alignment;
            if (aligned > _limits[_block]) throw Invalid($"alignment exceeds block {_block}");
            _cursors[_block] = (int)aligned;
        }

        private void Push(int block)
        {
            if (_stack.Count >= 64) throw Invalid("stream-position nesting limit exceeded");
            _stack.Push((_block, _cursors[block]));
            _block = block;
        }

        private void Pop()
        {
            if (!_stack.TryPop(out var saved)) throw Invalid("unbalanced stream-position pop");
            if (_block == 0)
            {
                _cursors[0] = saved.Saved;
                foreach (Address key in _direct.Keys.Where(key => key.Block == 0 && key.Offset >= saved.Saved).ToArray())
                    _direct.Remove(key);
                foreach (Address key in _aliases.Keys.Where(key => key.Block == 0 && key.Offset >= saved.Saved).ToArray())
                    _aliases.Remove(key);
                _strings.RemoveAll(region => region.Address.Block == 0 && region.Address.Offset >= saved.Saved);
            }
            _block = saved.Previous;
        }

        private Address InsertPointer()
        {
            Push(4);
            try { Align(4); return Reserve(4); }
            finally { Pop(); }
        }

        private int Range(Region region, uint offset, int size)
        {
            if (offset > region.Length || size < 0 || size > region.Length - (long)offset)
                throw Invalid("Cg region is outside its declared container");
            return region.FileOffset + (int)offset;
        }

        private int Count(uint value)
        {
            if (value > int.MaxValue) throw Invalid("array count exceeds the supported range");
            return (int)value;
        }

        private int Size(int count, int stride)
        {
            long size = (long)count * stride;
            if (size < 0 || size > _end) throw Invalid("array byte count exceeds XFile content");
            return (int)size;
        }

        private uint U32(int offset) => BinaryPrimitives.ReadUInt32BigEndian(_data.AsSpan(offset, 4));
        private ushort U16(int offset) => BinaryPrimitives.ReadUInt16BigEndian(_data.AsSpan(offset, 2));
        private ReadOnlyMemory<byte> Bytes(Region region) => _data.AsMemory(region.FileOffset, region.Length);

        private void Retain(long estimatedBytes)
        {
            // Includes conservative collection/object overhead as well as retained source bytes.
            _retainedBytes += estimatedBytes;
            if (_retainedBytes > MaximumRetainedBytes) throw Invalid("catalog exceeds the 256 MiB retained-memory budget");
        }

        private static void Add(HashSet<int> dependencies, Pointer? pointer)
        {
            if (pointer is not null) dependencies.UnionWith(pointer.Dependencies);
        }

        private InvalidDataException Invalid(string message) => new(
            $"IW3 PS3 fastfile '{_path}', root {_root}, decoded offset 0x{_position:X}: {message}.");

        private NotSupportedException Unsupported(string message) => new(
            $"IW3 PS3 fastfile '{_path}', root {_root}, decoded offset 0x{_position:X}: unsupported {message}.");
    }
}

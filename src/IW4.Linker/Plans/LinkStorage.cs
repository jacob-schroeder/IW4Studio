using System.Buffers.Binary;
using System.Text;
using IW4.FastFiles.Zone;

namespace IW4.Linker.Plans;

internal enum LinkMaterializationKind
{
    SourceBytes,
    RuntimeZeroFill,
    VirtualReservation,
    VertexReservation
}

/// <summary>
/// Request-scoped physical storage identity. Equality is deliberately object
/// identity: equal values, names, and serialized bytes do not imply sharing.
/// </summary>
internal sealed class LinkStorageSymbol
{
    private LinkStorageSymbol(LinkStorageDefinition definition) =>
        Definition = definition;

    public LinkStorageDefinition Definition { get; }

    public static LinkStorageSymbol SourceBytes(
        XFileBlockType block,
        ReadOnlySpan<byte> sourceTemplate,
        int alignment,
        Func<LinkStorageSymbol, IEnumerable<LinkOperation>>? operations = null)
    {
        LinkStorageSymbol symbol = CreateSourceBytes(
            block,
            sourceTemplate,
            alignment);
        symbol.FreezeOperations(operations?.Invoke(symbol) ?? []);
        return symbol;
    }

    /// <summary>
    /// Creates the physical symbol before its relocations are known. This is
    /// reserved for native structures whose direct pointers form forward or
    /// cyclic graphs; callers must close the symbol exactly once before the
    /// plan is traversed or emitted.
    /// </summary>
    internal static LinkStorageSymbol CreateSourceBytes(
        XFileBlockType block,
        ReadOnlySpan<byte> sourceTemplate,
        int alignment) =>
        new(new LinkStorageDefinition(
            block,
            sourceTemplate.Length,
            alignment,
            LinkMaterializationKind.SourceBytes,
            sourceTemplate.ToArray()));

    internal static LinkStorageSymbol CreatePendingSourceBytes(
        XFileBlockType block,
        int byteLength,
        int alignment) =>
        new(new LinkStorageDefinition(
            block,
            byteLength,
            alignment,
            LinkMaterializationKind.SourceBytes,
            sourceTemplate: null,
            pendingSourceTemplate: true));

    internal void FreezeSourceBytes(
        ReadOnlySpan<byte> sourceTemplate,
        IEnumerable<LinkOperation> operations)
    {
        Definition.FreezeSourceTemplate(sourceTemplate);
        Definition.FreezeOperations(operations);
    }

    internal void FreezeOperations(IEnumerable<LinkOperation> operations) =>
        Definition.FreezeOperations(operations);

    public static LinkStorageSymbol SourceFree(
        XFileBlockType block,
        int byteLength,
        int alignment,
        LinkMaterializationKind kind,
        Func<LinkStorageSymbol, IEnumerable<LinkOperation>>? operations = null)
    {
        if (kind == LinkMaterializationKind.SourceBytes)
            throw new ArgumentException("Source-free storage requires a source-free materialization kind.", nameof(kind));

        var definition = new LinkStorageDefinition(
            block,
            byteLength,
            alignment,
            kind,
            sourceTemplate: null);
        var symbol = new LinkStorageSymbol(definition);
        definition.FreezeOperations(operations?.Invoke(symbol) ?? []);
        return symbol;
    }

    public static LinkStorageSymbol CString(ReadOnlySpan<byte> bytes) =>
        SourceBytes(XFileBlockType.LARGE, bytes, alignment: 1);

    public static LinkStorageSymbol CString(string value, string fieldPath)
        => CString(EncodeCString(value, fieldPath));

    internal static byte[] EncodeCString(string value, string fieldPath)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldPath);
        if (value.Contains('\0'))
            throw new InvalidDataException($"{fieldPath} cannot contain NUL.");
        if (value.Any(character => character > byte.MaxValue))
        {
            throw new InvalidDataException(
                $"{fieldPath} must be representable as Latin-1.");
        }

        byte[] bytes = new byte[checked(value.Length + 1)];
        int written = Encoding.Latin1.GetBytes(value, bytes);
        if (written != value.Length)
            throw new InvalidDataException($"{fieldPath} could not be encoded as Latin-1.");
        return bytes;
    }
}

internal sealed class LinkStorageDefinition
{
    private IReadOnlyList<LinkOperation>? _operations;
    private byte[]? _sourceTemplate;
    private bool _sourceTemplateFrozen;

    internal LinkStorageDefinition(
        XFileBlockType block,
        int byteLength,
        int alignment,
        LinkMaterializationKind kind,
        byte[]? sourceTemplate,
        bool pendingSourceTemplate = false)
    {
        if (!Enum.IsDefined(block))
            throw new ArgumentOutOfRangeException(nameof(block));
        if (byteLength < 0)
            throw new ArgumentOutOfRangeException(nameof(byteLength));
        if (alignment <= 0 || (alignment & (alignment - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(alignment));
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (kind == LinkMaterializationKind.SourceBytes)
        {
            if (pendingSourceTemplate)
            {
                if (sourceTemplate is not null)
                {
                    throw new ArgumentException(
                        "Pending source-backed storage cannot begin with a byte template.",
                        nameof(sourceTemplate));
                }
            }
            else if (sourceTemplate is null || sourceTemplate.Length != byteLength)
            {
                throw new ArgumentException(
                    "Source-backed storage requires exactly one immutable byte per destination byte.",
                    nameof(sourceTemplate));
            }
        }
        else if (sourceTemplate is not null || pendingSourceTemplate)
        {
            throw new ArgumentException(
                "Source-free storage cannot retain or await a serialized byte template.",
                nameof(sourceTemplate));
        }
        if (kind == LinkMaterializationKind.RuntimeZeroFill && block != XFileBlockType.RUNTIME)
        {
            throw new ArgumentException(
                "Runtime zero-fill storage must target the RUNTIME block.",
                nameof(block));
        }
        if (kind == LinkMaterializationKind.VirtualReservation && block != XFileBlockType.VIRTUAL)
        {
            throw new ArgumentException(
                "Virtual reservations must target the VIRTUAL block.",
                nameof(block));
        }
        if (kind == LinkMaterializationKind.VertexReservation && block != XFileBlockType.VERTEX)
        {
            throw new ArgumentException(
                "Vertex reservations must target the VERTEX block.",
                nameof(block));
        }

        Block = block;
        ByteLength = byteLength;
        Alignment = alignment;
        Kind = kind;
        _sourceTemplate = sourceTemplate?.ToArray();
        _sourceTemplateFrozen = !pendingSourceTemplate;
    }

    public XFileBlockType Block { get; }
    public int ByteLength { get; }
    public int Alignment { get; }
    public LinkMaterializationKind Kind { get; }
    public ReadOnlyMemory<byte> SourceTemplate =>
        Kind != LinkMaterializationKind.SourceBytes
            ? ReadOnlyMemory<byte>.Empty
            : _sourceTemplateFrozen && _sourceTemplate is not null
                ? _sourceTemplate
                : throw new InvalidOperationException(
                    "Link storage source bytes have not been frozen.");
    public IReadOnlyList<LinkOperation> Operations => _operations ??
        throw new InvalidOperationException("Link storage operations have not been frozen.");

    internal void FreezeOperations(IEnumerable<LinkOperation> operations)
    {
        if (_operations is not null)
            throw new InvalidOperationException("Link storage operations were frozen more than once.");
        ArgumentNullException.ThrowIfNull(operations);
        LinkOperation[] copied = operations
            .Select(operation => operation ?? throw new ArgumentException(
                "Link storage operations cannot contain null.",
                nameof(operations)))
            .ToArray();
        if (Kind != LinkMaterializationKind.SourceBytes && copied.Any(OperationNeedsSourceCell))
        {
            throw new InvalidDataException(
                $"Source-free {Kind} storage cannot contain serialized relocation cells.");
        }
        _operations = Array.AsReadOnly(copied);
    }

    internal void FreezeSourceTemplate(ReadOnlySpan<byte> sourceTemplate)
    {
        if (Kind != LinkMaterializationKind.SourceBytes)
            throw new InvalidOperationException("Only source-backed storage owns a source template.");
        if (_sourceTemplateFrozen)
            throw new InvalidOperationException("Link storage source bytes were frozen more than once.");
        if (sourceTemplate.Length != ByteLength)
        {
            throw new InvalidDataException(
                "Frozen source byte count does not match its physical allocation.");
        }

        _sourceTemplate = sourceTemplate.ToArray();
        _sourceTemplateFrozen = true;
    }

    private static bool OperationNeedsSourceCell(LinkOperation operation) =>
        operation is not MaterializeStorageLinkOperation;
}

internal readonly record struct LinkStorageView
{
    public LinkStorageView(
        LinkStorageSymbol storage,
        int addend,
        int length)
        : this(storage, addend, length, compositeRange: null)
    {
    }

    private LinkStorageView(
        LinkStorageSymbol storage,
        int addend,
        int length,
        LinkStorageRange? compositeRange)
    {
        Storage = storage ?? throw new ArgumentNullException(nameof(storage));
        if (addend < 0 || length < 0 ||
            addend > storage.Definition.ByteLength - length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(addend),
                "Storage view lies outside its physical allocation.");
        }

        Addend = addend;
        Length = length;
        CompositeRange = compositeRange;
    }

    public LinkStorageSymbol Storage { get; }
    public int Addend { get; }
    public int Length { get; }
    internal LinkStorageRange? CompositeRange { get; }

    public static LinkStorageView Whole(LinkStorageSymbol storage) =>
        new(storage, 0, storage.Definition.ByteLength);

    internal static LinkStorageView Composite(
        IEnumerable<LinkStorageView> segments,
        int byteLength)
    {
        ArgumentNullException.ThrowIfNull(segments);
        LinkStorageView[] copied = segments.ToArray();
        var range = new LinkStorageRange(copied, byteLength);
        LinkStorageView first = copied[0];
        return new LinkStorageView(
            first.Storage,
            first.Addend,
            first.Length,
            range);
    }
}

/// <summary>
/// One logical direct range backed by multiple distinct captured allocation
/// symbols. The segment list is an adjacency constraint, not permission to
/// collapse the physical identities into one fabricated allocation.
/// </summary>
internal sealed class LinkStorageRange
{
    private readonly IReadOnlyList<LinkStorageView> _segments;

    public LinkStorageRange(
        IEnumerable<LinkStorageView> segments,
        int byteLength)
    {
        ArgumentNullException.ThrowIfNull(segments);
        LinkStorageView[] copied = segments.ToArray();
        if (copied.Length < 2)
        {
            throw new ArgumentException(
                "A composite storage range requires at least two physical segments.",
                nameof(segments));
        }
        if (copied.Any(segment => segment.CompositeRange is not null))
            throw new ArgumentException("Composite storage ranges cannot be nested.", nameof(segments));
        if (byteLength <= 0 || copied.Sum(segment => segment.Length) != byteLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(byteLength),
                "Composite segment lengths must exactly cover the logical direct range.");
        }

        ByteLength = byteLength;
        _segments = Array.AsReadOnly(copied);
    }

    public int ByteLength { get; }
    public IReadOnlyList<LinkStorageView> Segments => _segments;
}

internal readonly record struct LinkStorageCell
{
    public LinkStorageCell(LinkStorageSymbol owner, int offset)
    {
        Owner = owner ?? throw new ArgumentNullException(nameof(owner));
        if (offset < 0 || offset > owner.Definition.ByteLength)
            throw new ArgumentOutOfRangeException(nameof(offset));
        Offset = offset;
    }

    public LinkStorageSymbol Owner { get; }
    public int Offset { get; }
}

/// <summary>
/// Request-scoped identity for a durable non-XAsset pointer publication cell.
/// It remains distinct from both logical assets and the physical body symbol.
/// </summary>
internal sealed class LinkAliasCellSymbol
{
    public LinkAliasCellSymbol(LinkStorageView target) => Target = target;

    public LinkStorageView Target { get; }
}

/// <summary>Small checked writer for one fixed native structure template.</summary>
internal sealed class LinkTemplateWriter
{
    private readonly byte[] _bytes;

    public LinkTemplateWriter(int byteLength)
    {
        if (byteLength < 0)
            throw new ArgumentOutOfRangeException(nameof(byteLength));
        _bytes = new byte[byteLength];
    }

    public int Position { get; private set; }

    public void WriteByte(byte value)
    {
        EnsureAvailable(sizeof(byte));
        _bytes[Position++] = value;
    }

    public void WriteBytes(ReadOnlySpan<byte> value)
    {
        EnsureAvailable(value.Length);
        value.CopyTo(_bytes.AsSpan(Position, value.Length));
        Position += value.Length;
    }

    public void WriteInt32(int value)
    {
        EnsureAvailable(sizeof(int));
        BinaryPrimitives.WriteInt32BigEndian(
            _bytes.AsSpan(Position, sizeof(int)),
            value);
        Position += sizeof(int);
    }

    public void WriteUInt32(uint value)
    {
        EnsureAvailable(sizeof(uint));
        BinaryPrimitives.WriteUInt32BigEndian(
            _bytes.AsSpan(Position, sizeof(uint)),
            value);
        Position += sizeof(uint);
    }

    public void WriteUInt16(ushort value)
    {
        EnsureAvailable(sizeof(ushort));
        BinaryPrimitives.WriteUInt16BigEndian(
            _bytes.AsSpan(Position, sizeof(ushort)),
            value);
        Position += sizeof(ushort);
    }

    public void Skip(int byteCount)
    {
        EnsureAvailable(byteCount);
        Position += byteCount;
    }

    public byte[] Complete()
    {
        if (Position != _bytes.Length)
        {
            throw new InvalidOperationException(
                $"Storage template wrote 0x{Position:X} of 0x{_bytes.Length:X} bytes.");
        }
        return _bytes.ToArray();
    }

    private void EnsureAvailable(int byteCount)
    {
        if (byteCount < 0 || Position > _bytes.Length - byteCount)
            throw new InvalidOperationException("Storage template exceeds its fixed native size.");
    }
}

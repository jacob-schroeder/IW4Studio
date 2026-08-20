using IW4.Assets.Assets.StructuredData;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;
using XString = IW4.FastFiles.Pointers.XPointer<string>;

namespace IW4.Studio.Documents;

/// <summary>Detached, fixed-shape editing state for one StructuredDataDefSet asset.</summary>
public sealed class StructuredDataDraft
{
    private readonly XString _namePointer;
    private IReadOnlyList<StructuredDataDefinitionDraft> _definitions;

    internal StructuredDataDraft(StructuredDataDefSetAsset source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Name = source.Name;
        _namePointer = source.NamePointer;
        _definitions = CopyDefinitions(source.Defs);
    }

    private StructuredDataDraft(StructuredDataDraft source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Name = source.Name;
        _namePointer = source._namePointer;
        _definitions = CopyDefinitions(source._definitions);
    }

    public string? Name { get; }
    public IReadOnlyList<StructuredDataDefinitionDraft> Definitions => _definitions;

    /// <summary>Creates an independent detached copy of the complete definition graph.</summary>
    public StructuredDataDraft Copy() => new(this);

    /// <summary>Replaces all editable values while retaining this asset's stable identity.</summary>
    public void ReplaceWith(StructuredDataDraft source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!StringEquals(Name, source.Name))
            throw new InvalidOperationException("StructuredDataDefSet identity is read-only.");

        _definitions = CopyDefinitions(source._definitions);
    }

    /// <summary>Compares serialized meaning without pointer, count, or runtime state.</summary>
    public bool SemanticallyEquals(StructuredDataDraft other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return StringEquals(Name, other.Name) &&
            SequenceEquals(_definitions, other._definitions,
                static (left, right) => left.SemanticallyEquals(right));
    }

    internal StructuredDataDraft Clone() => Copy();

    internal StructuredDataDefSetAsset ToAsset() => new()
    {
        NamePointer = _namePointer,
        Name = Name,
        DefCount = _definitions.Count,
        Defs = _definitions.Select(value => value.ToAsset()).ToArray()
    };

    private static IReadOnlyList<StructuredDataDefinitionDraft> CopyDefinitions(
        IEnumerable<StructuredDataDef> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return Array.AsReadOnly(values.Select(value =>
            new StructuredDataDefinitionDraft(value ?? throw new InvalidDataException(
                "StructuredDataDefSet definitions cannot contain null."))).ToArray());
    }

    private static IReadOnlyList<StructuredDataDefinitionDraft> CopyDefinitions(
        IEnumerable<StructuredDataDefinitionDraft> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return Array.AsReadOnly(values.Select(value => value.Copy()).ToArray());
    }

    internal static bool SequenceEquals<T>(
        IReadOnlyList<T> left,
        IReadOnlyList<T> right,
        Func<T, T, bool> equals)
    {
        if (left.Count != right.Count)
            return false;
        for (int index = 0; index < left.Count; index++)
        {
            if (!equals(left[index], right[index]))
                return false;
        }
        return true;
    }

    internal static bool StringEquals(string? left, string? right) =>
        string.Equals(left, right, StringComparison.Ordinal);
}

public sealed class StructuredDataDefinitionDraft
{
    private readonly IReadOnlyList<StructuredDataEnumDraft> _enums;
    private readonly IReadOnlyList<StructuredDataStructDraft> _structs;
    private readonly IReadOnlyList<StructuredDataIndexedArrayDraft> _indexedArrays;
    private readonly IReadOnlyList<StructuredDataEnumedArrayDraft> _enumedArrays;

    internal StructuredDataDefinitionDraft(StructuredDataDef source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Version = source.Version;
        FormatChecksum = source.FormatChecksum;
        _enums = CopyEnums(source.Enums);
        _structs = CopyStructs(source.Structs);
        _indexedArrays = CopyIndexedArrays(source.IndexedArrays);
        _enumedArrays = CopyEnumedArrays(source.EnumedArrays);
        RootType = new StructuredDataTypeDraft(source.RootType);
        Size = source.Size;
    }

    private StructuredDataDefinitionDraft(StructuredDataDefinitionDraft source)
    {
        Version = source.Version;
        FormatChecksum = source.FormatChecksum;
        _enums = CopyEnums(source._enums);
        _structs = CopyStructs(source._structs);
        _indexedArrays = CopyIndexedArrays(source._indexedArrays);
        _enumedArrays = CopyEnumedArrays(source._enumedArrays);
        RootType = source.RootType.Copy();
        Size = source.Size;
    }

    public int Version { get; private set; }
    public uint FormatChecksum { get; private set; }
    public IReadOnlyList<StructuredDataEnumDraft> Enums => _enums;
    public IReadOnlyList<StructuredDataStructDraft> Structs => _structs;
    public IReadOnlyList<StructuredDataIndexedArrayDraft> IndexedArrays => _indexedArrays;
    public IReadOnlyList<StructuredDataEnumedArrayDraft> EnumedArrays => _enumedArrays;
    public StructuredDataTypeDraft RootType { get; }
    public uint Size { get; private set; }

    public void SetVersion(int value) => Version = value;
    public void SetFormatChecksum(uint value) => FormatChecksum = value;
    public void SetSize(uint value) => Size = value;

    internal StructuredDataDefinitionDraft Copy() => new(this);

    internal StructuredDataDef ToAsset() => new()
    {
        Version = Version,
        FormatChecksum = FormatChecksum,
        EnumCount = _enums.Count,
        Enums = _enums.Select(value => value.ToAsset()).ToArray(),
        StructCount = _structs.Count,
        Structs = _structs.Select(value => value.ToAsset()).ToArray(),
        IndexedArrayCount = _indexedArrays.Count,
        IndexedArrays = _indexedArrays.Select(value => value.ToAsset()).ToArray(),
        EnumedArrayCount = _enumedArrays.Count,
        EnumedArrays = _enumedArrays.Select(value => value.ToAsset()).ToArray(),
        RootType = RootType.ToAsset(),
        Size = Size
    };

    internal bool SemanticallyEquals(StructuredDataDefinitionDraft other) =>
        Version == other.Version &&
        FormatChecksum == other.FormatChecksum &&
        StructuredDataDraft.SequenceEquals(_enums, other._enums,
            static (left, right) => left.SemanticallyEquals(right)) &&
        StructuredDataDraft.SequenceEquals(_structs, other._structs,
            static (left, right) => left.SemanticallyEquals(right)) &&
        StructuredDataDraft.SequenceEquals(_indexedArrays, other._indexedArrays,
            static (left, right) => left.SemanticallyEquals(right)) &&
        StructuredDataDraft.SequenceEquals(_enumedArrays, other._enumedArrays,
            static (left, right) => left.SemanticallyEquals(right)) &&
        RootType.SemanticallyEquals(other.RootType) &&
        Size == other.Size;

    private static IReadOnlyList<StructuredDataEnumDraft> CopyEnums(
        IEnumerable<StructuredDataEnum> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return Array.AsReadOnly(values.Select(value =>
            new StructuredDataEnumDraft(value ?? throw new InvalidDataException(
                "StructuredDataDef enums cannot contain null."))).ToArray());
    }

    private static IReadOnlyList<StructuredDataEnumDraft> CopyEnums(
        IEnumerable<StructuredDataEnumDraft> values) =>
        Array.AsReadOnly(values.Select(value => value.Copy()).ToArray());

    private static IReadOnlyList<StructuredDataStructDraft> CopyStructs(
        IEnumerable<StructuredDataStruct> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return Array.AsReadOnly(values.Select(value =>
            new StructuredDataStructDraft(value ?? throw new InvalidDataException(
                "StructuredDataDef structs cannot contain null."))).ToArray());
    }

    private static IReadOnlyList<StructuredDataStructDraft> CopyStructs(
        IEnumerable<StructuredDataStructDraft> values) =>
        Array.AsReadOnly(values.Select(value => value.Copy()).ToArray());

    private static IReadOnlyList<StructuredDataIndexedArrayDraft> CopyIndexedArrays(
        IEnumerable<StructuredDataIndexedArray> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return Array.AsReadOnly(values.Select(value =>
            new StructuredDataIndexedArrayDraft(value ?? throw new InvalidDataException(
                "StructuredDataDef indexed arrays cannot contain null."))).ToArray());
    }

    private static IReadOnlyList<StructuredDataIndexedArrayDraft> CopyIndexedArrays(
        IEnumerable<StructuredDataIndexedArrayDraft> values) =>
        Array.AsReadOnly(values.Select(value => value.Copy()).ToArray());

    private static IReadOnlyList<StructuredDataEnumedArrayDraft> CopyEnumedArrays(
        IEnumerable<StructuredDataEnumedArray> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return Array.AsReadOnly(values.Select(value =>
            new StructuredDataEnumedArrayDraft(value ?? throw new InvalidDataException(
                "StructuredDataDef enumed arrays cannot contain null."))).ToArray());
    }

    private static IReadOnlyList<StructuredDataEnumedArrayDraft> CopyEnumedArrays(
        IEnumerable<StructuredDataEnumedArrayDraft> values) =>
        Array.AsReadOnly(values.Select(value => value.Copy()).ToArray());
}

public sealed class StructuredDataEnumDraft
{
    private readonly IReadOnlyList<StructuredDataEnumEntryDraft> _entries;

    internal StructuredDataEnumDraft(StructuredDataEnum source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ReservedEntryCount = source.ReservedEntryCount;
        ArgumentNullException.ThrowIfNull(source.Entries);
        _entries = Array.AsReadOnly(source.Entries.Select(value =>
            new StructuredDataEnumEntryDraft(value ?? throw new InvalidDataException(
                "StructuredDataEnum entries cannot contain null."))).ToArray());
    }

    private StructuredDataEnumDraft(StructuredDataEnumDraft source)
    {
        ReservedEntryCount = source.ReservedEntryCount;
        _entries = Array.AsReadOnly(source._entries.Select(value => value.Copy()).ToArray());
    }

    public int ReservedEntryCount { get; private set; }
    public IReadOnlyList<StructuredDataEnumEntryDraft> Entries => _entries;

    public void SetReservedEntryCount(int value) => ReservedEntryCount = value;

    internal StructuredDataEnumDraft Copy() => new(this);

    internal StructuredDataEnum ToAsset() => new()
    {
        EntryCount = _entries.Count,
        ReservedEntryCount = ReservedEntryCount,
        Entries = _entries.Select(value => value.ToAsset()).ToArray()
    };

    internal bool SemanticallyEquals(StructuredDataEnumDraft other) =>
        ReservedEntryCount == other.ReservedEntryCount &&
        StructuredDataDraft.SequenceEquals(_entries, other._entries,
            static (left, right) => left.SemanticallyEquals(right));
}

public sealed class StructuredDataEnumEntryDraft
{
    private readonly string? _originalString;
    private readonly XString _originalStringPointer;

    internal StructuredDataEnumEntryDraft(StructuredDataEnumEntry source)
    {
        ArgumentNullException.ThrowIfNull(source);
        String = source.String;
        _originalString = source.String;
        _originalStringPointer = source.StringPointer;
        Index = source.Index;
        Padding = source.Padding;
    }

    private StructuredDataEnumEntryDraft(StructuredDataEnumEntryDraft source)
    {
        String = source.String;
        _originalString = source._originalString;
        _originalStringPointer = source._originalStringPointer;
        Index = source.Index;
        Padding = source.Padding;
    }

    public string? String { get; private set; }
    public ushort Index { get; private set; }
    public ushort Padding { get; }

    public void SetString(string? value) => String = value;
    public void SetIndex(ushort value) => Index = value;

    internal StructuredDataEnumEntryDraft Copy() => new(this);

    internal StructuredDataEnumEntry ToAsset() => new()
    {
        StringPointer = StructuredDataDraft.StringEquals(String, _originalString)
            ? _originalStringPointer
            : default,
        String = String,
        Index = Index,
        Padding = Padding
    };

    internal bool SemanticallyEquals(StructuredDataEnumEntryDraft other) =>
        StructuredDataDraft.StringEquals(String, other.String) &&
        Index == other.Index &&
        Padding == other.Padding;
}

public sealed class StructuredDataStructDraft
{
    private readonly IReadOnlyList<StructuredDataStructPropertyDraft> _properties;

    internal StructuredDataStructDraft(StructuredDataStruct source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Size = source.Size;
        BitOffset = source.BitOffset;
        ArgumentNullException.ThrowIfNull(source.Properties);
        _properties = Array.AsReadOnly(source.Properties.Select(value =>
            new StructuredDataStructPropertyDraft(value ?? throw new InvalidDataException(
                "StructuredDataStruct properties cannot contain null."))).ToArray());
    }

    private StructuredDataStructDraft(StructuredDataStructDraft source)
    {
        Size = source.Size;
        BitOffset = source.BitOffset;
        _properties = Array.AsReadOnly(source._properties.Select(value => value.Copy()).ToArray());
    }

    public int Size { get; private set; }
    public uint BitOffset { get; private set; }
    public IReadOnlyList<StructuredDataStructPropertyDraft> Properties => _properties;

    public void SetSize(int value) => Size = value;
    public void SetBitOffset(uint value) => BitOffset = value;

    internal StructuredDataStructDraft Copy() => new(this);

    internal StructuredDataStruct ToAsset() => new()
    {
        PropertyCount = _properties.Count,
        Size = Size,
        BitOffset = BitOffset,
        Properties = _properties.Select(value => value.ToAsset()).ToArray()
    };

    internal bool SemanticallyEquals(StructuredDataStructDraft other) =>
        Size == other.Size &&
        BitOffset == other.BitOffset &&
        StructuredDataDraft.SequenceEquals(_properties, other._properties,
            static (left, right) => left.SemanticallyEquals(right));
}

public sealed class StructuredDataStructPropertyDraft
{
    private readonly string? _originalName;
    private readonly XString _originalNamePointer;

    internal StructuredDataStructPropertyDraft(StructuredDataStructProperty source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Name = source.Name;
        _originalName = source.Name;
        _originalNamePointer = source.NamePointer;
        Type = new StructuredDataTypeDraft(source.Type);
        Offset = source.Offset;
    }

    private StructuredDataStructPropertyDraft(StructuredDataStructPropertyDraft source)
    {
        Name = source.Name;
        _originalName = source._originalName;
        _originalNamePointer = source._originalNamePointer;
        Type = source.Type.Copy();
        Offset = source.Offset;
    }

    public string? Name { get; private set; }
    public StructuredDataTypeDraft Type { get; }
    public uint Offset { get; private set; }

    public void SetName(string? value) => Name = value;
    public void SetOffset(uint value) => Offset = value;

    internal StructuredDataStructPropertyDraft Copy() => new(this);

    internal StructuredDataStructProperty ToAsset() => new()
    {
        NamePointer = StructuredDataDraft.StringEquals(Name, _originalName)
            ? _originalNamePointer
            : default,
        Name = Name,
        Type = Type.ToAsset(),
        Offset = Offset
    };

    internal bool SemanticallyEquals(StructuredDataStructPropertyDraft other) =>
        StructuredDataDraft.StringEquals(Name, other.Name) &&
        Type.SemanticallyEquals(other.Type) &&
        Offset == other.Offset;
}

public sealed class StructuredDataIndexedArrayDraft
{
    internal StructuredDataIndexedArrayDraft(StructuredDataIndexedArray source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArraySize = source.ArraySize;
        ElementType = new StructuredDataTypeDraft(source.ElementType);
        ElementSize = source.ElementSize;
    }

    private StructuredDataIndexedArrayDraft(StructuredDataIndexedArrayDraft source)
    {
        ArraySize = source.ArraySize;
        ElementType = source.ElementType.Copy();
        ElementSize = source.ElementSize;
    }

    public int ArraySize { get; private set; }
    public StructuredDataTypeDraft ElementType { get; }
    public uint ElementSize { get; private set; }

    public void SetArraySize(int value) => ArraySize = value;
    public void SetElementSize(uint value) => ElementSize = value;

    internal StructuredDataIndexedArrayDraft Copy() => new(this);

    internal StructuredDataIndexedArray ToAsset() => new()
    {
        ArraySize = ArraySize,
        ElementType = ElementType.ToAsset(),
        ElementSize = ElementSize
    };

    internal bool SemanticallyEquals(StructuredDataIndexedArrayDraft other) =>
        ArraySize == other.ArraySize &&
        ElementType.SemanticallyEquals(other.ElementType) &&
        ElementSize == other.ElementSize;
}

public sealed class StructuredDataEnumedArrayDraft
{
    internal StructuredDataEnumedArrayDraft(StructuredDataEnumedArray source)
    {
        ArgumentNullException.ThrowIfNull(source);
        EnumIndex = source.EnumIndex;
        ElementType = new StructuredDataTypeDraft(source.ElementType);
        ElementSize = source.ElementSize;
    }

    private StructuredDataEnumedArrayDraft(StructuredDataEnumedArrayDraft source)
    {
        EnumIndex = source.EnumIndex;
        ElementType = source.ElementType.Copy();
        ElementSize = source.ElementSize;
    }

    public int EnumIndex { get; private set; }
    public StructuredDataTypeDraft ElementType { get; }
    public uint ElementSize { get; private set; }

    public void SetEnumIndex(int value) => EnumIndex = value;
    public void SetElementSize(uint value) => ElementSize = value;

    internal StructuredDataEnumedArrayDraft Copy() => new(this);

    internal StructuredDataEnumedArray ToAsset() => new()
    {
        EnumIndex = EnumIndex,
        ElementType = ElementType.ToAsset(),
        ElementSize = ElementSize
    };

    internal bool SemanticallyEquals(StructuredDataEnumedArrayDraft other) =>
        EnumIndex == other.EnumIndex &&
        ElementType.SemanticallyEquals(other.ElementType) &&
        ElementSize == other.ElementSize;
}

public sealed class StructuredDataTypeDraft
{
    internal StructuredDataTypeDraft(StructuredDataType source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Type = source.Type;
        UnionValue = source.UnionValue;
    }

    private StructuredDataTypeDraft(StructuredDataTypeDraft source)
    {
        Type = source.Type;
        UnionValue = source.UnionValue;
    }

    public StructuredDataTypeCategory Type { get; private set; }
    public int UnionValue { get; private set; }

    public void SetType(StructuredDataTypeCategory value) => Type = value;
    public void SetUnionValue(int value) => UnionValue = value;

    internal StructuredDataTypeDraft Copy() => new(this);

    internal StructuredDataType ToAsset() => new()
    {
        Type = Type,
        UnionValue = UnionValue
    };

    internal bool SemanticallyEquals(StructuredDataTypeDraft other) =>
        Type == other.Type && UnionValue == other.UnionValue;
}

internal sealed class StructuredDataAdapter
    : AssetAuthoringAdapter<StructuredDataDefSetAsset, StructuredDataDraft>
{
    public override XAssetType AssetType => XAssetType.StructuredDataDef;
    public override StructuredDataDraft CreateDraft(StructuredDataDefSetAsset value) =>
        new(value);
    public override StructuredDataDraft CloneDraft(StructuredDataDraft value) =>
        value.Clone();
    public override StructuredDataDefSetAsset CreateDefinition(StructuredDataDraft value) =>
        value.ToAsset();
    public override bool SemanticallyEquals(
        StructuredDataDraft left,
        StructuredDataDraft right) => left.SemanticallyEquals(right);

    public override IReadOnlyList<AssetValidationIssue> Validate(StructuredDataDraft value)
    {
        try
        {
            _ = new LinkAssetPool(
                [new LinkAssetProviderSource(value.ToAsset()).AsAuthoredDetached()]);
            return [];
        }
        catch (ArgumentException exception)
        {
            return ValidationFailure(exception);
        }
        catch (InvalidDataException exception)
        {
            return ValidationFailure(exception);
        }
        catch (OverflowException exception)
        {
            return ValidationFailure(exception);
        }
    }

    private static IReadOnlyList<AssetValidationIssue> ValidationFailure(Exception exception) =>
        [new AssetValidationIssue(
            ExtractFieldPath(exception),
            exception.Message,
            AssetValidationSeverity.Error)];

    private static string ExtractFieldPath(Exception exception)
    {
        const string graphRoot = "StructuredDataDefSet";
        string message = exception.Message;
        int start = message.IndexOf(graphRoot, StringComparison.Ordinal);
        if (start >= 0)
        {
            int end = start + graphRoot.Length;
            while (end < message.Length && IsFieldPathCharacter(message[end]))
                end++;
            return message[start..end];
        }

        int assetName = message.IndexOf("Asset.Name", StringComparison.Ordinal);
        if (assetName >= 0 ||
            exception is ArgumentException argument &&
            argument.ParamName is "definition" or "normalizedName")
        {
            return "StructuredDataDefSet.Name";
        }

        return graphRoot;
    }

    private static bool IsFieldPathCharacter(char value) =>
        char.IsAsciiLetterOrDigit(value) || value is '_' or '.' or '[' or ']';
}

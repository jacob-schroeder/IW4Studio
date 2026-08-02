using System.Collections.ObjectModel;
using System.Security.Cryptography;

namespace IW4.Studio.MapEditor.Editing.MapEntsSyntax;

public readonly record struct MapEntEntityOrdinal
{
    public MapEntEntityOrdinal(int value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value));

        Value = value;
    }

    public int Value { get; }
    public override string ToString() => Value.ToString(
        System.Globalization.CultureInfo.InvariantCulture);
}

public readonly record struct MapEntPropertyOrdinal
{
    public MapEntPropertyOrdinal(int value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value));

        Value = value;
    }

    public int Value { get; }
    public override string ToString() => Value.ToString(
        System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// Exact zero-based byte span in one immutable MapEnts syntax snapshot.
/// </summary>
public readonly record struct MapEntSourceSpan
{
    public MapEntSourceSpan(int offset, int length)
    {
        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset));
        if (length < 0 || length > int.MaxValue - offset)
            throw new ArgumentOutOfRangeException(nameof(length));

        Offset = offset;
        Length = length;
    }

    public int Offset { get; }
    public int Length { get; }
    public int End => Offset + Length;
}

public enum MapEntsSyntaxDiagnosticCode
{
    EmbeddedNul,
    UnexpectedToken,
    UnterminatedEntity,
    UnterminatedQuotedToken,
    MissingPropertySeparator,
    ExpectedQuotedValue,
    RuntimeDecodedTokenTooLong,
    RuntimePropertyCountExceeded,
    RuntimeSpawnVarPoolExceeded
}

public sealed record MapEntsSyntaxDiagnostic(
    MapEntsSyntaxDiagnosticCode Code,
    MapEntSourceSpan Span,
    string Message);

public sealed class MapEntsSyntaxProperty
{
    internal MapEntsSyntaxProperty(
        MapEntEntityOrdinal entityOrdinal,
        MapEntPropertyOrdinal ordinal,
        string key,
        string value,
        int runtimeDecodedKeyByteLength,
        int runtimeDecodedValueByteLength,
        MapEntSourceSpan span,
        MapEntSourceSpan keyTokenSpan,
        MapEntSourceSpan keyContentSpan,
        MapEntSourceSpan valueTokenSpan,
        MapEntSourceSpan valueContentSpan)
    {
        EntityOrdinal = entityOrdinal;
        Ordinal = ordinal;
        Key = key;
        Value = value;
        RuntimeDecodedKeyByteLength = runtimeDecodedKeyByteLength;
        RuntimeDecodedValueByteLength = runtimeDecodedValueByteLength;
        Span = span;
        KeyTokenSpan = keyTokenSpan;
        KeyContentSpan = keyContentSpan;
        ValueTokenSpan = valueTokenSpan;
        ValueContentSpan = valueContentSpan;
    }

    public MapEntEntityOrdinal EntityOrdinal { get; }
    public MapEntPropertyOrdinal Ordinal { get; }

    /// <summary>
    /// Exact Latin-1 source content between the quotes. Runtime escapes remain
    /// encoded so the source span and text stay byte-authoritative.
    /// </summary>
    public string Key { get; }

    /// <inheritdoc cref="Key"/>
    public string Value { get; }

    public int RuntimeDecodedKeyByteLength { get; }
    public int RuntimeDecodedValueByteLength { get; }
    public MapEntSourceSpan Span { get; }
    public MapEntSourceSpan KeyTokenSpan { get; }
    public MapEntSourceSpan KeyContentSpan { get; }
    public MapEntSourceSpan ValueTokenSpan { get; }
    public MapEntSourceSpan ValueContentSpan { get; }
}

public sealed class MapEntsSyntaxEntity
{
    private readonly IReadOnlyList<MapEntsSyntaxProperty> _properties;

    internal MapEntsSyntaxEntity(
        MapEntEntityOrdinal ordinal,
        MapEntSourceSpan span,
        MapEntSourceSpan openBraceSpan,
        MapEntSourceSpan closeBraceSpan,
        IEnumerable<MapEntsSyntaxProperty> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        Ordinal = ordinal;
        Span = span;
        OpenBraceSpan = openBraceSpan;
        CloseBraceSpan = closeBraceSpan;
        MapEntsSyntaxProperty[] propertyArray = properties.ToArray();
        _properties = new ReadOnlyCollection<MapEntsSyntaxProperty>(
            propertyArray);
        RuntimeDecodedPropertyPoolByteLength = propertyArray.Aggregate(
            0L,
            static (length, property) =>
                length +
                property.RuntimeDecodedKeyByteLength + 1L +
                property.RuntimeDecodedValueByteLength + 1L);
    }

    public MapEntEntityOrdinal Ordinal { get; }
    public MapEntSourceSpan Span { get; }
    public MapEntSourceSpan OpenBraceSpan { get; }
    public MapEntSourceSpan CloseBraceSpan { get; }
    public IReadOnlyList<MapEntsSyntaxProperty> Properties => _properties;
    public long RuntimeDecodedPropertyPoolByteLength { get; }
}

/// <summary>
/// Immutable, byte-authoritative MapEnts entity-string snapshot. Parsed nodes
/// are views over exact source spans; serialization always starts from the
/// preserved source bytes rather than reconstructing formatting.
/// </summary>
public sealed class MapEntsSyntaxDocument
{
    private readonly byte[] _sourceBytes;
    private readonly IReadOnlyList<MapEntsSyntaxEntity> _entities;
    private readonly IReadOnlyList<MapEntsSyntaxDiagnostic> _diagnostics;

    internal MapEntsSyntaxDocument(
        ReadOnlySpan<byte> sourceBytes,
        IEnumerable<MapEntsSyntaxEntity> entities,
        IEnumerable<MapEntsSyntaxDiagnostic> diagnostics,
        bool hasTrailingNul)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(diagnostics);
        _sourceBytes = sourceBytes.ToArray();
        _entities = new ReadOnlyCollection<MapEntsSyntaxEntity>(
            entities.ToArray());
        _diagnostics = new ReadOnlyCollection<MapEntsSyntaxDiagnostic>(
            diagnostics.ToArray());
        HasTrailingNul = hasTrailingNul;
        ContentDigest = Convert.ToHexString(
            SHA256.HashData(_sourceBytes));
    }

    public int ByteLength => _sourceBytes.Length;
    public bool HasTrailingNul { get; }
    public bool CanEdit => _diagnostics.Count == 0;
    public string ContentDigest { get; }
    public IReadOnlyList<MapEntsSyntaxEntity> Entities => _entities;
    public IReadOnlyList<MapEntsSyntaxDiagnostic> Diagnostics => _diagnostics;

    public byte[] Serialize() => _sourceBytes.ToArray();

    public byte[] GetBytesCopy(MapEntSourceSpan span)
    {
        RequireOwnedSpan(span);
        return _sourceBytes.AsSpan(span.Offset, span.Length).ToArray();
    }

    public MapEntsSyntaxEntity GetEntity(MapEntEntityOrdinal ordinal)
    {
        if ((uint)ordinal.Value >= (uint)_entities.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ordinal),
                ordinal.Value,
                "Entity ordinal is outside this syntax snapshot.");
        }

        return _entities[ordinal.Value];
    }

    public MapEntsSyntaxProperty GetProperty(
        MapEntEntityOrdinal entityOrdinal,
        MapEntPropertyOrdinal propertyOrdinal)
    {
        MapEntsSyntaxEntity entity = GetEntity(entityOrdinal);
        if ((uint)propertyOrdinal.Value >=
            (uint)entity.Properties.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(propertyOrdinal),
                propertyOrdinal.Value,
                "Property ordinal is outside the selected entity.");
        }

        return entity.Properties[propertyOrdinal.Value];
    }

    internal bool HasSameBytes(MapEntsSyntaxDocument other) =>
        string.Equals(
            ContentDigest,
            other.ContentDigest,
            StringComparison.Ordinal) &&
        _sourceBytes.AsSpan().SequenceEqual(other._sourceBytes);

    private void RequireOwnedSpan(MapEntSourceSpan span)
    {
        if (span.End > _sourceBytes.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(span),
                "Source span is outside this syntax snapshot.");
        }
    }
}

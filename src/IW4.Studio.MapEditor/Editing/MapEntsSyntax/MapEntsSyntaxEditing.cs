using System.Globalization;
using System.Text;
using IW4.Studio.MapEditor.Editing.Entities;

namespace IW4.Studio.MapEditor.Editing.MapEntsSyntax;

public enum MapEntPropertyField
{
    Key,
    Value
}

public enum MapEntsEditRejectionReason
{
    DocumentIsNotEditable,
    ReplacementIsNotLatin1,
    ReplacementContainsNul,
    ReplacementContainsQuote,
    ReplacementEscapesClosingQuote,
    ReplacementDecodedTokenTooLong,
    ReplacementExceedsRuntimeSpawnVarPool,
    CardinalityRequiresTrailingNul,
    CardinalityEntityLimitExceeded,
    CardinalitySchemaIsNotAuthorized,
    CardinalityTargetIsNotFinalEntity,
    SnapshotMismatch
}

public sealed class MapEntsEditRejectedException : InvalidOperationException
{
    internal MapEntsEditRejectedException(
        MapEntsEditRejectionReason reason,
        string message)
        : base(message)
    {
        Reason = reason;
    }

    public MapEntsEditRejectionReason Reason { get; }
}

/// <summary>
/// Detached, reversible replacement between two immutable syntax snapshots.
/// Apply and revert verify the expected byte identity before changing state.
/// </summary>
public sealed class MapEntsPropertyEdit
{
    internal MapEntsPropertyEdit(
        MapEntsSyntaxDocument before,
        MapEntsSyntaxDocument after,
        MapEntEntityOrdinal entityOrdinal,
        MapEntPropertyOrdinal propertyOrdinal,
        MapEntPropertyField field,
        string originalText,
        string replacementText)
    {
        Before = before;
        After = after;
        EntityOrdinal = entityOrdinal;
        PropertyOrdinal = propertyOrdinal;
        Field = field;
        OriginalText = originalText;
        ReplacementText = replacementText;
    }

    public MapEntsSyntaxDocument Before { get; }
    public MapEntsSyntaxDocument After { get; }
    public MapEntEntityOrdinal EntityOrdinal { get; }
    public MapEntPropertyOrdinal PropertyOrdinal { get; }
    public MapEntPropertyField Field { get; }
    public string OriginalText { get; }
    public string ReplacementText { get; }
    public bool IsNoChange => Before.HasSameBytes(After);

    public MapEntsSyntaxDocument Apply(MapEntsSyntaxDocument current)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (!current.HasSameBytes(Before))
        {
            throw new MapEntsEditRejectedException(
                MapEntsEditRejectionReason.SnapshotMismatch,
                "Cannot apply the MapEnt property edit because the current byte snapshot does not match its prepared source.");
        }

        return After;
    }

    public MapEntsSyntaxDocument Revert(MapEntsSyntaxDocument current)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (!current.HasSameBytes(After))
        {
            throw new MapEntsEditRejectedException(
                MapEntsEditRejectionReason.SnapshotMismatch,
                "Cannot revert the MapEnt property edit because the current byte snapshot does not match its prepared result.");
        }

        return Before;
    }
}

/// <summary>
/// One reversible, byte-exact tail-cardinality transition. The syntax layer
/// deliberately supports only the executable-proven script_origin schema.
/// </summary>
public sealed class MapEntsCardinalityEdit
{
    private readonly byte[] _entityBytes;

    internal MapEntsCardinalityEdit(
        MapEntsSyntaxDocument before,
        MapEntsSyntaxDocument after,
        MapEntityCardinalityOperation operation,
        MapEntEntityOrdinal entityOrdinal,
        ReadOnlySpan<byte> entityBytes)
    {
        Before = before;
        After = after;
        Operation = operation;
        EntityOrdinal = entityOrdinal;
        _entityBytes = entityBytes.ToArray();
    }

    public MapEntsSyntaxDocument Before { get; }
    public MapEntsSyntaxDocument After { get; }
    public MapEntityCardinalityOperation Operation { get; }
    public MapEntEntityOrdinal EntityOrdinal { get; }
    public byte[] GetEntityBytesCopy() => _entityBytes.ToArray();

    public MapEntsSyntaxDocument Apply(MapEntsSyntaxDocument current)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (!current.HasSameBytes(Before))
        {
            throw new MapEntsEditRejectedException(
                MapEntsEditRejectionReason.SnapshotMismatch,
                "Cannot apply the MapEnt cardinality edit because the current byte snapshot does not match its prepared source.");
        }

        return After;
    }

    public MapEntsSyntaxDocument Revert(MapEntsSyntaxDocument current)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (!current.HasSameBytes(After))
        {
            throw new MapEntsEditRejectedException(
                MapEntsEditRejectionReason.SnapshotMismatch,
                "Cannot revert the MapEnt cardinality edit because the current byte snapshot does not match its prepared result.");
        }

        return Before;
    }
}

public static class MapEntsSyntaxEditing
{
    /// <summary>
    /// Conservative executable limit for non-world MapEnt rows. The worldspawn
    /// row is counted separately.
    /// </summary>
    public const int MaximumNonWorldEntityCount = 2020;

    /// <summary>
    /// Reports whether this exact byte snapshot can accept the reviewed
    /// script_origin tail append before any entity definition is considered.
    /// The command remains responsible for validating its canonical fields.
    /// </summary>
    public static bool CanAppendScriptOrigin(
        this MapEntsSyntaxDocument document,
        out string blocker)
    {
        ArgumentNullException.ThrowIfNull(document);
        try
        {
            RequireCardinalityDocument(document);
            RequireScriptOriginAppendCapacity(document);
            blocker = string.Empty;
            return true;
        }
        catch (MapEntsEditRejectedException exception)
        {
            blocker = exception.Message;
            return false;
        }
    }

    /// <summary>
    /// Reports whether the physical final row has the exact reviewed
    /// script_origin schema and can be removed from this byte snapshot.
    /// </summary>
    public static bool CanRemoveFinalScriptOrigin(
        this MapEntsSyntaxDocument document,
        out string blocker)
    {
        ArgumentNullException.ThrowIfNull(document);
        try
        {
            RequireCardinalityDocument(document);
            _ = RequireFinalScriptOrigin(document);
            blocker = string.Empty;
            return true;
        }
        catch (MapEntsEditRejectedException exception)
        {
            blocker = exception.Message;
            return false;
        }
    }

    public static MapEntsPropertyEdit PreparePropertyReplacement(
        this MapEntsSyntaxDocument document,
        MapEntEntityOrdinal entityOrdinal,
        MapEntPropertyOrdinal propertyOrdinal,
        MapEntPropertyField field,
        string replacement,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(replacement);
        if (!Enum.IsDefined(field))
            throw new ArgumentOutOfRangeException(nameof(field));
        cancellationToken.ThrowIfCancellationRequested();

        if (!document.CanEdit)
        {
            string diagnostic = document.Diagnostics.Count == 0
                ? "unknown validation failure"
                : string.Join(
                    "; ",
                    document.Diagnostics.Select(value =>
                        $"{value.Code} at byte {value.Span.Offset}"));
            throw new MapEntsEditRejectedException(
                MapEntsEditRejectionReason.DocumentIsNotEditable,
                $"MapEnts syntax is not safely editable: {diagnostic}.");
        }

        MapEntsSyntaxProperty property = document.GetProperty(
            entityOrdinal,
            propertyOrdinal);
        MapEntSourceSpan replacementSpan;
        string originalText;
        switch (field)
        {
            case MapEntPropertyField.Key:
                replacementSpan = property.KeyContentSpan;
                originalText = property.Key;
                break;
            case MapEntPropertyField.Value:
                replacementSpan = property.ValueContentSpan;
                originalText = property.Value;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(field));
        }

        EncodedReplacement encodedReplacement =
            EncodeReplacement(replacement);
        byte[] replacementBytes = encodedReplacement.Bytes;
        MapEntsSyntaxEntity entity = document.GetEntity(entityOrdinal);
        long originalDecodedByteLength =
            field == MapEntPropertyField.Key
                ? property.RuntimeDecodedKeyByteLength
                : property.RuntimeDecodedValueByteLength;
        long proposedPoolByteLength =
            entity.RuntimeDecodedPropertyPoolByteLength -
            originalDecodedByteLength +
            encodedReplacement.RuntimeDecodedByteLength;
        if (proposedPoolByteLength >
            MapEntsRuntimeSyntaxLimits
                .MaximumDecodedPropertyPoolByteLengthPerEntity)
        {
            throw new MapEntsEditRejectedException(
                MapEntsEditRejectionReason
                    .ReplacementExceedsRuntimeSpawnVarPool,
                $"Replacement would require {proposedPoolByteLength} " +
                "decoded key/value bytes including NUL terminators; the " +
                "runtime SpawnVar pool holds at most " +
                $"{MapEntsRuntimeSyntaxLimits.MaximumDecodedPropertyPoolByteLengthPerEntity} " +
                "bytes per entity.");
        }

        byte[] beforeBytes = document.Serialize();
        if (beforeBytes
            .AsSpan(replacementSpan.Offset, replacementSpan.Length)
            .SequenceEqual(replacementBytes))
        {
            return new MapEntsPropertyEdit(
                document,
                document,
                entityOrdinal,
                propertyOrdinal,
                field,
                originalText,
                replacement);
        }

        byte[] afterBytes = new byte[
            checked(
                beforeBytes.Length -
                replacementSpan.Length +
                replacementBytes.Length)];
        beforeBytes.AsSpan(0, replacementSpan.Offset)
            .CopyTo(afterBytes);
        replacementBytes.CopyTo(
            afterBytes.AsSpan(replacementSpan.Offset));
        beforeBytes.AsSpan(replacementSpan.End)
            .CopyTo(afterBytes.AsSpan(
                replacementSpan.Offset + replacementBytes.Length));

        MapEntsSyntaxDocument after = MapEntsSyntaxParser.Parse(
            afterBytes,
            cancellationToken);
        RequireEquivalentStructure(document, after);
        return new MapEntsPropertyEdit(
            document,
            after,
            entityOrdinal,
            propertyOrdinal,
            field,
            originalText,
            replacement);
    }

    public static MapEntsPropertyEdit PreparePropertyKeyReplacement(
        this MapEntsSyntaxDocument document,
        MapEntEntityOrdinal entityOrdinal,
        MapEntPropertyOrdinal propertyOrdinal,
        string replacement,
        CancellationToken cancellationToken = default) =>
        PreparePropertyReplacement(
            document,
            entityOrdinal,
            propertyOrdinal,
            MapEntPropertyField.Key,
            replacement,
            cancellationToken);

    public static MapEntsPropertyEdit PreparePropertyValueReplacement(
        this MapEntsSyntaxDocument document,
        MapEntEntityOrdinal entityOrdinal,
        MapEntPropertyOrdinal propertyOrdinal,
        string replacement,
        CancellationToken cancellationToken = default) =>
        PreparePropertyReplacement(
            document,
            entityOrdinal,
            propertyOrdinal,
            MapEntPropertyField.Value,
            replacement,
            cancellationToken);

    public static MapEntsCardinalityEdit PrepareScriptOriginAppend(
        this MapEntsSyntaxDocument document,
        IEnumerable<KeyValuePair<string, string>> properties,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(properties);
        cancellationToken.ThrowIfCancellationRequested();
        RequireCardinalityDocument(document);

        KeyValuePair<string, string>[] canonicalProperties =
            ValidateAndCanonicalizeScriptOrigin(properties);
        RequireScriptOriginAppendCapacity(document);

        byte[] entityBytes = EncodeCanonicalEntity(canonicalProperties);
        byte[] beforeBytes = document.Serialize();
        int insertionOffset = beforeBytes.Length - 1;
        var afterBytes = new byte[
            checked(beforeBytes.Length + entityBytes.Length)];
        beforeBytes.AsSpan(0, insertionOffset).CopyTo(afterBytes);
        entityBytes.CopyTo(afterBytes.AsSpan(insertionOffset));
        beforeBytes.AsSpan(insertionOffset).CopyTo(
            afterBytes.AsSpan(insertionOffset + entityBytes.Length));

        MapEntsSyntaxDocument after = MapEntsSyntaxParser.Parse(
            afterBytes,
            cancellationToken);
        var ordinal = new MapEntEntityOrdinal(document.Entities.Count);
        RequireCardinalityResult(
            document,
            after,
            ordinal,
            expectedDelta: 1,
            entityBytes);
        return new MapEntsCardinalityEdit(
            document,
            after,
            MapEntityCardinalityOperation.Append,
            ordinal,
            entityBytes);
    }

    public static MapEntsCardinalityEdit PrepareFinalScriptOriginRemoval(
        this MapEntsSyntaxDocument document,
        MapEntEntityOrdinal entityOrdinal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();
        RequireCardinalityDocument(document);
        if (document.Entities.Count == 0 ||
            entityOrdinal.Value != document.Entities.Count - 1)
        {
            throw new MapEntsEditRejectedException(
                MapEntsEditRejectionReason
                    .CardinalityTargetIsNotFinalEntity,
                "Only the physical final MapEnt row can be removed.");
        }
        MapEntsSyntaxEntity entity = RequireFinalScriptOrigin(document);

        byte[] entityBytes = document.GetBytesCopy(entity.Span);
        byte[] beforeBytes = document.Serialize();
        var afterBytes = new byte[
            checked(beforeBytes.Length - entity.Span.Length)];
        beforeBytes.AsSpan(0, entity.Span.Offset).CopyTo(afterBytes);
        beforeBytes.AsSpan(entity.Span.End).CopyTo(
            afterBytes.AsSpan(entity.Span.Offset));

        MapEntsSyntaxDocument after = MapEntsSyntaxParser.Parse(
            afterBytes,
            cancellationToken);
        RequireCardinalityResult(
            document,
            after,
            entityOrdinal,
            expectedDelta: -1,
            entityBytes);
        return new MapEntsCardinalityEdit(
            document,
            after,
            MapEntityCardinalityOperation.Remove,
            entityOrdinal,
            entityBytes);
    }

    private static EncodedReplacement EncodeReplacement(
        string replacement)
    {
        var bytes = new byte[replacement.Length];
        for (int index = 0; index < replacement.Length; index++)
        {
            char value = replacement[index];
            if (value > byte.MaxValue)
            {
                throw new MapEntsEditRejectedException(
                    MapEntsEditRejectionReason.ReplacementIsNotLatin1,
                    $"Replacement character U+{(int)value:X4} at index {index} is not representable in Latin-1.");
            }
            if (value == '\0')
            {
                throw new MapEntsEditRejectedException(
                    MapEntsEditRejectionReason.ReplacementContainsNul,
                    $"Replacement contains a NUL character at index {index}.");
            }
            bytes[index] = (byte)value;
        }

        MapEntsRuntimeTokenAnalysis analysis =
            MapEntsRuntimeTokenDecoder.Analyze(bytes);
        if (analysis.UnescapedQuoteOffset is int quoteOffset)
        {
            throw new MapEntsEditRejectedException(
                MapEntsEditRejectionReason.ReplacementContainsQuote,
                $"Replacement contains an unescaped quote at index " +
                $"{quoteOffset}; use the runtime \\\" escape when a literal " +
                "quote is required.");
        }
        if (analysis.EscapesFollowingQuote)
        {
            throw new MapEntsEditRejectedException(
                MapEntsEditRejectionReason.ReplacementEscapesClosingQuote,
                "Replacement ends with an odd number of backslashes and would escape the preserved closing quote.");
        }
        if (analysis.DecodedByteLength >
            MapEntsRuntimeSyntaxLimits.MaximumDecodedTokenByteLength)
        {
            throw new MapEntsEditRejectedException(
                MapEntsEditRejectionReason.ReplacementDecodedTokenTooLong,
                $"Replacement decodes to {analysis.DecodedByteLength} " +
                "bytes; Com_Parse preserves at most " +
                $"{MapEntsRuntimeSyntaxLimits.MaximumDecodedTokenByteLength} " +
                "bytes per token.");
        }

        return new EncodedReplacement(
            bytes,
            analysis.DecodedByteLength);
    }

    private static void RequireCardinalityDocument(
        MapEntsSyntaxDocument document)
    {
        if (!document.CanEdit)
        {
            string diagnostic = document.Diagnostics.Count == 0
                ? "unknown validation failure"
                : string.Join(
                    "; ",
                    document.Diagnostics.Select(value =>
                        $"{value.Code} at byte {value.Span.Offset}"));
            throw new MapEntsEditRejectedException(
                MapEntsEditRejectionReason.DocumentIsNotEditable,
                $"MapEnts syntax is not safely editable: {diagnostic}.");
        }
        if (!document.HasTrailingNul)
        {
            throw new MapEntsEditRejectedException(
                MapEntsEditRejectionReason
                    .CardinalityRequiresTrailingNul,
                "The proven MapEnt cardinality patch requires and preserves " +
                "the compiled entity string's trailing NUL.");
        }
    }

    private static void RequireScriptOriginAppendCapacity(
        MapEntsSyntaxDocument document)
    {
        int nonWorldEntityCount = document.Entities.Count;
        if (document.Entities.Count != 0 &&
            HasExactUniqueProperty(
                document.Entities[0].Properties,
                "classname",
                "worldspawn"))
        {
            nonWorldEntityCount--;
        }
        if (nonWorldEntityCount >= MaximumNonWorldEntityCount)
        {
            throw new MapEntsEditRejectedException(
                MapEntsEditRejectionReason.CardinalityEntityLimitExceeded,
                "Appending script_origin would exceed the conservative IW4 " +
                $"limit of {MaximumNonWorldEntityCount} non-world MapEnt rows.");
        }
    }

    private static MapEntsSyntaxEntity RequireFinalScriptOrigin(
        MapEntsSyntaxDocument document)
    {
        if (document.Entities.Count == 0)
        {
            throw new MapEntsEditRejectedException(
                MapEntsEditRejectionReason
                    .CardinalityTargetIsNotFinalEntity,
                "The MapEnt snapshot has no physical final entity to remove.");
        }

        MapEntsSyntaxEntity entity = document.Entities[^1];
        _ = ValidateAndCanonicalizeScriptOrigin(
            entity.Properties.Select(value =>
                new KeyValuePair<string, string>(
                    value.Key,
                    value.Value)));
        return entity;
    }

    /// <summary>
    /// Applies the one canonical entity-cardinality schema shared by syntax
    /// mutation and executable-evidence classification. Keeping this gate
    /// singular prevents an evidence assessment from authorizing bytes the
    /// patcher would later reject.
    /// </summary>
    internal static KeyValuePair<string, string>[]
        ValidateAndCanonicalizeScriptOrigin(
            IEnumerable<KeyValuePair<string, string>> properties)
    {
        KeyValuePair<string, string>[] copy = properties.ToArray();
        string[] canonicalOrder =
        [
            "classname",
            "origin",
            "angles",
            "angle",
            "target",
            "targetname",
            "spawnflags"
        ];
        if (copy.Length == 0 ||
            copy.Any(value =>
                !canonicalOrder.Contains(
                    value.Key,
                    StringComparer.Ordinal)) ||
            copy.GroupBy(
                    value => value.Key,
                    StringComparer.Ordinal)
                .Any(group => group.Count() != 1))
        {
            RejectCardinalitySchema(
                "Only unique, lower-case reviewed keys are permitted: " +
                string.Join(", ", canonicalOrder) + ".");
        }

        Dictionary<string, string> byKey = copy.ToDictionary(
            value => value.Key,
            value => value.Value,
            StringComparer.Ordinal);
        if (!byKey.TryGetValue("classname", out string? className) ||
            !string.Equals(
                className,
                "script_origin",
                StringComparison.Ordinal))
        {
            RejectCardinalitySchema(
                "Exactly one classname equal to 'script_origin' is required.");
        }
        if (!byKey.TryGetValue("origin", out string? origin) ||
            !TryParseFiniteVector(origin, expectedComponents: 3))
        {
            RejectCardinalitySchema(
                "Exactly one finite three-component origin is required.");
        }
        if (byKey.ContainsKey("angles") &&
            byKey.ContainsKey("angle"))
        {
            RejectCardinalitySchema(
                "Use either angles or angle, not both.");
        }
        if (byKey.TryGetValue("angles", out string? angles) &&
            !TryParseFiniteVector(angles, expectedComponents: 3))
        {
            RejectCardinalitySchema(
                "angles must contain exactly three finite components.");
        }
        if (byKey.TryGetValue("angle", out string? angle) &&
            !TryParseFiniteVector(angle, expectedComponents: 1))
        {
            RejectCardinalitySchema(
                "angle must contain exactly one finite component.");
        }
        if (byKey.TryGetValue("spawnflags", out string? spawnFlags) &&
            !int.TryParse(
                spawnFlags,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out _))
        {
            RejectCardinalitySchema(
                "spawnflags must be a signed 32-bit integer.");
        }
        if (copy.Any(value =>
                string.Equals(
                    value.Key,
                    "model",
                    StringComparison.OrdinalIgnoreCase) ||
                value.Value.StartsWith('*')))
        {
            RejectCardinalitySchema(
                "Model and compiled-geometry markers are not permitted.");
        }

        long runtimePoolBytes = 0;
        foreach (KeyValuePair<string, string> property in copy)
        {
            EncodedReplacement key = EncodeReplacement(property.Key);
            EncodedReplacement value = EncodeReplacement(property.Value);
            runtimePoolBytes +=
                key.RuntimeDecodedByteLength + 1L +
                value.RuntimeDecodedByteLength + 1L;
        }
        if (copy.Length >
            MapEntsRuntimeSyntaxLimits.MaximumPropertyCountPerEntity)
        {
            RejectCardinalitySchema(
                "The authored row exceeds the runtime SpawnVar property " +
                $"limit of {MapEntsRuntimeSyntaxLimits.MaximumPropertyCountPerEntity}.");
        }
        if (runtimePoolBytes >
            MapEntsRuntimeSyntaxLimits
                .MaximumDecodedPropertyPoolByteLengthPerEntity)
        {
            throw new MapEntsEditRejectedException(
                MapEntsEditRejectionReason
                    .ReplacementExceedsRuntimeSpawnVarPool,
                $"The authored row requires {runtimePoolBytes} decoded " +
                "key/value bytes including NUL terminators; the runtime " +
                $"SpawnVar pool holds at most " +
                $"{MapEntsRuntimeSyntaxLimits.MaximumDecodedPropertyPoolByteLengthPerEntity}.");
        }

        return canonicalOrder
            .Where(byKey.ContainsKey)
            .Select(key =>
                new KeyValuePair<string, string>(key, byKey[key]))
            .ToArray();
    }

    private static byte[] EncodeCanonicalEntity(
        IEnumerable<KeyValuePair<string, string>> properties)
    {
        var bytes = new List<byte>();
        bytes.Add((byte)'{');
        foreach (KeyValuePair<string, string> property in properties)
        {
            bytes.Add((byte)'\n');
            bytes.Add((byte)'"');
            bytes.AddRange(Encoding.Latin1.GetBytes(property.Key));
            bytes.Add((byte)'"');
            bytes.Add((byte)' ');
            bytes.Add((byte)'"');
            bytes.AddRange(Encoding.Latin1.GetBytes(property.Value));
            bytes.Add((byte)'"');
        }
        bytes.Add((byte)'\n');
        bytes.Add((byte)'}');
        return bytes.ToArray();
    }

    private static void RequireCardinalityResult(
        MapEntsSyntaxDocument before,
        MapEntsSyntaxDocument after,
        MapEntEntityOrdinal affectedOrdinal,
        int expectedDelta,
        ReadOnlySpan<byte> entityBytes)
    {
        if (!after.CanEdit ||
            !after.HasTrailingNul ||
            after.Entities.Count !=
                checked(before.Entities.Count + expectedDelta))
        {
            throw new InvalidOperationException(
                "A validated MapEnt cardinality transition did not preserve " +
                "strict syntax and the trailing NUL.");
        }
        int sharedCount = Math.Min(
            before.Entities.Count,
            after.Entities.Count);
        for (int index = 0; index < sharedCount; index++)
        {
            if (!before.GetBytesCopy(before.Entities[index].Span)
                    .AsSpan()
                    .SequenceEqual(
                        after.GetBytesCopy(after.Entities[index].Span)))
            {
                throw new InvalidOperationException(
                    $"MapEnt cardinality transition changed existing entity {index}.");
            }
        }

        MapEntsSyntaxDocument syntaxWithEntity =
            expectedDelta > 0 ? after : before;
        MapEntsSyntaxEntity affected =
            syntaxWithEntity.GetEntity(affectedOrdinal);
        if (!syntaxWithEntity.GetBytesCopy(affected.Span)
                .AsSpan()
                .SequenceEqual(entityBytes))
        {
            throw new InvalidOperationException(
                "MapEnt cardinality transition did not retain the exact " +
                "authorized entity bytes.");
        }
    }

    private static bool HasExactUniqueProperty(
        IEnumerable<MapEntsSyntaxProperty> properties,
        string key,
        string value)
    {
        string[] matches = properties
            .Where(property => string.Equals(
                property.Key,
                key,
                StringComparison.Ordinal))
            .Select(property => property.Value)
            .ToArray();
        return matches.Length == 1 &&
               string.Equals(matches[0], value, StringComparison.Ordinal);
    }

    private static bool TryParseFiniteVector(
        string value,
        int expectedComponents)
    {
        string[] components = value.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries);
        return components.Length == expectedComponents &&
               components.All(component =>
                   float.TryParse(
                       component,
                       NumberStyles.Float,
                       CultureInfo.InvariantCulture,
                       out float parsed) &&
                   float.IsFinite(parsed));
    }

    private static void RejectCardinalitySchema(string message) =>
        throw new MapEntsEditRejectedException(
            MapEntsEditRejectionReason
                .CardinalitySchemaIsNotAuthorized,
            message);

    private static void RequireEquivalentStructure(
        MapEntsSyntaxDocument before,
        MapEntsSyntaxDocument after)
    {
        if (!after.CanEdit ||
            before.Entities.Count != after.Entities.Count)
        {
            throw new InvalidOperationException(
                "A validated MapEnt property replacement did not preserve the parsed entity structure.");
        }

        for (int entityIndex = 0;
             entityIndex < before.Entities.Count;
             entityIndex++)
        {
            if (before.Entities[entityIndex].Properties.Count !=
                after.Entities[entityIndex].Properties.Count)
            {
                throw new InvalidOperationException(
                    "A validated MapEnt property replacement changed property cardinality.");
            }
        }
    }

    private readonly record struct EncodedReplacement(
        byte[] Bytes,
        int RuntimeDecodedByteLength);
}

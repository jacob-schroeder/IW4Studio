using System.Text;

namespace IW4.Studio.MapEditor.Editing.MapEntsSyntax;

/// <summary>
/// Strict structural parser for the compiled MapEnts entity-string payload.
/// ISO-8859-1 decoding is byte preserving. Malformed input produces a
/// non-editable document whose original bytes can still be serialized exactly.
/// </summary>
public static class MapEntsSyntaxParser
{
    public static MapEntsSyntaxDocument Parse(
        ReadOnlySpan<byte> sourceBytes,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int contentEnd = sourceBytes.Length;
        bool hasTrailingNul =
            contentEnd != 0 && sourceBytes[contentEnd - 1] == 0;
        if (hasTrailingNul)
            contentEnd--;

        var diagnostics = new List<MapEntsSyntaxDiagnostic>();
        for (int index = 0; index < contentEnd; index++)
        {
            if ((index & 4095) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            if (sourceBytes[index] == 0)
            {
                diagnostics.Add(new MapEntsSyntaxDiagnostic(
                    MapEntsSyntaxDiagnosticCode.EmbeddedNul,
                    new MapEntSourceSpan(index, 1),
                    "MapEnts entity text may contain only one optional trailing NUL byte."));
            }
        }

        var entities = new List<MapEntsSyntaxEntity>();
        int offset = 0;
        while (offset < contentEnd)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SkipTrivia(sourceBytes, contentEnd, ref offset);
            if (offset >= contentEnd)
                break;

            if (sourceBytes[offset] != (byte)'{')
            {
                MapEntSourceSpan unknown = ConsumeUnexpected(
                    sourceBytes,
                    contentEnd,
                    ref offset);
                diagnostics.Add(new MapEntsSyntaxDiagnostic(
                    MapEntsSyntaxDiagnosticCode.UnexpectedToken,
                    unknown,
                    "Expected an entity opening brace."));
                continue;
            }

            entities.Add(ParseEntity(
                sourceBytes,
                contentEnd,
                new MapEntEntityOrdinal(entities.Count),
                ref offset,
                diagnostics,
                cancellationToken));
        }

        return new MapEntsSyntaxDocument(
            sourceBytes,
            entities,
            diagnostics,
            hasTrailingNul);
    }

    private static MapEntsSyntaxEntity ParseEntity(
        ReadOnlySpan<byte> source,
        int contentEnd,
        MapEntEntityOrdinal entityOrdinal,
        ref int offset,
        ICollection<MapEntsSyntaxDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        int entityStart = offset;
        var openBrace = new MapEntSourceSpan(offset, 1);
        offset++;
        var properties = new List<MapEntsSyntaxProperty>();
        var closeBrace = new MapEntSourceSpan(contentEnd, 0);
        bool closed = false;
        bool propertyNeedsFollowingDelimiter = false;
        long runtimePropertyPoolByteLength = 0;
        bool reportedPropertyLimit = false;
        bool reportedPropertyPoolLimit = false;

        while (offset < contentEnd)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int triviaStart = offset;
            SkipTrivia(source, contentEnd, ref offset);
            bool hadTrivia = offset != triviaStart;
            if (offset >= contentEnd)
                break;

            byte current = source[offset];
            if (current == (byte)'}')
            {
                closeBrace = new MapEntSourceSpan(offset, 1);
                offset++;
                closed = true;
                break;
            }

            if (propertyNeedsFollowingDelimiter && !hadTrivia)
            {
                diagnostics.Add(new MapEntsSyntaxDiagnostic(
                    MapEntsSyntaxDiagnosticCode.MissingPropertySeparator,
                    new MapEntSourceSpan(offset, 0),
                    "Adjacent MapEnt properties require ASCII whitespace."));
            }
            propertyNeedsFollowingDelimiter = false;

            if (current != (byte)'"')
            {
                MapEntSourceSpan unknown = ConsumeUnexpected(
                    source,
                    contentEnd,
                    ref offset);
                diagnostics.Add(new MapEntsSyntaxDiagnostic(
                    MapEntsSyntaxDiagnosticCode.UnexpectedToken,
                    unknown,
                    current == (byte)'{'
                        ? "Nested entity braces are not valid MapEnt property syntax."
                        : "Expected a quoted MapEnt property key."));
                continue;
            }

            if (!TryReadQuotedToken(
                    source,
                    contentEnd,
                    ref offset,
                    cancellationToken,
                    out QuotedToken key))
            {
                diagnostics.Add(new MapEntsSyntaxDiagnostic(
                    MapEntsSyntaxDiagnosticCode.UnterminatedQuotedToken,
                    key.TokenSpan,
                    "MapEnt property key has no closing quote."));
                break;
            }

            int separatorStart = offset;
            SkipTrivia(source, contentEnd, ref offset);
            if (separatorStart == offset)
            {
                diagnostics.Add(new MapEntsSyntaxDiagnostic(
                    MapEntsSyntaxDiagnosticCode.MissingPropertySeparator,
                    new MapEntSourceSpan(offset, 0),
                    "MapEnt property key and value require ASCII whitespace."));
            }

            if (offset >= contentEnd ||
                source[offset] != (byte)'"')
            {
                int diagnosticLength =
                    offset < contentEnd ? 1 : 0;
                diagnostics.Add(new MapEntsSyntaxDiagnostic(
                    MapEntsSyntaxDiagnosticCode.ExpectedQuotedValue,
                    new MapEntSourceSpan(offset, diagnosticLength),
                    "Expected a quoted MapEnt property value."));
                if (offset < contentEnd &&
                    source[offset] != (byte)'}')
                {
                    ConsumeUnexpected(
                        source,
                        contentEnd,
                        ref offset);
                }
                continue;
            }

            if (!TryReadQuotedToken(
                    source,
                    contentEnd,
                    ref offset,
                    cancellationToken,
                    out QuotedToken value))
            {
                diagnostics.Add(new MapEntsSyntaxDiagnostic(
                    MapEntsSyntaxDiagnosticCode.UnterminatedQuotedToken,
                    value.TokenSpan,
                    "MapEnt property value has no closing quote."));
                break;
            }

            var propertyOrdinal = new MapEntPropertyOrdinal(
                properties.Count);
            var property = new MapEntsSyntaxProperty(
                entityOrdinal,
                propertyOrdinal,
                key.Text,
                value.Text,
                key.RuntimeDecodedByteLength,
                value.RuntimeDecodedByteLength,
                new MapEntSourceSpan(
                    key.TokenSpan.Offset,
                    value.TokenSpan.End - key.TokenSpan.Offset),
                key.TokenSpan,
                key.ContentSpan,
                value.TokenSpan,
                value.ContentSpan);

            AddDecodedTokenLimitDiagnostic(
                entityOrdinal,
                propertyOrdinal,
                "key",
                key,
                diagnostics);
            AddDecodedTokenLimitDiagnostic(
                entityOrdinal,
                propertyOrdinal,
                "value",
                value,
                diagnostics);

            if (!reportedPropertyLimit &&
                properties.Count >=
                MapEntsRuntimeSyntaxLimits.MaximumPropertyCountPerEntity)
            {
                diagnostics.Add(new MapEntsSyntaxDiagnostic(
                    MapEntsSyntaxDiagnosticCode
                        .RuntimePropertyCountExceeded,
                    property.Span,
                    $"MapEnt entity #{entityOrdinal.Value} property " +
                    $"#{propertyOrdinal.Value} exceeds the runtime limit of " +
                    $"{MapEntsRuntimeSyntaxLimits.MaximumPropertyCountPerEntity} " +
                    "properties per entity."));
                reportedPropertyLimit = true;
            }

            long poolAfterKey = runtimePropertyPoolByteLength +
                key.RuntimeDecodedByteLength + 1L;
            long poolAfterValue = poolAfterKey +
                value.RuntimeDecodedByteLength + 1L;
            if (!reportedPropertyPoolLimit &&
                poolAfterValue >
                MapEntsRuntimeSyntaxLimits
                    .MaximumDecodedPropertyPoolByteLengthPerEntity)
            {
                bool keyExceedsPool =
                    poolAfterKey >
                    MapEntsRuntimeSyntaxLimits
                        .MaximumDecodedPropertyPoolByteLengthPerEntity;
                MapEntSourceSpan overflowSpan =
                    keyExceedsPool
                        ? key.ContentSpan
                        : value.ContentSpan;
                long requiredPoolByteLength =
                    keyExceedsPool
                        ? poolAfterKey
                        : poolAfterValue;
                diagnostics.Add(new MapEntsSyntaxDiagnostic(
                    MapEntsSyntaxDiagnosticCode
                        .RuntimeSpawnVarPoolExceeded,
                    overflowSpan,
                    $"MapEnt entity #{entityOrdinal.Value} decoded " +
                    $"key/value storage requires " +
                    $"{requiredPoolByteLength} bytes " +
                    "including NUL terminators; the runtime SpawnVar pool " +
                    $"holds at most " +
                    $"{MapEntsRuntimeSyntaxLimits.MaximumDecodedPropertyPoolByteLengthPerEntity} " +
                    "bytes per entity."));
                reportedPropertyPoolLimit = true;
            }

            runtimePropertyPoolByteLength = poolAfterValue;
            properties.Add(property);
            propertyNeedsFollowingDelimiter = true;
        }

        if (!closed)
        {
            diagnostics.Add(new MapEntsSyntaxDiagnostic(
                MapEntsSyntaxDiagnosticCode.UnterminatedEntity,
                new MapEntSourceSpan(
                    entityStart,
                    contentEnd - entityStart),
                "MapEnt entity has no closing brace."));
            offset = contentEnd;
        }

        return new MapEntsSyntaxEntity(
            entityOrdinal,
            new MapEntSourceSpan(
                entityStart,
                offset - entityStart),
            openBrace,
            closeBrace,
            properties);
    }

    private static bool TryReadQuotedToken(
        ReadOnlySpan<byte> source,
        int contentEnd,
        ref int offset,
        CancellationToken cancellationToken,
        out QuotedToken token)
    {
        int tokenStart = offset;
        int contentStart = ++offset;
        int precedingBackslashes = 0;
        while (offset < contentEnd)
        {
            if ((offset & 4095) == 0)
                cancellationToken.ThrowIfCancellationRequested();

            byte current = source[offset];
            if (current == (byte)'"' &&
                (precedingBackslashes & 1) == 0)
            {
                var contentSpan = new MapEntSourceSpan(
                    contentStart,
                    offset - contentStart);
                offset++;
                token = new QuotedToken(
                    new MapEntSourceSpan(
                        tokenStart,
                        offset - tokenStart),
                    contentSpan,
                    Encoding.Latin1.GetString(
                        source.Slice(
                            contentSpan.Offset,
                            contentSpan.Length)),
                    MapEntsRuntimeTokenDecoder
                        .Analyze(source.Slice(
                            contentSpan.Offset,
                            contentSpan.Length))
                        .DecodedByteLength);
                return true;
            }

            precedingBackslashes = current == (byte)'\\'
                ? precedingBackslashes + 1
                : 0;
            offset++;
        }

        var unterminatedContent = new MapEntSourceSpan(
            contentStart,
            contentEnd - contentStart);
        token = new QuotedToken(
            new MapEntSourceSpan(
                tokenStart,
                contentEnd - tokenStart),
            unterminatedContent,
            Encoding.Latin1.GetString(
                source.Slice(
                    unterminatedContent.Offset,
                    unterminatedContent.Length)),
            MapEntsRuntimeTokenDecoder
                .Analyze(source.Slice(
                    unterminatedContent.Offset,
                    unterminatedContent.Length))
                .DecodedByteLength);
        return false;
    }

    private static void AddDecodedTokenLimitDiagnostic(
        MapEntEntityOrdinal entityOrdinal,
        MapEntPropertyOrdinal propertyOrdinal,
        string fieldName,
        QuotedToken token,
        ICollection<MapEntsSyntaxDiagnostic> diagnostics)
    {
        if (token.RuntimeDecodedByteLength <=
            MapEntsRuntimeSyntaxLimits.MaximumDecodedTokenByteLength)
        {
            return;
        }

        diagnostics.Add(new MapEntsSyntaxDiagnostic(
            MapEntsSyntaxDiagnosticCode.RuntimeDecodedTokenTooLong,
            token.ContentSpan,
            $"MapEnt entity #{entityOrdinal.Value} property " +
            $"#{propertyOrdinal.Value} {fieldName} decodes to " +
            $"{token.RuntimeDecodedByteLength} bytes; Com_Parse preserves " +
            $"at most " +
            $"{MapEntsRuntimeSyntaxLimits.MaximumDecodedTokenByteLength} " +
            "bytes per token."));
    }

    private static void SkipTrivia(
        ReadOnlySpan<byte> source,
        int contentEnd,
        ref int offset)
    {
        while (offset < contentEnd &&
               source[offset] is (
                   (byte)' ' or
                   (byte)'\t' or
                   (byte)'\r' or
                   (byte)'\n'))
        {
            offset++;
        }
    }

    private static MapEntSourceSpan ConsumeUnexpected(
        ReadOnlySpan<byte> source,
        int contentEnd,
        ref int offset)
    {
        int start = offset;
        if (source[offset] is (byte)'{' or (byte)'}' or (byte)'"')
        {
            offset++;
        }
        else
        {
            while (offset < contentEnd &&
                   !IsTrivia(source[offset]) &&
                   source[offset] is not (
                       (byte)'{' or
                       (byte)'}' or
                       (byte)'"'))
            {
                offset++;
            }
        }

        return new MapEntSourceSpan(start, offset - start);
    }

    private static bool IsTrivia(byte value) =>
        value is (
            (byte)' ' or
            (byte)'\t' or
            (byte)'\r' or
            (byte)'\n');

    private readonly record struct QuotedToken(
        MapEntSourceSpan TokenSpan,
        MapEntSourceSpan ContentSpan,
        string Text,
        int RuntimeDecodedByteLength);
}

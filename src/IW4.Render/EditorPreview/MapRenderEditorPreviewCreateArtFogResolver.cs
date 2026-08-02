using System.Globalization;
using System.Numerics;
using IW4.Assets.Assets.RawFile;
using IW4.Render.Assets;
using IW4.Render.Scheduling.Fog;

namespace IW4.Render.EditorPreview;

/// <summary>
/// Resolves the canonical map createart RawFile and translates its single
/// setExpFog call into the active GfxFog shape consumed by R_SetFrameFog.
/// </summary>
public static class MapRenderEditorPreviewCreateArtFogResolver
{
    private const int MaximumScriptByteCount = 4 * 1024 * 1024;
    private const int SetExpFogArgumentCount = 14;
    // The createart command path stores the single-precision ln(2)
    // approximation below before dividing by halfDistance. Retaining that
    // float, rather than recomputing MathF.Log(2), reproduces the captured
    // mp_boneyard density bit-for-bit.
    private const float HalfTransmissionNaturalLog = 0.6931471f;

    public static MapRenderEditorPreviewCreateArtFogResolution Resolve(
        string? mapIdentity,
        long expectedPoolRevision,
        IMapRenderCanonicalRawFileProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (expectedPoolRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(expectedPoolRevision));

        if (!TryCreateCanonicalRawFileName(
                mapIdentity,
                out string canonicalName,
                out string mapDetail))
        {
            return Failed(
                MapRenderEditorPreviewCreateArtFogStatus.InvalidMapIdentity,
                mapDetail);
        }

        if (!provider.HasCanonicalAssetPoolRevision(expectedPoolRevision))
        {
            return Failed(
                MapRenderEditorPreviewCreateArtFogStatus
                    .AssetPoolRevisionMismatch,
                $"The active RawFile provider no longer owns pool revision {expectedPoolRevision}.");
        }

        bool rawFileResolved = provider.TryResolveCanonicalRawFile(
            canonicalName,
            expectedPoolRevision,
            out RawFileAsset? rawFile);
        // The provider boundary is intentionally checked on both sides of the
        // lookup. A canonical-pool replacement during lookup must not be
        // misclassified as an absent createart file and authorize neutral fog.
        if (!provider.HasCanonicalAssetPoolRevision(expectedPoolRevision))
        {
            return Failed(
                MapRenderEditorPreviewCreateArtFogStatus
                    .AssetPoolRevisionMismatch,
                $"The active RawFile provider changed while resolving '{canonicalName}' at pool revision {expectedPoolRevision}.");
        }

        if (!rawFileResolved)
        {
            return Failed(
                MapRenderEditorPreviewCreateArtFogStatus
                    .CanonicalRawFileAbsent,
                $"Canonical createart RawFile '{canonicalName}' is absent at pool revision {expectedPoolRevision}.");
        }
        if (rawFile is null)
        {
            return Failed(
                MapRenderEditorPreviewCreateArtFogStatus
                    .RawFileMetadataInvalid,
                $"Canonical createart RawFile provider returned a successful lookup with no asset for '{canonicalName}'.");
        }

        if (!MapRenderCanonicalRawFileTextDecoder.TryDecode(
                rawFile,
                MaximumScriptByteCount,
                out string script,
                out MapRenderCanonicalRawFileTextDecodeStatus decodeStatus,
                out string decodeDetail))
        {
            return Failed(
                decodeStatus switch
                {
                    MapRenderCanonicalRawFileTextDecodeStatus.BufferMissing =>
                        MapRenderEditorPreviewCreateArtFogStatus
                            .RawFileBufferMissing,
                    MapRenderCanonicalRawFileTextDecodeStatus.DecodeFailed =>
                        MapRenderEditorPreviewCreateArtFogStatus
                            .RawFileDecodeFailed,
                    _ => MapRenderEditorPreviewCreateArtFogStatus
                        .RawFileMetadataInvalid
                },
                $"Canonical createart RawFile '{canonicalName}' is unusable: {decodeDetail}");
        }

        if (!TryFindSetExpFogCall(
                script,
                out string arguments,
                out MapRenderEditorPreviewCreateArtFogStatus callStatus,
                out string callDetail))
        {
            return Failed(
                callStatus,
                $"Canonical createart RawFile '{canonicalName}' is unusable: {callDetail}");
        }

        if (!TryCreateActiveFog(
                arguments,
                out MapRenderActiveFogState? activeFog,
                out MapRenderEditorPreviewCreateArtFogStatus valueStatus,
                out string valueDetail))
        {
            return Failed(
                valueStatus,
                $"Canonical createart RawFile '{canonicalName}' has an invalid setExpFog call: {valueDetail}");
        }

        return new MapRenderEditorPreviewCreateArtFogResolution(
            MapRenderEditorPreviewCreateArtFogStatus.Ready,
            activeFog,
            $"Canonical createart RawFile '{canonicalName}' supplied the active Live Preview fog state.");
    }

    internal static bool TryCreateCanonicalRawFileName(
        string? mapIdentity,
        out string canonicalName,
        out string detail)
    {
        canonicalName = string.Empty;
        detail = string.Empty;
        if (string.IsNullOrWhiteSpace(mapIdentity))
        {
            detail = "A map identity is required for canonical createart lookup.";
            return false;
        }

        string normalized = mapIdentity
            .Trim()
            .Replace('\\', '/');
        string mapName = normalized.Split(
                '/',
                StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault() ?? string.Empty;
        int extension = mapName.LastIndexOf('.');
        if (extension > 0)
            mapName = mapName[..extension];
        if (mapName.Length == 0 || mapName.Any(character =>
                !char.IsAsciiLetterOrDigit(character) &&
                character != '_' &&
                character != '-'))
        {
            detail = $"Map identity '{mapIdentity}' does not contain a safe canonical map name.";
            return false;
        }

        canonicalName =
            $"maps/createart/{mapName.ToLowerInvariant()}_art.gsc";
        return true;
    }

    private static bool TryFindSetExpFogCall(
        string script,
        out string arguments,
        out MapRenderEditorPreviewCreateArtFogStatus status,
        out string detail)
    {
        arguments = string.Empty;
        status = MapRenderEditorPreviewCreateArtFogStatus.SetExpFogMissing;
        detail = string.Empty;
        if (!TryMaskComments(
                script,
                out string searchable,
                out detail))
        {
            status = MapRenderEditorPreviewCreateArtFogStatus.SetExpFogMalformed;
            return false;
        }

        var calls = new List<string>();
        for (int index = 0; index < searchable.Length;)
        {
            char character = searchable[index];
            if (character is '\'' or '"')
            {
                if (!TrySkipQuoted(searchable, index, out index))
                {
                    status = MapRenderEditorPreviewCreateArtFogStatus.SetExpFogMalformed;
                    detail = "Createart contains an unterminated quoted string.";
                    return false;
                }
                continue;
            }

            if (!IsIdentifierStart(character))
            {
                index++;
                continue;
            }

            int start = index++;
            while (index < searchable.Length &&
                   IsIdentifierPart(searchable[index]))
            {
                index++;
            }

            if (!searchable.AsSpan(start, index - start)
                    .SequenceEqual("setExpFog"))
            {
                continue;
            }

            int open = index;
            while (open < searchable.Length && char.IsWhiteSpace(searchable[open]))
                open++;
            if (open >= searchable.Length || searchable[open] != '(')
            {
                status = MapRenderEditorPreviewCreateArtFogStatus.SetExpFogMalformed;
                detail = "setExpFog is not followed by an argument list.";
                return false;
            }

            if (!TryReadBalancedParentheses(
                    searchable,
                    open,
                    out int close,
                    out string callArguments,
                    out detail))
            {
                status = MapRenderEditorPreviewCreateArtFogStatus.SetExpFogMalformed;
                return false;
            }

            calls.Add(callArguments);
            index = close + 1;
        }

        if (calls.Count == 0)
        {
            detail = "No executable setExpFog call was found.";
            return false;
        }
        if (calls.Count != 1)
        {
            status = MapRenderEditorPreviewCreateArtFogStatus.SetExpFogAmbiguous;
            detail = $"Expected one executable setExpFog call, found {calls.Count}.";
            return false;
        }

        arguments = calls[0];
        return true;
    }

    private static bool TryCreateActiveFog(
        string arguments,
        out MapRenderActiveFogState? activeFog,
        out MapRenderEditorPreviewCreateArtFogStatus status,
        out string detail)
    {
        activeFog = null;
        status = MapRenderEditorPreviewCreateArtFogStatus.SetExpFogMalformed;
        detail = string.Empty;
        if (!TrySplitTopLevel(arguments, out string[] fields, out detail) ||
            fields.Length != SetExpFogArgumentCount)
        {
            if (detail.Length == 0)
            {
                detail =
                    $"Expected {SetExpFogArgumentCount} top-level arguments, found {fields.Length}.";
            }
            return false;
        }

        float[] scalar = new float[SetExpFogArgumentCount - 1];
        int scalarIndex = 0;
        for (int fieldIndex = 0; fieldIndex < fields.Length; fieldIndex++)
        {
            if (fieldIndex == 10)
                continue;
            if (!float.TryParse(
                    fields[fieldIndex],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out float value) ||
                !float.IsFinite(value))
            {
                detail = $"Argument {fieldIndex} is not a finite float.";
                return false;
            }

            scalar[scalarIndex++] = value;
        }

        if (!TryParseDirection(fields[10], out Vector3 direction, out detail))
            return false;

        float fogStart = scalar[0];
        float halfDistance = scalar[1];
        float fogRed = scalar[2];
        float fogGreen = scalar[3];
        float fogBlue = scalar[4];
        float fogMaxOpacity = scalar[5];
        float transitionSeconds = scalar[6];
        float sunRed = scalar[7];
        float sunGreen = scalar[8];
        float sunBlue = scalar[9];
        float beginAngle = scalar[10];
        float endAngle = scalar[11];
        float sunFogScale = scalar[12];

        float directionLengthSquared = direction.LengthSquared();
        if (fogStart < 0f || halfDistance <= 0f ||
            transitionSeconds < 0f ||
            !Unit(fogRed) || !Unit(fogGreen) || !Unit(fogBlue) ||
            !Unit(fogMaxOpacity) ||
            !Unit(sunRed) || !Unit(sunGreen) || !Unit(sunBlue) ||
            beginAngle < 0f || beginAngle > 180f ||
            endAngle < 0f || endAngle > 180f ||
            beginAngle >= endAngle ||
            sunFogScale < 0f ||
            !float.IsFinite(directionLengthSquared) ||
            directionLengthSquared <= 0.00000001f)
        {
            status = MapRenderEditorPreviewCreateArtFogStatus
                .SetExpFogValueOutOfRange;
            detail = "One or more setExpFog values are outside the supported GfxFog domain.";
            return false;
        }
        if (transitionSeconds != 0f)
        {
            status = MapRenderEditorPreviewCreateArtFogStatus
                .SetExpFogTransitionUnsupported;
            detail =
                "A nonzero setExpFog transition requires previous-fog and frame-time interpolation state that Live Preview does not yet model.";
            return false;
        }

        float density = HalfTransmissionNaturalLog / halfDistance;
        if (!float.IsFinite(density) || density <= 0f)
        {
            status = MapRenderEditorPreviewCreateArtFogStatus
                .SetExpFogValueOutOfRange;
            detail = "Fog half-distance did not produce a finite positive density.";
            return false;
        }
        float fogDistanceTerm = density * fogStart;
        float sunDensityTerm = density * sunFogScale;
        if (!float.IsFinite(fogDistanceTerm) ||
            !float.IsFinite(sunDensityTerm))
        {
            status = MapRenderEditorPreviewCreateArtFogStatus
                .SetExpFogValueOutOfRange;
            detail =
                "Fog density produced a nonfinite distance or sun-scale row term.";
            return false;
        }

        var fogColor = new MapRenderBgra8Color(
            ToByte(fogBlue),
            ToByte(fogGreen),
            ToByte(fogRed),
            byte.MaxValue);
        var sunColor = new MapRenderBgra8Color(
            ToByte(sunBlue),
            ToByte(sunGreen),
            ToByte(sunRed),
            byte.MaxValue);
        activeFog = new MapRenderActiveFogState(
            startTime: 0,
            finishTime: 0,
            fogColor,
            fogStart,
            density,
            fogMaxOpacity,
            new MapRenderActiveSunFogState(
                enabled: true,
                sunColor,
                direction,
                beginAngle,
                endAngle,
                sunFogScale));
        return true;
    }

    private static bool TryParseDirection(
        string field,
        out Vector3 direction,
        out string detail)
    {
        direction = default;
        detail = string.Empty;
        string trimmed = field.Trim();
        if (trimmed.Length < 5 || trimmed[0] != '(' || trimmed[^1] != ')')
        {
            detail = "Sun-fog direction is not a parenthesized vector.";
            return false;
        }

        if (!TrySplitTopLevel(
                trimmed[1..^1],
                out string[] components,
                out detail) ||
            components.Length != 3)
        {
            if (detail.Length == 0)
                detail = "Sun-fog direction must contain three components.";
            return false;
        }

        float[] values = new float[3];
        for (int index = 0; index < values.Length; index++)
        {
            if (!float.TryParse(
                    components[index],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out values[index]) ||
                !float.IsFinite(values[index]))
            {
                detail = $"Sun-fog direction component {index} is not finite.";
                return false;
            }
        }

        direction = new(values[0], values[1], values[2]);
        return true;
    }

    private static bool TrySplitTopLevel(
        string text,
        out string[] fields,
        out string detail)
    {
        var result = new List<string>();
        int depth = 0;
        int start = 0;
        detail = string.Empty;
        for (int index = 0; index < text.Length; index++)
        {
            switch (text[index])
            {
                case '(':
                    depth++;
                    break;
                case ')':
                    if (--depth < 0)
                    {
                        fields = [];
                        detail = "Argument list has an unmatched closing parenthesis.";
                        return false;
                    }
                    break;
                case ',' when depth == 0:
                    result.Add(text[start..index].Trim());
                    start = index + 1;
                    break;
            }
        }

        if (depth != 0)
        {
            fields = [];
            detail = "Argument list has an unmatched opening parenthesis.";
            return false;
        }

        result.Add(text[start..].Trim());
        if (result.Any(string.IsNullOrWhiteSpace))
        {
            fields = [];
            detail = "Argument list contains an empty field.";
            return false;
        }

        fields = result.ToArray();
        return true;
    }

    private static bool TryReadBalancedParentheses(
        string text,
        int open,
        out int close,
        out string arguments,
        out string detail)
    {
        int depth = 0;
        close = -1;
        arguments = string.Empty;
        detail = string.Empty;
        for (int index = open; index < text.Length; index++)
        {
            if (text[index] is '\'' or '"')
            {
                if (!TrySkipQuoted(text, index, out int next))
                {
                    detail = "setExpFog contains an unterminated quoted string.";
                    return false;
                }
                index = next - 1;
                continue;
            }

            if (text[index] == '(')
                depth++;
            else if (text[index] == ')' && --depth == 0)
            {
                close = index;
                arguments = text[(open + 1)..close];
                return true;
            }
        }

        detail = "setExpFog has an unterminated argument list.";
        return false;
    }

    private static bool TryMaskComments(
        string text,
        out string masked,
        out string detail)
    {
        char[] result = text.ToCharArray();
        detail = string.Empty;
        for (int index = 0; index < result.Length;)
        {
            if (result[index] is '\'' or '"')
            {
                if (!TrySkipQuoted(text, index, out int next))
                {
                    masked = string.Empty;
                    detail = "Createart contains an unterminated quoted string.";
                    return false;
                }
                index = next;
                continue;
            }

            if (result[index] != '/' || index + 1 >= result.Length)
            {
                index++;
                continue;
            }

            if (result[index + 1] == '/')
            {
                result[index++] = ' ';
                result[index++] = ' ';
                while (index < result.Length && result[index] is not ('\r' or '\n'))
                    result[index++] = ' ';
                continue;
            }

            if (result[index + 1] == '*')
            {
                result[index++] = ' ';
                result[index++] = ' ';
                bool closed = false;
                while (index < result.Length)
                {
                    if (index + 1 < result.Length &&
                        result[index] == '*' && result[index + 1] == '/')
                    {
                        result[index++] = ' ';
                        result[index++] = ' ';
                        closed = true;
                        break;
                    }
                    if (result[index] is not ('\r' or '\n'))
                        result[index] = ' ';
                    index++;
                }
                if (!closed)
                {
                    masked = string.Empty;
                    detail = "Createart contains an unterminated block comment.";
                    return false;
                }
                continue;
            }

            index++;
        }

        masked = new string(result);
        return true;
    }

    private static bool TrySkipQuoted(
        string text,
        int quote,
        out int next)
    {
        char delimiter = text[quote];
        for (int index = quote + 1; index < text.Length; index++)
        {
            if (text[index] == '\\')
            {
                index++;
                continue;
            }
            if (text[index] == delimiter)
            {
                next = index + 1;
                return true;
            }
        }

        next = text.Length;
        return false;
    }

    private static bool IsIdentifierStart(char character) =>
        char.IsAsciiLetter(character) || character == '_';

    private static bool IsIdentifierPart(char character) =>
        IsIdentifierStart(character) || char.IsAsciiDigit(character);

    private static bool Unit(float value) => value is >= 0f and <= 1f;

    private static byte ToByte(float value) => checked((byte)Math.Clamp(
        (int)MathF.Round(value * byte.MaxValue),
        byte.MinValue,
        byte.MaxValue));

    private static MapRenderEditorPreviewCreateArtFogResolution Failed(
        MapRenderEditorPreviewCreateArtFogStatus status,
        string detail) =>
        new(status, null, detail);
}

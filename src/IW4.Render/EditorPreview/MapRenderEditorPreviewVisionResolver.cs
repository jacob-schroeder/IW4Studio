using System.Globalization;
using System.Numerics;
using IW4.Assets.Assets.RawFile;
using IW4.Render.Assets;

namespace IW4.Render.EditorPreview;

/// <summary>
/// Resolves the immediate VisionSetNaked selection authored by map createart
/// and parses the renderer-owned subset of the selected .vision RawFile.
/// </summary>
public static class MapRenderEditorPreviewVisionResolver
{
    private const int MaximumTextByteCount = 4 * 1024 * 1024;
    private const float MaximumStrength = 16f;
    private const float MaximumTint = 16f;
    private const float MaximumGlowRadius = 1024f;

    public static MapRenderEditorPreviewVisionResolution Resolve(
        string? mapIdentity,
        long expectedPoolRevision,
        IMapRenderCanonicalRawFileProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (expectedPoolRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(expectedPoolRevision));

        if (!MapRenderEditorPreviewCreateArtFogResolver
                .TryCreateCanonicalRawFileName(
                    mapIdentity,
                    out string createArtName,
                    out string mapDetail))
        {
            return Failed(
                MapRenderEditorPreviewVisionStatus.InvalidMapIdentity,
                mapDetail);
        }

        if (!provider.HasCanonicalAssetPoolRevision(expectedPoolRevision))
        {
            return Failed(
                MapRenderEditorPreviewVisionStatus
                    .AssetPoolRevisionMismatch,
                $"The active RawFile provider no longer owns pool revision {expectedPoolRevision}.");
        }

        if (!provider.TryResolveCanonicalRawFile(
                createArtName,
                expectedPoolRevision,
                out RawFileAsset? createArt) ||
            createArt is null)
        {
            return Failed(
                MapRenderEditorPreviewVisionStatus.CreateArtRawFileAbsent,
                $"Canonical createart RawFile '{createArtName}' is absent at pool revision {expectedPoolRevision}.");
        }
        if (!provider.HasCanonicalAssetPoolRevision(expectedPoolRevision))
        {
            return Failed(
                MapRenderEditorPreviewVisionStatus
                    .AssetPoolRevisionMismatch,
                $"The active RawFile provider changed while resolving '{createArtName}'.");
        }

        if (!TryDecode(createArt, out string createArtText,
                out string createArtDecodeDetail))
        {
            return Failed(
                MapRenderEditorPreviewVisionStatus.CreateArtRawFileInvalid,
                $"Canonical createart RawFile '{createArtName}' is unusable: {createArtDecodeDetail}");
        }

        if (!TryFindVisionSetNaked(
                createArtText,
                out string visionName,
                out float transitionSeconds,
                out MapRenderEditorPreviewVisionStatus callStatus,
                out string callDetail))
        {
            return Failed(callStatus, callDetail);
        }
        if (transitionSeconds != 0f)
        {
            return Failed(
                MapRenderEditorPreviewVisionStatus
                    .VisionSetTransitionUnsupported,
                $"VisionSetNaked selects '{visionName}' with a nonzero {transitionSeconds.ToString(CultureInfo.InvariantCulture)} second transition; previous-vision/frame-time interpolation state is unavailable.");
        }

        string visionRawFileName = $"vision/{visionName}.vision";
        if (!provider.TryResolveCanonicalRawFile(
                visionRawFileName,
                expectedPoolRevision,
                out RawFileAsset? visionRawFile) ||
            visionRawFile is null)
        {
            return Failed(
                MapRenderEditorPreviewVisionStatus.VisionRawFileAbsent,
                $"Canonical vision RawFile '{visionRawFileName}' is absent at pool revision {expectedPoolRevision}.");
        }
        if (!provider.HasCanonicalAssetPoolRevision(expectedPoolRevision))
        {
            return Failed(
                MapRenderEditorPreviewVisionStatus
                    .AssetPoolRevisionMismatch,
                $"The active RawFile provider changed while resolving '{visionRawFileName}'.");
        }

        if (!TryDecode(
                visionRawFile,
                out string visionText,
                out string visionDecodeDetail))
        {
            return Failed(
                MapRenderEditorPreviewVisionStatus.VisionRawFileInvalid,
                $"Canonical vision RawFile '{visionRawFileName}' is unusable: {visionDecodeDetail}");
        }

        if (!TryParseVision(
                visionName,
                visionText,
                out MapRenderEditorPreviewVisionState? vision,
                out MapRenderEditorPreviewVisionStatus visionStatus,
                out string visionDetail))
        {
            return Failed(
                visionStatus,
                $"Canonical vision RawFile '{visionRawFileName}' is unusable: {visionDetail}");
        }

        return new MapRenderEditorPreviewVisionResolution(
            MapRenderEditorPreviewVisionStatus.Ready,
            vision,
            $"Canonical createart '{createArtName}' selected immediate naked vision '{visionRawFileName}'.");
    }

    private static bool TryDecode(
        RawFileAsset rawFile,
        out string text,
        out string detail)
    {
        bool decoded = MapRenderCanonicalRawFileTextDecoder.TryDecode(
            rawFile,
            MaximumTextByteCount,
            out text,
            out _,
            out detail);
        return decoded;
    }

    private static bool TryFindVisionSetNaked(
        string createArt,
        out string visionName,
        out float transitionSeconds,
        out MapRenderEditorPreviewVisionStatus status,
        out string detail)
    {
        visionName = string.Empty;
        transitionSeconds = 0f;
        status = MapRenderEditorPreviewVisionStatus.VisionSetCallMissing;
        detail = string.Empty;
        if (!TryMaskComments(createArt, out string searchable, out detail))
        {
            status = MapRenderEditorPreviewVisionStatus
                .VisionSetCallMalformed;
            return false;
        }

        var calls = new List<string>();
        for (int index = 0; index < searchable.Length;)
        {
            if (searchable[index] is '\'' or '"')
            {
                if (!TrySkipQuoted(searchable, index, out index))
                {
                    status = MapRenderEditorPreviewVisionStatus
                        .VisionSetCallMalformed;
                    detail = "Createart contains an unterminated quoted string.";
                    return false;
                }
                continue;
            }
            if (!IsIdentifierStart(searchable[index]))
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
                    .Equals("VisionSetNaked", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            int open = index;
            while (open < searchable.Length &&
                   char.IsWhiteSpace(searchable[open]))
            {
                open++;
            }
            if (open >= searchable.Length || searchable[open] != '(' ||
                !TryReadBalancedParentheses(
                    searchable,
                    open,
                    out int close,
                    out string arguments,
                    out detail))
            {
                status = MapRenderEditorPreviewVisionStatus
                    .VisionSetCallMalformed;
                if (detail.Length == 0)
                    detail = "VisionSetNaked is not followed by a valid argument list.";
                return false;
            }

            calls.Add(arguments);
            index = close + 1;
        }

        if (calls.Count == 0)
        {
            detail = "No executable VisionSetNaked call was found.";
            return false;
        }
        if (calls.Count != 1)
        {
            status = MapRenderEditorPreviewVisionStatus
                .VisionSetCallAmbiguous;
            detail =
                $"Expected one executable VisionSetNaked call, found {calls.Count}.";
            return false;
        }

        if (!TrySplitCallArguments(calls[0], out string[] fields) ||
            fields.Length != 2)
        {
            status = MapRenderEditorPreviewVisionStatus
                .VisionSetCallMalformed;
            detail = "VisionSetNaked must have exactly a quoted name and transition duration.";
            return false;
        }

        string quotedName = fields[0].Trim();
        if (quotedName.Length < 2 ||
            quotedName[0] is not ('\'' or '"') ||
            quotedName[^1] != quotedName[0])
        {
            status = MapRenderEditorPreviewVisionStatus
                .VisionSetCallMalformed;
            detail = "VisionSetNaked name must be quoted.";
            return false;
        }
        string selected = quotedName[1..^1].ToLowerInvariant();
        if (selected.Length == 0 || selected.Any(character =>
                !char.IsAsciiLetterOrDigit(character) &&
                character is not ('_' or '-')))
        {
            status = MapRenderEditorPreviewVisionStatus
                .VisionSetCallMalformed;
            detail = "VisionSetNaked name is not a safe canonical vision identity.";
            return false;
        }
        if (!float.TryParse(
                fields[1],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out transitionSeconds) ||
            !float.IsFinite(transitionSeconds) ||
            transitionSeconds < 0f)
        {
            status = MapRenderEditorPreviewVisionStatus
                .VisionSetCallMalformed;
            detail = "VisionSetNaked transition is not a finite nonnegative number.";
            return false;
        }

        visionName = selected;
        return true;
    }

    private static bool TryParseVision(
        string name,
        string text,
        out MapRenderEditorPreviewVisionState? vision,
        out MapRenderEditorPreviewVisionStatus status,
        out string detail)
    {
        vision = null;
        status = MapRenderEditorPreviewVisionStatus.VisionFieldMalformed;
        detail = string.Empty;
        var fields = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            string line = lines[lineIndex].Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
                continue;
            int whitespace = line.IndexOfAny([' ', '\t']);
            if (whitespace <= 0)
            {
                detail = $"Line {lineIndex + 1} has no quoted value.";
                return false;
            }
            string key = line[..whitespace];
            string value = line[whitespace..].Trim();
            if (value.Length < 2 || value[0] != '"' || value[^1] != '"')
            {
                detail = $"Line {lineIndex + 1} value is not exactly quoted.";
                return false;
            }
            if (!fields.TryAdd(key, value[1..^1].Trim()))
            {
                detail = $"Vision field '{key}' is duplicated.";
                return false;
            }
        }

        string[] required =
        [
            "r_glow",
            "r_glowRadius0",
            "r_glowBloomCutoff",
            "r_glowBloomDesaturation",
            "r_glowBloomIntensity0",
            "r_filmEnable",
            "r_filmContrast",
            "r_filmBrightness",
            "r_filmDesaturation",
            "r_filmDesaturationDark",
            "r_filmInvert",
            "r_filmLightTint",
            "r_filmMediumTint",
            "r_filmDarkTint",
            "r_primaryLightUseTweaks",
            "r_primaryLightTweakDiffuseStrength",
            "r_primaryLightTweakSpecularStrength"
        ];
        string[] missing = required.Where(field => !fields.ContainsKey(field))
            .ToArray();
        if (missing.Length != 0)
        {
            status = MapRenderEditorPreviewVisionStatus.VisionFieldMissing;
            detail = $"Required fields are absent: {string.Join(", ", missing)}.";
            return false;
        }

        if (!TryBool(fields, "r_glow", out bool glowEnabled) ||
            !TryFloat(fields, "r_glowRadius0", out float glowRadius) ||
            !TryFloat(fields, "r_glowBloomCutoff", out float glowCutoff) ||
            !TryFloat(fields, "r_glowBloomDesaturation", out float glowDesaturation) ||
            !TryFloat(fields, "r_glowBloomIntensity0", out float glowIntensity) ||
            !TryBool(fields, "r_filmEnable", out bool filmEnabled) ||
            !TryFloat(fields, "r_filmContrast", out float filmContrast) ||
            !TryFloat(fields, "r_filmBrightness", out float filmBrightness) ||
            !TryFloat(fields, "r_filmDesaturation", out float filmDesaturation) ||
            !TryFloat(fields, "r_filmDesaturationDark", out float filmDesaturationDark) ||
            !TryBool(fields, "r_filmInvert", out bool filmInvert) ||
            !TryVector(fields, "r_filmLightTint", out Vector3 filmLightTint) ||
            !TryVector(fields, "r_filmMediumTint", out Vector3 filmMediumTint) ||
            !TryVector(fields, "r_filmDarkTint", out Vector3 filmDarkTint) ||
            !TryBool(fields, "r_primaryLightUseTweaks", out bool useLightTweaks) ||
            !TryFloat(fields, "r_primaryLightTweakDiffuseStrength", out float diffuseStrength) ||
            !TryFloat(fields, "r_primaryLightTweakSpecularStrength", out float specularStrength))
        {
            detail = "One or more required values have the wrong scalar/vector type.";
            return false;
        }

        if (!Between(glowRadius, 0f, MaximumGlowRadius) ||
            !Between(glowCutoff, 0f, 1f) ||
            !Between(glowDesaturation, -1f, 1f) ||
            !Between(glowIntensity, 0f, MaximumStrength) ||
            !Between(filmContrast, 0f, MaximumStrength) ||
            !Between(filmBrightness, -1f, 1f) ||
            !Between(filmDesaturation, -1f, 1f) ||
            !Between(filmDesaturationDark, -1f, 1f) ||
            !TintInRange(filmLightTint) ||
            !TintInRange(filmMediumTint) ||
            !TintInRange(filmDarkTint) ||
            !Between(diffuseStrength, 0f, MaximumStrength) ||
            !Between(specularStrength, 0f, MaximumStrength))
        {
            status = MapRenderEditorPreviewVisionStatus
                .VisionFieldValueOutOfRange;
            detail = "One or more required values are outside the supported renderer domain.";
            return false;
        }

        vision = new MapRenderEditorPreviewVisionState(
            name,
            new MapRenderEditorPreviewPrimaryLightVisionState(
                useLightTweaks,
                diffuseStrength,
                specularStrength),
            new MapRenderEditorPreviewFilmVisionState(
                filmEnabled,
                filmContrast,
                filmBrightness,
                filmDesaturation,
                filmDesaturationDark,
                filmInvert,
                filmLightTint,
                filmMediumTint,
                filmDarkTint),
            new MapRenderEditorPreviewGlowVisionState(
                glowEnabled,
                glowRadius,
                glowCutoff,
                glowDesaturation,
                glowIntensity));
        status = MapRenderEditorPreviewVisionStatus.Ready;
        return true;
    }

    private static bool TryBool(
        IReadOnlyDictionary<string, string> fields,
        string name,
        out bool value)
    {
        value = false;
        if (!fields.TryGetValue(name, out string? text) ||
            !int.TryParse(
                text,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int parsed) ||
            parsed is not (0 or 1))
        {
            return false;
        }
        value = parsed != 0;
        return true;
    }

    private static bool TryFloat(
        IReadOnlyDictionary<string, string> fields,
        string name,
        out float value)
    {
        value = default;
        return fields.TryGetValue(name, out string? text) &&
               float.TryParse(
                   text,
                   NumberStyles.Float,
                   CultureInfo.InvariantCulture,
                   out value) &&
               float.IsFinite(value);
    }

    private static bool TryVector(
        IReadOnlyDictionary<string, string> fields,
        string name,
        out Vector3 value)
    {
        value = default;
        if (!fields.TryGetValue(name, out string? text))
            return false;
        string[] components = text.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries);
        if (components.Length != 3 ||
            !float.TryParse(components[0], NumberStyles.Float,
                CultureInfo.InvariantCulture, out float x) ||
            !float.TryParse(components[1], NumberStyles.Float,
                CultureInfo.InvariantCulture, out float y) ||
            !float.TryParse(components[2], NumberStyles.Float,
                CultureInfo.InvariantCulture, out float z) ||
            !float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z))
        {
            return false;
        }
        value = new Vector3(x, y, z);
        return true;
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
                while (index < result.Length &&
                       result[index] is not ('\r' or '\n'))
                {
                    result[index++] = ' ';
                }
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

    private static bool TryReadBalancedParentheses(
        string text,
        int open,
        out int close,
        out string arguments,
        out string detail)
    {
        close = -1;
        arguments = string.Empty;
        detail = string.Empty;
        int depth = 0;
        for (int index = open; index < text.Length; index++)
        {
            if (text[index] is '\'' or '"')
            {
                if (!TrySkipQuoted(text, index, out int next))
                {
                    detail = "VisionSetNaked contains an unterminated quoted string.";
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
        detail = "VisionSetNaked has an unterminated argument list.";
        return false;
    }

    private static bool TrySplitCallArguments(
        string arguments,
        out string[] fields)
    {
        var result = new List<string>();
        int start = 0;
        for (int index = 0; index < arguments.Length; index++)
        {
            if (arguments[index] is '\'' or '"')
            {
                if (!TrySkipQuoted(arguments, index, out int next))
                {
                    fields = [];
                    return false;
                }
                index = next - 1;
                continue;
            }
            if (arguments[index] == ',')
            {
                result.Add(arguments[start..index].Trim());
                start = index + 1;
            }
        }
        result.Add(arguments[start..].Trim());
        fields = result.ToArray();
        return fields.All(field => field.Length != 0);
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

    private static bool Between(float value, float minimum, float maximum) =>
        float.IsFinite(value) && value >= minimum && value <= maximum;

    private static bool TintInRange(Vector3 tint) =>
        Between(tint.X, 0f, MaximumTint) &&
        Between(tint.Y, 0f, MaximumTint) &&
        Between(tint.Z, 0f, MaximumTint);

    private static MapRenderEditorPreviewVisionResolution Failed(
        MapRenderEditorPreviewVisionStatus status,
        string detail) =>
        new(status, null, detail);
}

using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using IW4.Assets.Assets.Physics;
using MapConverter.Game.IW3.PC.Extraction;

namespace MapConverter.Game.IW3.PC.Physics;

internal sealed record Iw3PhysPresetCompilation(
    IReadOnlyDictionary<string, PhysPresetAsset> ByName);

/// <summary>
/// Converts explicitly requested IW3 PHYSIC info strings into authored IW4
/// physics presets. Proven stock IW3 references are materialized with their
/// IW3 values so conversion does not depend on same-named IW4 providers.
/// </summary>
internal static class Iw3PhysPresetCompiler
{
    private const string ManifestAssetType = "physpreset";
    private const string SourcePrefix = "PHYSIC";
    private const int MaximumSourceBytes = 64 * 1024;

    private static readonly HashSet<string> SourceFieldNames =
    [
        "mass",
        "bounce",
        "friction",
        "isFrictionInfinity",
        "bulletForceScale",
        "explosiveForceScale",
        "sndAliasPrefix",
        "piecesSpreadFraction",
        "piecesUpwardVelocity",
        "tempDefaultToCylinder"
    ];

    internal static Iw3PhysPresetCompilation Compile(
        string assetDirectory,
        Iw3ZoneManifest manifest,
        IReadOnlyList<string> requestedNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetDirectory);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(requestedNames);
        string fullAssetDirectory = Path.GetFullPath(assetDirectory);
        if (!Directory.Exists(fullAssetDirectory))
        {
            throw new DirectoryNotFoundException(
                $"The IW3 asset directory '{fullAssetDirectory}' does not exist.");
        }

        Dictionary<string, bool> declarations = ReadManifestDeclarations(manifest);
        string[] names = ValidateRequestedNames(requestedNames);
        var byName = new Dictionary<string, PhysPresetAsset>(
            names.Length,
            StringComparer.Ordinal);
        foreach (string name in names)
        {
            if (!declarations.TryGetValue(name, out bool isReference))
            {
                throw new InvalidDataException(
                    $"IW3 physics preset '{name}' is requested but is not " +
                    "declared by the zone manifest.");
            }

            PhysPresetAsset asset;
            if (isReference)
            {
                asset = CompileStockReference(name);
            }
            else
            {
                string path = ResolveSourcePath(fullAssetDirectory, name);
                asset = CompileOwned(name, path);
            }
            byName.Add(name, asset);
        }

        return new Iw3PhysPresetCompilation(
            new ReadOnlyDictionary<string, PhysPresetAsset>(byName));
    }

    private static PhysPresetAsset CompileStockReference(string name) => name switch
    {
        // Canonical COD4 SDK definitions. The shipped IW3 PS3 brick preset
        // independently confirms the SDK brick values field-for-field.
        "default" => CreateStockPreset(
            name,
            mass: 10.0f,
            bounce: 0.5f,
            friction: 0.5f,
            bulletForceScale: 0.5f,
            explosiveForceScale: 0.3f,
            sndAliasPrefix: string.Empty),
        "brick" => CreateStockPreset(
            name,
            mass: 5.0f,
            bounce: 0.3f,
            friction: 0.5f,
            bulletForceScale: 0.6f,
            explosiveForceScale: 0.12f,
            sndAliasPrefix: "physics_brick"),
        _ => throw new InvalidDataException(
            $"IW3 stock physics preset '{name}' has no proven donor-free " +
            "definition in MapConverter.")
    };

    private static PhysPresetAsset CreateStockPreset(
        string name,
        float mass,
        float bounce,
        float friction,
        float bulletForceScale,
        float explosiveForceScale,
        string sndAliasPrefix) => new()
    {
        Name = name,
        Type = 0,
        Mass = mass,
        Bounce = bounce,
        Friction = friction,
        BulletForceScale = bulletForceScale,
        ExplosiveForceScale = explosiveForceScale,
        SndAliasPrefix = sndAliasPrefix,
        PiecesSpreadFraction = 0.0f,
        PiecesUpwardVelocity = 0.0f,
        TempDefaultToCylinder = 0,
        PerSurfaceSndAlias = 0,
        Pad2A = 0
    };

    private static Dictionary<string, bool> ReadManifestDeclarations(
        Iw3ZoneManifest manifest)
    {
        var declarations = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (Iw3ZoneManifestEntry entry in manifest.Entries.Where(entry =>
                     string.Equals(
                         entry.AssetType,
                         ManifestAssetType,
                         StringComparison.Ordinal)))
        {
            ValidateAssetName(entry.AssetName, "zone-manifest physics preset");
            if (!declarations.TryAdd(entry.AssetName, entry.IsReference))
            {
                throw new InvalidDataException(
                    $"IW3 zone manifest repeats physics preset '{entry.AssetName}'.");
            }
        }
        return declarations;
    }

    private static string[] ValidateRequestedNames(
        IReadOnlyList<string> requestedNames)
    {
        var names = new List<string>(requestedNames.Count);
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (string? requestedName in requestedNames)
        {
            if (requestedName is null)
            {
                throw new ArgumentException(
                    "An IW3 physics-preset request cannot be null.",
                    nameof(requestedNames));
            }

            ValidateAssetName(requestedName, "requested IW3 physics preset");
            if (!seenNames.Add(requestedName))
            {
                throw new ArgumentException(
                    $"IW3 physics preset '{requestedName}' was requested more than once.",
                    nameof(requestedNames));
            }
            names.Add(requestedName);
        }

        return names.Order(StringComparer.Ordinal).ToArray();
    }

    private static void ValidateAssetName(string name, string description)
    {
        if (string.IsNullOrWhiteSpace(name) ||
            name[0] is ',' or '/' ||
            Path.IsPathRooted(name) ||
            name.Contains('\\') ||
            !string.Equals(name, name.Trim(), StringComparison.Ordinal) ||
            name.Split('/').Any(segment => segment.Length == 0 || segment is "." or "..") ||
            name.Any(character => char.IsControl(character) || character > byte.MaxValue))
        {
            throw new InvalidDataException(
                $"The {description} name '{name}' is not a canonical Latin-1 asset name.");
        }
    }

    private static string ResolveSourcePath(
        string fullAssetDirectory,
        string name)
    {
        string physicsDirectory = Path.GetFullPath(Path.Combine(
            fullAssetDirectory,
            "physic"));
        string path = Path.GetFullPath(Path.Combine(
            physicsDirectory,
            name.Replace('/', Path.DirectorySeparatorChar)));
        string directoryPrefix = physicsDirectory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        StringComparison pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!path.StartsWith(directoryPrefix, pathComparison))
        {
            throw new InvalidDataException(
                $"IW3 physics-preset path '{name}' escapes the physic directory.");
        }
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Extracted IW3 physics preset '{name}' was not found.",
                path);
        }
        return path;
    }

    private static PhysPresetAsset CompileOwned(string name, string path)
    {
        string source = ReadBoundedLatin1(path, name);
        Dictionary<string, string> fields = ParseFields(source, name);
        bool hasInfiniteFriction = ParseBoolean(
            fields["isFrictionInfinity"],
            name,
            "isFrictionInfinity");
        string sndAliasPrefix = fields["sndAliasPrefix"];
        ValidateInfoStringValue(sndAliasPrefix, name, "sndAliasPrefix");

        return new PhysPresetAsset
        {
            Name = name,
            Type = 0,
            Mass = ParseFloat(fields["mass"], name, "mass"),
            Bounce = ParseFloat(fields["bounce"], name, "bounce"),
            Friction = hasInfiniteFriction
                ? float.MaxValue
                : ParseFloat(fields["friction"], name, "friction"),
            BulletForceScale = ParseFloat(
                fields["bulletForceScale"],
                name,
                "bulletForceScale"),
            ExplosiveForceScale = ParseFloat(
                fields["explosiveForceScale"],
                name,
                "explosiveForceScale"),
            SndAliasPrefix = sndAliasPrefix,
            PiecesSpreadFraction = ParseFloat(
                fields["piecesSpreadFraction"],
                name,
                "piecesSpreadFraction"),
            PiecesUpwardVelocity = ParseFloat(
                fields["piecesUpwardVelocity"],
                name,
                "piecesUpwardVelocity"),
            TempDefaultToCylinder = ParseBoolean(
                fields["tempDefaultToCylinder"],
                name,
                "tempDefaultToCylinder") ? (byte)1 : (byte)0,
            PerSurfaceSndAlias = 0,
            Pad2A = 0
        };
    }

    private static string ReadBoundedLatin1(string path, string name)
    {
        long declaredLength = new FileInfo(path).Length;
        if (declaredLength > MaximumSourceBytes)
        {
            throw SourceError(
                name,
                $"source is larger than {MaximumSourceBytes} bytes");
        }

        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length > MaximumSourceBytes)
        {
            throw SourceError(
                name,
                $"source grew beyond {MaximumSourceBytes} bytes while being read");
        }
        return Encoding.Latin1.GetString(bytes);
    }

    private static Dictionary<string, string> ParseFields(
        string source,
        string name)
    {
        string[] tokens = source.Split('\\');
        if (tokens.Length == 0 ||
            !string.Equals(tokens[0], SourcePrefix, StringComparison.Ordinal))
        {
            throw SourceError(name, $"expected '{SourcePrefix}' prefix");
        }
        if (tokens.Length == 1 || tokens.Length % 2 == 0)
        {
            throw SourceError(name, "expected complete backslash-delimited key/value pairs");
        }

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 1; index < tokens.Length; index += 2)
        {
            string key = tokens[index];
            string value = tokens[index + 1];
            if (!SourceFieldNames.Contains(key))
                throw SourceError(name, $"contains unsupported field '{key}'");
            if (!fields.TryAdd(key, value))
                throw SourceError(name, $"repeats field '{key}'");
        }

        string[] missingFields = SourceFieldNames
            .Where(field => !fields.ContainsKey(field))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (missingFields.Length != 0)
        {
            throw SourceError(
                name,
                "is missing fields: " + string.Join(", ", missingFields));
        }
        return fields;
    }

    private static float ParseFloat(
        string value,
        string name,
        string fieldName)
    {
        const NumberStyles styles =
            NumberStyles.AllowLeadingSign |
            NumberStyles.AllowDecimalPoint |
            NumberStyles.AllowExponent;
        if (!float.TryParse(
                value,
                styles,
                CultureInfo.InvariantCulture,
                out float result) ||
            !float.IsFinite(result))
        {
            throw SourceError(
                name,
                $"field '{fieldName}' has invalid invariant float '{value}'");
        }
        return result;
    }

    private static bool ParseBoolean(
        string value,
        string name,
        string fieldName) => value switch
    {
        "0" => false,
        "1" => true,
        _ => throw SourceError(
            name,
            $"field '{fieldName}' must be 0 or 1, found '{value}'")
    };

    private static void ValidateInfoStringValue(
        string value,
        string name,
        string fieldName)
    {
        if (value.Any(character => char.IsControl(character)))
        {
            throw SourceError(
                name,
                $"field '{fieldName}' contains a control character");
        }
    }

    private static InvalidDataException SourceError(
        string name,
        string message) => new(
            $"IW3 physics preset '{name}' {message}.");
}

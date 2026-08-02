using System.Collections.ObjectModel;
using System.Globalization;
using IW4.Studio.MapEditor.Editing.Identity;
using IW4.Studio.MapEditor.Editing.MapEntsSyntax;
using IW4.Studio.MapEditor.Editing.Objects;
using IW4.Studio.MapEditor.Editing.Provenance;

namespace IW4.Studio.MapEditor.Editing.Entities;

/// <summary>
/// Closed taxonomy of MapEnt keys that have a non-authoritative typed view.
/// Exact ordered <see cref="EditorEntity.KeyValues"/> remain serialization
/// authority.
/// </summary>
public enum MapEntityCommonKey
{
    ClassName,
    Origin,
    Angles,
    Angle,
    Model,
    Target,
    TargetName,
    SpawnFlags
}

/// <summary>
/// Resolution state for one common-key view. A duplicate key is deliberately
/// distinct from a malformed value so consumers cannot select an arbitrary
/// occurrence.
/// </summary>
public enum MapEntityCommonValueStatus
{
    Missing,
    Parsed,
    Duplicate,
    Malformed
}

public enum MapEntityCommonValueDiagnosticCode
{
    DuplicateKey,
    EmptyValue,
    InvalidVector3,
    InvalidFiniteScalar,
    InvalidInt32
}

public sealed class MapEntityCommonValueDiagnostic
{
    internal MapEntityCommonValueDiagnostic(
        MapEntityCommonKey key,
        MapEntityCommonValueDiagnosticCode code,
        string message,
        IEnumerable<MapEntPropertyOrdinal> propertyOrdinals)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentNullException.ThrowIfNull(propertyOrdinals);
        Key = key;
        Code = code;
        Message = message;
        PropertyOrdinals = new ReadOnlyCollection<MapEntPropertyOrdinal>(
            propertyOrdinals.ToArray());
    }

    public MapEntityCommonKey Key { get; }
    public MapEntityCommonValueDiagnosticCode Code { get; }
    public string Message { get; }
    public IReadOnlyList<MapEntPropertyOrdinal> PropertyOrdinals { get; }
}

public interface IMapEntityCommonValue
{
    MapEntityCommonKey Key { get; }
    string SerializedKey { get; }
    MapEntityCommonValueStatus Status { get; }
    bool IsPresent { get; }
    bool IsParsed { get; }
    IReadOnlyList<EditorEntityProperty> MatchingProperties { get; }
    MapValueProvenance SourceProvenance { get; }
    MapValueProvenance ProjectionProvenance { get; }
    SourceBindingId? SourceBinding { get; }
    MapEntityCommonValueDiagnostic? Diagnostic { get; }
    string DisplayValue { get; }
}

/// <summary>
/// One fail-closed typed interpretation of an exact MapEnt property. The
/// parsed value is derived and retains the unique source value binding. Raw
/// spelling, ordering, duplicates, and malformed content remain available
/// through <see cref="MatchingProperties"/> and the entity's KeyValues.
/// </summary>
public sealed class MapEntityCommonValue<T> : IMapEntityCommonValue
{
    private readonly IReadOnlyList<EditorEntityProperty> _matchingProperties;

    internal MapEntityCommonValue(
        MapEntityCommonKey key,
        string serializedKey,
        MapEntityCommonValueStatus status,
        IEnumerable<EditorEntityProperty> matchingProperties,
        MapValue<T>? parsedValue,
        MapEntityCommonValueDiagnostic? diagnostic,
        string displayValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serializedKey);
        ArgumentNullException.ThrowIfNull(matchingProperties);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayValue);
        if (!Enum.IsDefined(key))
            throw new ArgumentOutOfRangeException(nameof(key));
        if (!Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status));

        EditorEntityProperty[] matches = matchingProperties.ToArray();
        if (matches.Any(value => value is null))
        {
            throw new ArgumentException(
                "Common-key matches cannot contain null properties.",
                nameof(matchingProperties));
        }
        if (status == MapEntityCommonValueStatus.Parsed &&
            (matches.Length != 1 ||
             parsedValue is null ||
             diagnostic is not null))
        {
            throw new ArgumentException(
                "A parsed common-key value requires one source property, " +
                "one derived value, and no diagnostic.",
                nameof(status));
        }
        if (status == MapEntityCommonValueStatus.Missing &&
            (matches.Length != 0 ||
             parsedValue is not null ||
             diagnostic is not null))
        {
            throw new ArgumentException(
                "A missing common-key value cannot carry source data.",
                nameof(status));
        }
        if ((status is MapEntityCommonValueStatus.Duplicate or
             MapEntityCommonValueStatus.Malformed) &&
            (parsedValue is not null || diagnostic is null))
        {
            throw new ArgumentException(
                "An unresolved common-key value requires a diagnostic and " +
                "cannot expose a parsed value.",
                nameof(status));
        }
        if (status == MapEntityCommonValueStatus.Duplicate &&
            matches.Length < 2)
        {
            throw new ArgumentException(
                "A duplicate common-key value requires at least two " +
                "matching properties.",
                nameof(status));
        }
        if (status == MapEntityCommonValueStatus.Malformed &&
            matches.Length != 1)
        {
            throw new ArgumentException(
                "A malformed common-key value requires exactly one " +
                "matching property.",
                nameof(status));
        }

        Key = key;
        SerializedKey = serializedKey;
        Status = status;
        _matchingProperties =
            new ReadOnlyCollection<EditorEntityProperty>(matches);
        ParsedValue = parsedValue;
        Diagnostic = diagnostic;
        DisplayValue = displayValue;
    }

    public MapEntityCommonKey Key { get; }
    public string SerializedKey { get; }
    public MapEntityCommonValueStatus Status { get; }
    public bool IsPresent => Status != MapEntityCommonValueStatus.Missing;
    public bool IsParsed => Status == MapEntityCommonValueStatus.Parsed;
    public IReadOnlyList<EditorEntityProperty> MatchingProperties =>
        _matchingProperties;
    public MapValue<T>? ParsedValue { get; }
    public MapValueProvenance SourceProvenance =>
        _matchingProperties.Count == 1
            ? _matchingProperties[0].ValueProvenance
            : MapValueProvenance.Unknown;
    public MapValueProvenance ProjectionProvenance =>
        ParsedValue?.Provenance ?? MapValueProvenance.Unknown;
    public SourceBindingId? SourceBinding =>
        _matchingProperties.Count == 1
            ? _matchingProperties[0].ValueSourceBinding
            : null;
    public MapEntityCommonValueDiagnostic? Diagnostic { get; }
    public string DisplayValue { get; }

    public bool TryGetValue(out T value)
    {
        if (ParsedValue is not null)
        {
            value = ParsedValue.Value;
            return true;
        }

        value = default!;
        return false;
    }
}

/// <summary>
/// Read-only semantic convenience layer over exact ordered MapEnt properties.
/// It never rewrites or selects among duplicate properties.
/// </summary>
public sealed class MapEntityCommonKeyProjection
{
    private delegate bool TryParseValue<T>(
        string source,
        out T value,
        out MapEntityCommonValueDiagnosticCode diagnosticCode,
        out string diagnostic);

    private readonly IReadOnlyList<IMapEntityCommonValue> _values;
    private readonly IReadOnlyList<MapEntityCommonValueDiagnostic> _diagnostics;

    private MapEntityCommonKeyProjection(
        IReadOnlyList<EditorEntityProperty> properties)
    {
        ClassName = ParseText(
            properties,
            MapEntityCommonKey.ClassName,
            "classname");
        Origin = Parse<MapVector3>(
            properties,
            MapEntityCommonKey.Origin,
            "origin",
            TryParseVector3);
        Angles = Parse<MapVector3>(
            properties,
            MapEntityCommonKey.Angles,
            "angles",
            TryParseVector3);
        Angle = Parse<float>(
            properties,
            MapEntityCommonKey.Angle,
            "angle",
            TryParseFiniteScalar);
        Model = ParseText(
            properties,
            MapEntityCommonKey.Model,
            "model");
        Target = ParseText(
            properties,
            MapEntityCommonKey.Target,
            "target");
        TargetName = ParseText(
            properties,
            MapEntityCommonKey.TargetName,
            "targetname");
        SpawnFlags = Parse<int>(
            properties,
            MapEntityCommonKey.SpawnFlags,
            "spawnflags",
            TryParseInt32);

        _values = Array.AsReadOnly<IMapEntityCommonValue>(
        [
            ClassName,
            Origin,
            Angles,
            Angle,
            Model,
            Target,
            TargetName,
            SpawnFlags
        ]);
        _diagnostics = Array.AsReadOnly(
            _values
                .Select(value => value.Diagnostic)
                .Where(value => value is not null)
                .Select(value => value!)
                .ToArray());
    }

    public MapEntityCommonValue<string> ClassName { get; }
    public MapEntityCommonValue<MapVector3> Origin { get; }
    public MapEntityCommonValue<MapVector3> Angles { get; }
    public MapEntityCommonValue<float> Angle { get; }
    public MapEntityCommonValue<string> Model { get; }
    public MapEntityCommonValue<string> Target { get; }
    public MapEntityCommonValue<string> TargetName { get; }
    public MapEntityCommonValue<int> SpawnFlags { get; }
    public IReadOnlyList<IMapEntityCommonValue> Values => _values;
    public IReadOnlyList<MapEntityCommonValueDiagnostic> Diagnostics =>
        _diagnostics;
    public bool HasDiagnostics => _diagnostics.Count != 0;

    public IMapEntityCommonValue Get(MapEntityCommonKey key) =>
        key switch
        {
            MapEntityCommonKey.ClassName => ClassName,
            MapEntityCommonKey.Origin => Origin,
            MapEntityCommonKey.Angles => Angles,
            MapEntityCommonKey.Angle => Angle,
            MapEntityCommonKey.Model => Model,
            MapEntityCommonKey.Target => Target,
            MapEntityCommonKey.TargetName => TargetName,
            MapEntityCommonKey.SpawnFlags => SpawnFlags,
            _ => throw new ArgumentOutOfRangeException(nameof(key))
        };

    internal static MapEntityCommonKeyProjection Create(
        IReadOnlyList<EditorEntityProperty> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        if (properties.Any(value => value is null))
        {
            throw new ArgumentException(
                "Common-key projection properties cannot contain null values.",
                nameof(properties));
        }

        return new MapEntityCommonKeyProjection(properties);
    }

    private static MapEntityCommonValue<string> ParseText(
        IReadOnlyList<EditorEntityProperty> properties,
        MapEntityCommonKey key,
        string serializedKey) =>
        Parse<string>(
            properties,
            key,
            serializedKey,
            TryParseNonEmptyText);

    private static MapEntityCommonValue<T> Parse<T>(
        IReadOnlyList<EditorEntityProperty> properties,
        MapEntityCommonKey key,
        string serializedKey,
        TryParseValue<T> parser)
    {
        EditorEntityProperty[] matches = properties
            .Where(property => string.Equals(
                property.Key,
                serializedKey,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length == 0)
        {
            return new MapEntityCommonValue<T>(
                key,
                serializedKey,
                MapEntityCommonValueStatus.Missing,
                [],
                parsedValue: null,
                diagnostic: null,
                displayValue: "(missing)");
        }
        if (matches.Length > 1)
        {
            MapEntPropertyOrdinal[] ordinals = matches
                .Select(value => value.Ordinal)
                .ToArray();
            string ordinalText = string.Join(
                ", ",
                ordinals.Select(value => $"#{value.Value}"));
            var diagnostic = new MapEntityCommonValueDiagnostic(
                key,
                MapEntityCommonValueDiagnosticCode.DuplicateKey,
                $"Common key '{serializedKey}' occurs {matches.Length} " +
                $"times at property ordinals {ordinalText}; no occurrence " +
                "was selected.",
                ordinals);
            return new MapEntityCommonValue<T>(
                key,
                serializedKey,
                MapEntityCommonValueStatus.Duplicate,
                matches,
                parsedValue: null,
                diagnostic,
                displayValue: $"(duplicate: {ordinalText})");
        }

        EditorEntityProperty source = matches[0];
        if (!parser(
                source.Value,
                out T parsed,
                out MapEntityCommonValueDiagnosticCode diagnosticCode,
                out string diagnosticText))
        {
            var diagnostic = new MapEntityCommonValueDiagnostic(
                key,
                diagnosticCode,
                $"Common key '{serializedKey}' at property ordinal " +
                $"#{source.Ordinal.Value} is malformed: {diagnosticText}",
                [source.Ordinal]);
            return new MapEntityCommonValue<T>(
                key,
                serializedKey,
                MapEntityCommonValueStatus.Malformed,
                matches,
                parsedValue: null,
                diagnostic,
                displayValue: $"(malformed: {diagnosticText})");
        }

        var projected = new MapValue<T>(
            parsed,
            MapValueProvenance.Derived,
            source.ValueSourceBinding);
        return new MapEntityCommonValue<T>(
            key,
            serializedKey,
            MapEntityCommonValueStatus.Parsed,
            matches,
            projected,
            diagnostic: null,
            displayValue: Format(parsed));
    }

    private static bool TryParseNonEmptyText(
        string source,
        out string value,
        out MapEntityCommonValueDiagnosticCode diagnosticCode,
        out string diagnostic)
    {
        value = source;
        if (!string.IsNullOrWhiteSpace(source))
        {
            diagnosticCode = default;
            diagnostic = string.Empty;
            return true;
        }

        diagnosticCode = MapEntityCommonValueDiagnosticCode.EmptyValue;
        diagnostic = "the value is empty or whitespace";
        return false;
    }

    private static bool TryParseVector3(
        string source,
        out MapVector3 value,
        out MapEntityCommonValueDiagnosticCode diagnosticCode,
        out string diagnostic)
    {
        string[] components = source.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries);
        if (components.Length == 3 &&
            TryParseFiniteFloat(components[0], out float x) &&
            TryParseFiniteFloat(components[1], out float y) &&
            TryParseFiniteFloat(components[2], out float z))
        {
            value = new MapVector3(x, y, z);
            diagnosticCode = default;
            diagnostic = string.Empty;
            return true;
        }

        value = default;
        diagnosticCode = MapEntityCommonValueDiagnosticCode.InvalidVector3;
        diagnostic =
            "expected exactly three finite invariant-culture numbers";
        return false;
    }

    private static bool TryParseFiniteScalar(
        string source,
        out float value,
        out MapEntityCommonValueDiagnosticCode diagnosticCode,
        out string diagnostic)
    {
        if (TryParseFiniteFloat(source, out value))
        {
            diagnosticCode = default;
            diagnostic = string.Empty;
            return true;
        }

        diagnosticCode =
            MapEntityCommonValueDiagnosticCode.InvalidFiniteScalar;
        diagnostic = "expected one finite invariant-culture number";
        return false;
    }

    private static bool TryParseInt32(
        string source,
        out int value,
        out MapEntityCommonValueDiagnosticCode diagnosticCode,
        out string diagnostic)
    {
        if (int.TryParse(
                source,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value))
        {
            diagnosticCode = default;
            diagnostic = string.Empty;
            return true;
        }

        diagnosticCode = MapEntityCommonValueDiagnosticCode.InvalidInt32;
        diagnostic = "expected one invariant-culture 32-bit integer";
        return false;
    }

    private static bool TryParseFiniteFloat(
        string source,
        out float value) =>
        float.TryParse(
            source,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value) &&
        float.IsFinite(value);

    private static string Format<T>(T value) =>
        value switch
        {
            float scalar =>
                scalar.ToString("R", CultureInfo.InvariantCulture),
            MapVector3 vector => FormattableString.Invariant(
                $"{vector.X:R} {vector.Y:R} {vector.Z:R}"),
            IFormattable formattable =>
                formattable.ToString(
                    format: null,
                    CultureInfo.InvariantCulture),
            _ => value is null
                ? string.Empty
                : value.ToString() ?? string.Empty
        };
}

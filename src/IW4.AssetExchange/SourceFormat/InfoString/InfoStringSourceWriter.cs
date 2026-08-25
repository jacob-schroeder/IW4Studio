using System.Globalization;
using IW4.FastFiles.Strings;

namespace IW4.AssetExchange.SourceFormat.InfoString;

/// <summary>
/// Builds IW4 developer info strings while retaining the first insertion
/// position of duplicate keys, matching the engine/OAT info-string contract.
/// </summary>
internal sealed class InfoStringSourceWriter(string prefix)
{
    private readonly List<Field> _fields = [];
    private readonly Dictionary<string, int> _fieldIndices =
        new(StringComparer.Ordinal);

    public void AddString(string key, string? value) =>
        Set(key, value ?? string.Empty);

    public void AddInt(string key, int value) =>
        Set(key, value.ToString(CultureInfo.InvariantCulture));

    public void AddInt(string key, uint value, string field)
    {
        if (value > int.MaxValue)
        {
            throw new InvalidDataException(
                $"{field} value {value} cannot be represented by the signed IW4 source field '{key}'.");
        }

        AddInt(key, checked((int)value));
    }

    public void AddBoolean(string key, bool value) =>
        Set(key, value ? "1" : "0");

    public void AddBoolean(string key, int value) =>
        AddBoolean(key, value != 0);

    public void AddBoolean(string key, byte value) =>
        AddBoolean(key, value != 0);

    public void AddFloat(string key, float value) =>
        Set(key, FormatFloat(value, key));

    public void AddMilesPerHour(string key, float inchesPerSecond)
    {
        RequireFinite(inchesPerSecond, key);
        float milesPerHour = inchesPerSecond / 17.6f;
        RequireFinite(milesPerHour, key);
        Set(key, milesPerHour.ToString("F6", CultureInfo.InvariantCulture));
    }

    public void AddMilliseconds(string key, int milliseconds)
    {
        if (milliseconds < 0)
        {
            throw new InvalidDataException(
                $"Info-string field '{key}' has a negative millisecond value.");
        }

        AddFloat(key, milliseconds / 1000.0f);
    }

    public void AddMilliseconds(string key, float milliseconds)
    {
        RequireFinite(milliseconds, key);
        if (milliseconds < 0.0f ||
            (double)milliseconds > uint.MaxValue ||
            milliseconds != MathF.Truncate(milliseconds))
        {
            throw new InvalidDataException(
                $"Info-string field '{key}' has a millisecond value that cannot be represented by the IW4 source field.");
        }

        AddFloat(key, milliseconds / 1000.0f);
    }

    public void AddIntegerAsFloat(string key, int value, string field)
    {
        float sourceValue = value;
        if ((double)sourceValue != value)
        {
            throw new InvalidDataException(
                $"{field} value {value} cannot be represented exactly by the floating-point IW4 source field '{key}'.");
        }

        AddFloat(key, sourceValue);
    }

    public void AddEnum(
        string key,
        int value,
        IReadOnlyList<string> names,
        string field)
    {
        ArgumentNullException.ThrowIfNull(names);
        if ((uint)value >= (uint)names.Count)
        {
            throw new InvalidDataException(
                $"{field} value {value} has no IW4 source-format name.");
        }

        Set(key, names[value]);
    }

    public void Write(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(prefix);
        foreach (Field field in _fields)
        {
            writer.Write('\\');
            writer.Write(field.Key);
            writer.Write('\\');
            writer.Write(field.Value);
        }
    }

    public static string MaterializedString(
        int pointerRaw,
        string? value,
        string field)
    {
        if (value is not null)
            return value;
        if (pointerRaw != 0)
        {
            throw new InvalidDataException(
                $"{field} is referenced by the PS3 asset but was not materialized.");
        }

        return string.Empty;
    }

    public static string ReferencedAssetName(
        int pointerRaw,
        string? name,
        string field)
    {
        if (name is null)
        {
            if (pointerRaw != 0)
            {
                throw new InvalidDataException(
                    $"{field} is referenced by the PS3 asset but was not materialized.");
            }

            return string.Empty;
        }

        string normalized = SourceOutput.NormalizeReferencedAssetName(name, field);
        if (normalized.Contains('\\'))
        {
            throw new InvalidDataException(
                $"{field} contains the IW4 info-string delimiter.");
        }

        return normalized;
    }

    public static string ScriptStringText(
        ScriptStringReference reference,
        string field)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (reference.Text is not null)
            return reference.Text;
        if (reference.RawLocalIndex != 0)
        {
            throw new InvalidDataException(
                $"{field} script string {reference.RawLocalIndex} was not materialized.");
        }

        return string.Empty;
    }

    public static void RequireFixedPayload(
        int pointerRaw,
        int actualCount,
        int expectedCount,
        string field)
    {
        if (actualCount == expectedCount ||
            pointerRaw == 0 && actualCount == 0)
        {
            return;
        }

        throw new InvalidDataException(
            $"{field} requires {expectedCount} materialized values but has {actualCount}.");
    }

    private void Set(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        if (key.Contains('\\') || value.Contains('\\'))
        {
            throw new InvalidDataException(
                $"Info-string field '{key}' contains the IW4 field delimiter.");
        }

        if (_fieldIndices.TryGetValue(key, out int index))
        {
            _fields[index] = new Field(key, value);
            return;
        }

        _fieldIndices.Add(key, _fields.Count);
        _fields.Add(new Field(key, value));
    }

    private static string FormatFloat(float value, string field)
    {
        RequireFinite(value, field);
        return value.ToString("G6", CultureInfo.InvariantCulture);
    }

    private static void RequireFinite(float value, string field)
    {
        if (!float.IsFinite(value))
        {
            throw new InvalidDataException(
                $"{field} has a non-finite source value.");
        }
    }

    private sealed record Field(string Key, string Value);
}

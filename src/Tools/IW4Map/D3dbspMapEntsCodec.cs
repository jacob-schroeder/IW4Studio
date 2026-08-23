using System.Globalization;
using System.Text;
using IW4.Assets.Assets.MapEnts;
using IW4.Assets.Math;

namespace IW4Map;

internal static class D3dbspMapEntsCodec
{
    private static readonly HashSet<string> PurgeableClassnames = new(
        [
            "misc_model",
            "misc_prefab",
            "dyn_brushmodel",
            "dyn_model",
            "reflection_probe",
            "info_null",
            "func_group",
            "glass"
        ],
        StringComparer.OrdinalIgnoreCase);

    public static byte[] DecodeEntityString(ReadOnlySpan<byte> source)
    {
        IReadOnlyList<ParsedEntity> entities = ParseEntities(source);
        using var output = new MemoryStream(source.Length);
        foreach (ParsedEntity entity in entities)
        {
            if (!CanPurge(entity))
                output.Write(source[entity.Begin..entity.End]);
        }

        output.WriteByte(0);
        return output.ToArray();
    }

    public static IReadOnlyList<Stage> DecodeStages(ReadOnlySpan<byte> source)
    {
        IReadOnlyList<ParsedEntity> entities = ParseEntities(source);
        var stages = new List<Stage>();
        foreach (ParsedEntity entity in entities)
        {
            if (!IsRuntimeStage(entity))
                continue;

            stages.Add(new Stage
            {
                StageName = entity.GetRequiredValue("name"),
                Origin = ParseVec3(entity.GetRequiredValue("origin"), "stage origin"),
                TriggerIndex = ParseUInt16(
                    entity.GetRequiredValue("script_index"),
                    "stage script_index"),
                SunPrimaryLightIndex = ParseByte(
                    entity.GetRequiredValue("sunPrimaryLightIndex"),
                    "stage sunPrimaryLightIndex"),
                Pad13 = 0
            });
        }

        if (stages.Count > byte.MaxValue)
        {
            throw new InvalidDataException(
                $"The entity lump defines {stages.Count} runtime stages; IW4 MapEnts supports at most {byte.MaxValue}.");
        }

        return stages.AsReadOnly();
    }

    private static bool CanPurge(ParsedEntity entity)
    {
        string classname = entity.TryGetValue("classname", out string value)
            ? value
            : string.Empty;
        return PurgeableClassnames.Contains(classname) ||
            (string.Equals(classname, "light", StringComparison.OrdinalIgnoreCase) &&
                !entity.ContainsKey("pl#")) ||
            IsRuntimeStage(entity);
    }

    private static bool IsRuntimeStage(ParsedEntity entity) =>
        entity.TryGetValue("classname", out string classname) &&
        string.Equals(classname, "stage", StringComparison.OrdinalIgnoreCase) &&
        entity.ContainsKey("name") &&
        entity.ContainsKey("origin") &&
        entity.ContainsKey("script_index") &&
        entity.ContainsKey("sunPrimaryLightIndex");

    private static IReadOnlyList<ParsedEntity> ParseEntities(ReadOnlySpan<byte> source)
    {
        int terminator = source.IndexOf((byte)0);
        int length = terminator < 0 ? source.Length : terminator;
        if (terminator >= 0 && source[(terminator + 1)..].IndexOfAnyExcept((byte)0) >= 0)
            throw new InvalidDataException("The entity lump contains data after its first NUL terminator.");

        var parser = new EntityParser(source[..length]);
        var entities = new List<ParsedEntity>();
        while (parser.ParseEntity() is { } entity)
            entities.Add(entity);
        return entities.AsReadOnly();
    }

    private static Vec3 ParseVec3(string value, string description)
    {
        string[] components = value.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (components.Length != 3)
        {
            throw new InvalidDataException(
                $"The {description} value '{value}' does not contain three components.");
        }

        return new Vec3
        {
            X = ParseSingle(components[0], description),
            Y = ParseSingle(components[1], description),
            Z = ParseSingle(components[2], description)
        };
    }

    private static float ParseSingle(string value, string description)
    {
        if (!float.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float result) ||
            !float.IsFinite(result))
        {
            throw new InvalidDataException($"The {description} value '{value}' is not a finite float.");
        }

        return result;
    }

    private static ushort ParseUInt16(string value, string description)
    {
        if (!ushort.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort result))
            throw new InvalidDataException($"The {description} value '{value}' is not a ushort.");
        return result;
    }

    private static byte ParseByte(string value, string description)
    {
        if (!byte.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte result))
            throw new InvalidDataException($"The {description} value '{value}' is not a byte.");
        return result;
    }

    private sealed class ParsedEntity
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

        public ParsedEntity(int begin)
        {
            Begin = begin;
        }

        public int Begin { get; }
        public int End { get; set; }

        public void Add(string key, string value) => _values.TryAdd(key, value);

        public bool ContainsKey(string key) => _values.ContainsKey(key);

        public bool TryGetValue(string key, out string value)
        {
            if (_values.TryGetValue(key, out string? found))
            {
                value = found;
                return true;
            }

            value = string.Empty;
            return false;
        }

        public string GetRequiredValue(string key) =>
            TryGetValue(key, out string value)
                ? value
                : throw new InvalidDataException($"A runtime stage entity has no '{key}' value.");
    }

    private ref struct EntityParser
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _position;

        public EntityParser(ReadOnlySpan<byte> data)
        {
            _data = data;
            _position = 0;
        }

        public ParsedEntity? ParseEntity()
        {
            int begin = _position;
            string? token = ReadToken();
            if (token is null)
                return null;
            if (token != "{")
                throw new InvalidDataException($"Expected '{{' at entity byte {_position}, found '{token}'.");

            var entity = new ParsedEntity(begin);
            while (true)
            {
                string key = ReadToken() ??
                    throw new InvalidDataException("The entity lump ended before a closing brace.");
                if (key == "}")
                {
                    entity.End = _position;
                    return entity;
                }

                string value = ReadToken() ??
                    throw new InvalidDataException($"The entity key '{key}' has no value.");
                if (value == "}")
                    throw new InvalidDataException($"The entity key '{key}' has no value.");
                entity.Add(key, value);
            }
        }

        private string? ReadToken()
        {
            SkipTrivia();
            if (_position >= _data.Length)
                return null;

            byte first = _data[_position++];
            if (first is (byte)'{' or (byte)'}')
                return ((char)first).ToString();
            if (first == (byte)'"')
                return ReadQuotedToken();

            int begin = _position - 1;
            while (_position < _data.Length &&
                   _data[_position] > 32 &&
                   _data[_position] is not (byte)'{' and not (byte)'}')
            {
                _position++;
            }

            return Encoding.Latin1.GetString(_data[begin.._position]);
        }

        private string ReadQuotedToken()
        {
            var token = new StringBuilder();
            while (_position < _data.Length)
            {
                byte value = _data[_position++];
                if (value == (byte)'"')
                    return token.ToString();
                if (value == (byte)'\\' &&
                    _position < _data.Length &&
                    _data[_position] is (byte)'"' or (byte)'\\')
                {
                    value = _data[_position++];
                }

                token.Append((char)value);
            }

            throw new InvalidDataException("The entity lump ended inside a quoted token.");
        }

        private void SkipTrivia()
        {
            while (true)
            {
                while (_position < _data.Length && _data[_position] <= 32)
                    _position++;
                if (_position + 1 >= _data.Length || _data[_position] != (byte)'/')
                    return;

                if (_data[_position + 1] == (byte)'/')
                {
                    _position += 2;
                    while (_position < _data.Length && _data[_position] != (byte)'\n')
                        _position++;
                    continue;
                }
                if (_data[_position + 1] != (byte)'*')
                    return;

                _position += 2;
                while (_position + 1 < _data.Length &&
                       (_data[_position] != (byte)'*' || _data[_position + 1] != (byte)'/'))
                {
                    _position++;
                }
                if (_position + 1 >= _data.Length)
                    throw new InvalidDataException("The entity lump ended inside a block comment.");
                _position += 2;
            }
        }
    }
}

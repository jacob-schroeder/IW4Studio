using System.Text;
using System.Globalization;
using IW4.Formats.D3dbsp;
using IW4.Game.Assets.MapEnts;
using IW4.Game.Math;
using IW4.Game.Pointers;
using IW4.Game.Zone;

namespace IW4.Studio.Documents;

public sealed record AddonMapEntsEntityRow(
    int Ordinal,
    IReadOnlyList<KeyValuePair<string, string>> Properties);

/// <summary>Detached native entity bytes and trigger geometry for one add-on map.</summary>
public sealed class AddonMapEntsDraft
{
    private readonly int _offset;
    private readonly XRuntimeAddress? _runtimeAddress;
    private readonly XPointer<string> _namePointer;
    private readonly XPointer<byte[]> _entityPointer;
    private readonly int _declaredEntityChars;
    private readonly string? _originalEntityString;
    private byte[] _bytes;
    private MapTriggers _trigger;
    private List<ParsedEntity>? _parsed;
    private IReadOnlyList<AddonMapEntsEntityRow>? _rows;

    internal AddonMapEntsDraft(AddonMapEntsAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        Name = asset.Name;
        _offset = asset.Offset;
        _runtimeAddress = asset.RuntimeAddress;
        _namePointer = asset.NamePointer;
        _entityPointer = asset.EntityStringPointer;
        _declaredEntityChars = asset.NumEntityChars;
        _originalEntityString = asset.EntityString;
        _bytes = asset.EntityStringBytes.ToArray();
        _trigger = Copy(asset.Trigger);
        try
        {
            if (_declaredEntityChars != _bytes.Length)
                throw new InvalidDataException("The add-on entity byte count does not match its payload.");
            _parsed = Parse(_bytes);
        }
        catch (InvalidDataException error)
        {
            ParseError = error.Message;
        }
    }

    private AddonMapEntsDraft(AddonMapEntsDraft source)
    {
        Name = source.Name;
        _offset = source._offset;
        _runtimeAddress = source._runtimeAddress;
        _namePointer = source._namePointer;
        _entityPointer = source._entityPointer;
        _declaredEntityChars = source._declaredEntityChars;
        _originalEntityString = source._originalEntityString;
        _bytes = source._bytes.ToArray();
        _trigger = Copy(source._trigger);
        ParseError = source.ParseError;
        _parsed = source._parsed;
        _rows = source._rows;
    }

    public string? Name { get; }
    public string? ParseError { get; }
    public MapTriggers Trigger => Copy(_trigger);
    public IReadOnlyList<AddonMapEntsEntityRow> Rows => ParseError is not null ? [] :
        (_rows ??= Array.AsReadOnly(ParseCurrent().Select((row, index) => new AddonMapEntsEntityRow(
            index,
            Array.AsReadOnly(row.Properties.Select(property =>
                new KeyValuePair<string, string>(property.Key, property.Value)).ToArray()))).ToArray()));

    public AddonMapEntsDraft Clone() => new(this);

    public void SetProperty(int entityIndex, int propertyIndex, string key, string value)
    {
        RequireParseable();
        ParsedEntity entity = RequireEntity(entityIndex);
        ParsedProperty property = RequireProperty(entity, propertyIndex);
        if (property.Key == key && property.Value == value)
            return;
        byte[]? encodedKey = property.Key == key ? null : EncodeToken(key);
        byte[]? encodedValue = property.Value == value ? null : EncodeToken(value);
        // Replace from right to left so both native token offsets stay valid.
        if (encodedValue is not null)
            Replace(property.ValueBegin, property.ValueEnd, encodedValue);
        if (encodedKey is not null)
            Replace(property.KeyBegin, property.KeyEnd, encodedKey);
    }

    public void AddProperty(int entityIndex, string key, string value)
    {
        RequireParseable();
        byte[] pair = EncodePair(key, value);
        ParsedEntity entity = RequireEntity(entityIndex);
        Insert(entity.End - 1, pair);
    }

    public void RemoveProperty(int entityIndex, int propertyIndex)
    {
        RequireParseable();
        ParsedEntity entity = RequireEntity(entityIndex);
        ParsedProperty property = RequireProperty(entity, propertyIndex);
        Replace(property.KeyBegin, property.ValueEnd, []);
    }

    public int CreateEntity(IEnumerable<KeyValuePair<string, string>> properties)
    {
        RequireParseable();
        ArgumentNullException.ThrowIfNull(properties);
        KeyValuePair<string, string>[] pairs = properties.ToArray();
        byte[] block = EncodeEntity(pairs);
        List<ParsedEntity> rows = ParseCurrent();
        int insertAt = rows.Count == 0 ? 0 : rows[^1].End;
        Insert(insertAt, block);
        if (_bytes.AsSpan().IndexOf((byte)0) < 0)
            Insert(_bytes.Length, [0]);
        return rows.Count;
    }

    public int DuplicateEntity(int entityIndex)
    {
        RequireParseable();
        ParsedEntity entity = RequireEntity(entityIndex);
        byte[] block = new byte[entity.End - entity.Begin + 1];
        block[0] = (byte)'\n';
        _bytes.AsSpan(entity.Begin, entity.End - entity.Begin).CopyTo(block.AsSpan(1));
        Insert(entity.End, block);
        return entityIndex + 1;
    }

    public void DeleteEntity(int entityIndex)
    {
        RequireParseable();
        ParsedEntity entity = RequireEntity(entityIndex);
        Replace(entity.Begin, entity.End, []);
    }

    public bool CanResizeTrigger(int modelIndex)
    {
        if (ParseError is not null) return false;
        if ((uint)modelIndex >= (uint)_trigger.Models.Count)
            return false;
        TriggerModel model = _trigger.Models[modelIndex];
        if (model.HullCount != 1 || model.FirstHull >= _trigger.Hulls.Count)
            return false;
        TriggerHull hull = _trigger.Hulls[model.FirstHull];
        if (hull.SlabCount != 0)
            return false;
        if (_trigger.Models.Where((_, index) => index != modelIndex).Any(other =>
                model.FirstHull >= other.FirstHull &&
                model.FirstHull < other.FirstHull + other.HullCount))
            return false;
        var references = ParseCurrent()
            .SelectMany(row => row.Properties.Select(property => (row, property)))
            .Where(pair => pair.property.Key.Equals("model", StringComparison.OrdinalIgnoreCase) &&
                pair.property.Value.Length > 1 && pair.property.Value[0] == '?' &&
                int.TryParse(pair.property.Value.AsSpan(1), NumberStyles.None,
                    CultureInfo.InvariantCulture, out int reference) && reference == modelIndex)
            .ToArray();
        if (references.Length != 1) return false;
        return references[0].row.Properties.Any(property =>
            property.Key.Equals("classname", StringComparison.OrdinalIgnoreCase) &&
            (property.Value.StartsWith("trigger_", StringComparison.OrdinalIgnoreCase) ||
             property.Value.Equals("info_volume", StringComparison.OrdinalIgnoreCase) ||
             property.Value.Equals("stage", StringComparison.OrdinalIgnoreCase)));
    }

    public void ResizeTrigger(int modelIndex, Vec3 midPoint, Vec3 halfSize)
    {
        if (!CanResizeTrigger(modelIndex))
            throw new InvalidOperationException("This trigger is not a single unshared box hull.");
        if (!Finite(midPoint) || !Finite(halfSize) ||
            halfSize.X <= 0 || halfSize.Y <= 0 || halfSize.Z <= 0)
            throw new ArgumentOutOfRangeException(nameof(halfSize), "Trigger bounds must be finite with positive half sizes.");
        TriggerHull[] hulls = _trigger.Hulls.Select(Copy).ToArray();
        int hullIndex = _trigger.Models[modelIndex].FirstHull;
        TriggerHull old = hulls[hullIndex];
        hulls[hullIndex] = new TriggerHull
        {
            Bounds = new Bounds { MidPoint = midPoint, HalfSize = halfSize },
            Contents = old.Contents,
            FirstSlab = old.FirstSlab,
            SlabCount = old.SlabCount
        };
        _trigger = new MapTriggers
        {
            Count = _trigger.Count, ModelsPointer = _trigger.ModelsPointer, Models = _trigger.Models,
            HullCount = _trigger.HullCount, HullsPointer = _trigger.HullsPointer, Hulls = hulls,
            SlabCount = _trigger.SlabCount, SlabsPointer = _trigger.SlabsPointer, Slabs = _trigger.Slabs
        };
    }

    public bool Matches(AddonMapEntsDraft other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Name == other.Name && _bytes.AsSpan().SequenceEqual(other._bytes) &&
            Same(_trigger, other._trigger);
    }

    public void ApplyEdits(AddonMapEntsDraft before, AddonMapEntsDraft after)
    {
        RequireParseable();
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        if (!Matches(before))
            throw new InvalidOperationException("These add-on entities changed since editing began; refresh before applying.");
        if (Name != after.Name)
            throw new InvalidOperationException("An add-on entity edit cannot change asset identity.");
        after.RequireParseable();
        byte[] replacement = after._bytes.ToArray();
        MapTriggers triggers = Copy(after._trigger);
        List<ParsedEntity> parsed = Parse(replacement);
        _bytes = replacement;
        _trigger = triggers;
        _parsed = parsed;
        _rows = null;
    }

    internal AddonMapEntsAsset ToAsset() => new()
    {
        Offset = _offset, RuntimeAddress = _runtimeAddress,
        Name = Name, NamePointer = _namePointer,
        EntityStringPointer = _entityPointer,
        EntityStringBytes = Array.AsReadOnly(_bytes.ToArray()),
        EntityString = ParseError is not null ? _originalEntityString :
            _bytes.Length == 0 ? null : Encoding.Latin1.GetString(_bytes).TrimEnd('\0'),
        NumEntityChars = ParseError is null ? _bytes.Length : _declaredEntityChars,
        Trigger = Copy(_trigger)
    };

    private ParsedEntity RequireEntity(int index)
    {
        List<ParsedEntity> rows = ParseCurrent();
        if ((uint)index >= (uint)rows.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        return rows[index];
    }

    private void RequireParseable()
    {
        if (ParseError is not null)
            throw new InvalidOperationException($"This add-on entity payload cannot be edited: {ParseError}");
    }

    private static ParsedProperty RequireProperty(ParsedEntity entity, int index)
    {
        if ((uint)index >= (uint)entity.Properties.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        return entity.Properties[index];
    }

    private void Insert(int at, byte[] bytes) => Replace(at, at, bytes);

    private List<ParsedEntity> ParseCurrent() => _parsed ??= Parse(_bytes);

    private void Replace(int begin, int end, byte[] replacement)
    {
        byte[] next = new byte[checked(_bytes.Length - (end - begin) + replacement.Length)];
        _bytes.AsSpan(0, begin).CopyTo(next);
        replacement.AsSpan().CopyTo(next.AsSpan(begin));
        _bytes.AsSpan(end).CopyTo(next.AsSpan(begin + replacement.Length));
        _bytes = next;
        _parsed = null;
        _rows = null;
    }

    private static byte[] EncodeEntity(IEnumerable<KeyValuePair<string, string>> properties)
    {
        using var output = new MemoryStream();
        output.Write("\n{"u8);
        foreach (KeyValuePair<string, string> property in properties)
            output.Write(EncodePair(property.Key, property.Value));
        output.Write("\n}"u8);
        return output.ToArray();
    }

    private static byte[] EncodePair(string key, string value)
    {
        byte[] encodedKey = EncodeToken(key);
        byte[] encodedValue = EncodeToken(value);
        using var output = new MemoryStream();
        output.WriteByte((byte)'\n');
        output.Write(encodedKey);
        output.WriteByte((byte)' ');
        output.Write(encodedValue);
        return output.ToArray();
    }

    private static byte[] EncodeToken(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Any(character => character is '\0' or '\r' or '\n' || character > 255))
            throw new ArgumentException("Entity fields must be single-line Latin-1 text.", nameof(text));
        using var output = new MemoryStream();
        output.WriteByte((byte)'"');
        foreach (char character in text)
        {
            if (character is '"' or '\\') output.WriteByte((byte)'\\');
            output.WriteByte((byte)character);
        }
        output.WriteByte((byte)'"');
        return output.ToArray();
    }

    private static bool Finite(Vec3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static MapTriggers Copy(MapTriggers source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new MapTriggers
        {
            Count = source.Count, ModelsPointer = source.ModelsPointer,
            Models = Array.AsReadOnly(source.Models.Select(model => new TriggerModel
            { Contents = model.Contents, FirstHull = model.FirstHull, HullCount = model.HullCount }).ToArray()),
            HullCount = source.HullCount, HullsPointer = source.HullsPointer,
            Hulls = Array.AsReadOnly(source.Hulls.Select(Copy).ToArray()),
            SlabCount = source.SlabCount, SlabsPointer = source.SlabsPointer,
            Slabs = Array.AsReadOnly(source.Slabs.Select(slab => new TriggerSlab
            { Dir = slab.Dir, MidPoint = slab.MidPoint, HalfSize = slab.HalfSize }).ToArray())
        };
    }

    private static TriggerHull Copy(TriggerHull hull) => new()
    {
        Bounds = new Bounds { MidPoint = hull.Bounds.MidPoint, HalfSize = hull.Bounds.HalfSize },
        Contents = hull.Contents, FirstSlab = hull.FirstSlab, SlabCount = hull.SlabCount
    };

    private static bool Same(MapTriggers left, MapTriggers right) =>
        left.Count == right.Count && left.HullCount == right.HullCount && left.SlabCount == right.SlabCount &&
        left.Models.Count == right.Models.Count && left.Hulls.Count == right.Hulls.Count && left.Slabs.Count == right.Slabs.Count &&
        left.Models.Zip(right.Models).All(pair => pair.First.Contents == pair.Second.Contents &&
            pair.First.FirstHull == pair.Second.FirstHull && pair.First.HullCount == pair.Second.HullCount) &&
        left.Hulls.Zip(right.Hulls).All(pair => pair.First.Contents == pair.Second.Contents &&
            pair.First.FirstSlab == pair.Second.FirstSlab && pair.First.SlabCount == pair.Second.SlabCount &&
            Same(pair.First.Bounds.MidPoint, pair.Second.Bounds.MidPoint) && Same(pair.First.Bounds.HalfSize, pair.Second.Bounds.HalfSize)) &&
        left.Slabs.Zip(right.Slabs).All(pair => Same(pair.First.Dir, pair.Second.Dir) &&
            Bits(pair.First.MidPoint, pair.Second.MidPoint) && Bits(pair.First.HalfSize, pair.Second.HalfSize));

    private static bool Same(Vec3 left, Vec3 right) =>
        Bits(left.X, right.X) && Bits(left.Y, right.Y) && Bits(left.Z, right.Z);
    private static bool Bits(float left, float right) =>
        BitConverter.SingleToInt32Bits(left) == BitConverter.SingleToInt32Bits(right);

    private sealed record ParsedEntity(int Begin, int End, List<ParsedProperty> Properties);
    private sealed record ParsedProperty(
        string Key, string Value, int KeyBegin, int KeyEnd, int ValueBegin, int ValueEnd);

    private static List<ParsedEntity> Parse(ReadOnlySpan<byte> bytes)
    {
        int nul = bytes.IndexOf((byte)0);
        var parser = new EntityParser(nul < 0 ? bytes : bytes[..nul]);
        var rows = new List<ParsedEntity>();
        while (parser.NextEntity() is { } row) rows.Add(row);
        return rows;
    }

    private ref struct EntityParser
    {
        private EntityTokenReader _reader;
        public EntityParser(ReadOnlySpan<byte> bytes) => _reader = new EntityTokenReader(bytes);

        public ParsedEntity? NextEntity()
        {
            EntityToken? opening = _reader.ReadToken();
            if (opening is null) return null;
            if (opening.Value.Text != "{" || opening.Value.IsQuoted)
                throw new InvalidDataException($"Expected entity opening brace at byte {opening.Value.Begin}.");
            var properties = new List<ParsedProperty>();
            while (true)
            {
                EntityToken key = _reader.ReadToken() ??
                    throw new InvalidDataException("Entity ended before its closing brace.");
                if (key.Text == "}" && !key.IsQuoted)
                    return new ParsedEntity(opening.Value.Begin, key.End, properties);
                EntityToken value = _reader.ReadToken() ??
                    throw new InvalidDataException("Entity property has no value.");
                if (value.Text == "}" && !value.IsQuoted)
                    throw new InvalidDataException("Entity property has no value.");
                properties.Add(new ParsedProperty(key.Text, value.Text,
                    key.Begin, key.End, value.Begin, value.End));
            }
        }
    }
}

internal sealed class AddonMapEntsAdapter : AssetAuthoringAdapter<AddonMapEntsAsset, AddonMapEntsDraft>
{
    public override XAssetType AssetType => XAssetType.AddonMapEnts;
    public override AddonMapEntsDraft CreateDraft(AddonMapEntsAsset asset) => new(asset);
    public override AddonMapEntsDraft CloneDraft(AddonMapEntsDraft draft) => draft.Clone();
    public override AddonMapEntsAsset CreateDefinition(AddonMapEntsDraft draft) => draft.ToAsset();
    public override bool SemanticallyEquals(AddonMapEntsDraft left, AddonMapEntsDraft right) => left.Matches(right);
}

using MapConverter.IW3.PC.Assets;
using MapConverter.IW3.PC.Assets.XModel;
using MapConverter.IW3.PC.Assets.World;

namespace MapConverter.IW3.PC.Readers.World;

internal sealed record Iw3PcModelProvider(string Name, string FastFile, int RecordId);

internal sealed class Iw3PcWorldSource
{
    private readonly Iw3PcZone zone;
    private readonly IReadOnlyDictionary<string, Iw3PcModelProvider> providers;
    private readonly Dictionary<string, int> collisionModels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Iw3PcAsset> localModels = new(StringComparer.OrdinalIgnoreCase);
    internal Iw3PcAsset Clip { get; }
    internal Iw3PcAsset Graphics { get; }
    internal Iw3PcAsset Common { get; }
    private Iw3PcAsset Entities { get; }

    internal Iw3PcWorldSource(Iw3PcZone zone, IReadOnlyDictionary<string, Iw3PcModelProvider> providers)
    {
        this.zone = zone;
        this.providers = providers;
        Clip = Required("clipmap");
        Graphics = Required("gfxworld");
        Common = Required("comworld");
        Entities = Required("mapents");
        if (Clip.Name != Graphics.Name || Clip.Name != Common.Name || Clip.Name != Entities.Name)
            throw new InvalidDataException("IW3 PC ClipMap, GfxWorld, ComWorld and MapEnts names disagree.");
        foreach (var asset in zone.Assets.Where(asset => asset.Type == "xmodel"))
            localModels[asset.Name.Replace('\\', '/')] = asset;
    }

    private Iw3PcAsset Required(string type)
    {
        var records = zone.Assets.Where(asset => asset.Type == type && !asset.Name.StartsWith(',')).ToArray();
        return records.Length == 1 ? records[0] :
            throw new InvalidDataException($"Expected one concrete IW3 PC {type}, found {records.Length}.");
    }

    internal ReadOnlySpan<byte> EntityText()
    {
        int length = zone.Int32(Entities.Offset + 8), buffer = zone.Pointer(Entities.Offset + 4);
        if (length <= 0 || buffer == 0) throw new InvalidDataException("IW3 PC MapEnts has no entity text.");
        var text = zone.Bytes(buffer, length);
        int end = text.IndexOf((byte)0);
        return end < 0 ? text : text[..end];
    }

    internal void IncludeScriptModel(string name)
    {
        string key = name.Replace('\\', '/');
        if (localModels.TryGetValue(key, out var asset) ||
            providers.ContainsKey(key) && localModels.TryGetValue("," + key, out asset))
            IncludeModel(asset.Offset);
    }

    private string IncludeModel(int offset)
    {
        if (offset == 0) throw new InvalidDataException("IW3 PC world references a null XModel.");
        string name = zone.String(offset);
        if (name.StartsWith(','))
        {
            if (!providers.TryGetValue(name[1..].Replace('\\', '/'), out var provider))
                throw new InvalidDataException($"IW3 PC world requires source XModel '{name[1..]}'.");
            name = provider.Name;
            offset = 0;
        }
        if (name.Length is 0 or > 1024 || name.StartsWith(','))
            throw new InvalidDataException($"Invalid IW3 PC world XModel name '{name}'.");
        if (!collisionModels.TryAdd(name, offset) && collisionModels[name] != offset)
            throw new InvalidDataException($"IW3 PC world uses conflicting definitions of XModel '{name}'.");
        return name;
    }

    internal Iw3XModelCollisionSourceData ReadCollision(CancellationToken token)
    {
        var collision = new Dictionary<string, Iw3XModelCollisionSource>(StringComparer.Ordinal);
        foreach (var (name, offset) in collisionModels)
        {
            token.ThrowIfCancellationRequested();
            if (offset != 0) collision.Add(name, new Iw3PcModelSource(zone, offset).ReadCollision());
        }
        // Large source libraries can span dozens of zones. Read only providers actually used
        // by the world and retain their collision records after releasing each decoded zone.
        var external = collisionModels.Where(pair => pair.Value == 0)
            .Select(pair => providers[pair.Key.Replace('\\', '/')]);
        foreach (var group in external.GroupBy(provider => provider.FastFile, StringComparer.Ordinal))
        {
            var source = Iw3PcZone.Load(group.Key, token);
            foreach (var provider in group)
            {
                token.ThrowIfCancellationRequested();
                var asset = source.Assets[provider.RecordId];
                if (asset.Type != "xmodel" || asset.Name != provider.Name)
                    throw new InvalidDataException($"XModel '{provider.Name}' changed in '{provider.FastFile}'.");
                collision.Add(asset.Name, new Iw3PcModelSource(source, asset.Offset).ReadCollision());
            }
        }
        return new(Clip.Name, collision);
    }

    internal Iw3DynamicEntitySourceData ReadDynamicEntities(CancellationToken token)
    {
        var lists = new Iw3DynamicEntitySource[2][];
        for (int list = 0; list < lists.Length; list++)
        {
            int count = zone.UInt16(Clip.Offset + 244 + 2 * list);
            int table = zone.Pointer(Clip.Offset + 248 + 4 * list);
            if (count != 0 && table == 0) throw new InvalidDataException($"IW3 PC dynamic entity list {list} is missing.");
            var rows = lists[list] = new Iw3DynamicEntitySource[count];
            for (int i = 0; i < count; i++)
            {
                token.ThrowIfCancellationRequested();
                int row = checked(table + i * 96), model = zone.Pointer(row + 32);
                int type = zone.Int32(row);
                var quat = zone.Floats(row + 4, 4);
                if (type is not (1 or 2) || quat.All(v => v == 0) || (model == 0) != (list == 1))
                    throw new InvalidDataException($"Invalid IW3 PC dynamic entity {list}:{i} (type {type}, model 0x{model:X}).");
                rows[i] = new(type, quat, zone.Floats(row + 20, 3), model == 0 ? null : IncludeModel(model),
                    zone.UInt16(row + 36), zone.UInt16(row + 38), AssetName(row + 40), AssetName(row + 44),
                    AssetName(row + 48), zone.Int32(row + 52), zone.Floats(row + 56, 3), zone.Floats(row + 68, 3),
                    zone.Floats(row + 80, 3), zone.Int32(row + 92));
            }
        }
        return new(Clip.Name, lists);
    }

    internal ushort[][]? ReadShadowModelIndices()
    {
        int table = zone.Pointer(Graphics.Offset + 572);
        if (table == 0) return null;
        int lightCount = zone.Int32(Graphics.Offset + 220);
        var byLight = new ushort[lightCount][];
        for (int light = 0; light < lightCount; light++)
        {
            int row = checked(table + light * 12);
            int count = zone.UInt16(row + 2);
            int list = zone.Pointer(row + 8);
            if (count != 0 && list == 0)
                throw new InvalidDataException($"IW3 PC primary light {light} is missing its static shadow casters.");
            var ids = new ushort[count];
            for (int i = 0; i < ids.Length; i++)
                ids[i] = zone.UInt16(checked(list + i * 2));
            byLight[light] = ids;
        }
        return byLight;
    }

    private string? AssetName(int field)
    {
        int target = zone.Pointer(field);
        if (target == 0) return null;
        string name = zone.String(target);
        if (name.Length is 0 or > 1024) throw new InvalidDataException($"Invalid IW3 PC asset name at 0x{field:X}.");
        return name;
    }

    internal IEnumerable<(string Name, float[] Origin, float[] Axis, float Scale, byte Flags,
        float CullDistance, byte ReflectionProbeIndex, byte PrimaryLightIndex,
        float[] BoundsMins, float[] BoundsMaxs, byte[] GroundLighting)> StaticPlacements()
    {
        int count = zone.Int32(Graphics.Offset + 580), table = zone.Pointer(Graphics.Offset + 668);
        int instances = zone.Pointer(Graphics.Offset + 656);
        if (count < 0 || count != 0 && (table == 0 || instances == 0))
            throw new InvalidDataException("Invalid IW3 PC static model table.");
        for (int i = 0; i < count; i++)
        {
            int row = checked(table + i * 76), instance = checked(instances + i * 28);
            yield return (IncludeModel(zone.Pointer(row + 56)), zone.Floats(row + 4, 3), zone.Floats(row + 16, 9),
                zone.Floats(row + 52, 1)[0], zone.Bytes(row + 72, 1)[0],
                zone.Floats(row, 1)[0], zone.Bytes(row + 68, 1)[0], zone.Bytes(row + 69, 1)[0],
                zone.Floats(instance, 3), zone.Floats(instance + 12, 3), zone.Bytes(instance + 24, 4).ToArray());
        }
    }
}

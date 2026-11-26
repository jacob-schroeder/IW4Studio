using System.Buffers.Binary;
using System.Text.Json;
using MapConverter.IW3.PC.Readers;

namespace MapConverter.IW3.PC.Assets;

/// <summary>Reads the IW3 PC Fx graph in the shape consumed by the effect compiler.</summary>
public static class Iw3PcEffectExporter
{
    public static byte[] Export(Iw3PcZone zone, IReadOnlyCollection<string> roots,
        IReadOnlyDictionary<string, int>? selectedRecords, CancellationToken token)
    {
        var definitions = zone.Assets.Where(a => a.Type == "fx" && !a.Name.StartsWith(','))
            .GroupBy(a => Iw3PcAsset.CanonicalName(a.Name), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var references = zone.Assets.Where(a => a.Type == "fx" && a.Name.StartsWith(','))
            .Select(a => Iw3PcAsset.CanonicalName(a.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<string>(roots);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var external = new SortedSet<string>(StringComparer.Ordinal);
        var unresolved = new SortedSet<string>(StringComparer.Ordinal);
        var effects = new List<object>();
        while (pending.Count != 0)
        {
            token.ThrowIfCancellationRequested();
            string name = Iw3PcAsset.CanonicalName(pending.Dequeue());
            if (!visited.Add(name)) continue;
            Iw3PcAsset? asset;
            if (selectedRecords is not null && selectedRecords.TryGetValue(name, out int recordId))
            {
                if (recordId < 0 || recordId >= zone.Assets.Count)
                    throw new InvalidDataException($"Selected IW3 FX record does not match '{name}'.");
                Iw3PcAsset selected = zone.Assets[recordId];
                if (selected.Type != "fx" || selected.Name.StartsWith(',') ||
                    Iw3PcAsset.CanonicalName(selected.Name) != name)
                    throw new InvalidDataException($"Selected IW3 FX record does not match '{name}'.");
                asset = selected;
            }
            else definitions.TryGetValue(name, out asset);
            if (asset is null)
            {
                (references.Contains(name) ? external : unresolved).Add(name);
                continue;
            }
            var children = new List<string>();
            effects.Add(ReadEffect(zone, asset, children));
            foreach (string child in children)
            {
                if (selectedRecords is null) pending.Enqueue(Iw3PcAsset.CanonicalName(child));
                else external.Add(Iw3PcAsset.CanonicalName(child));
            }
        }
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            effects, externalEffectReferences = external, unresolvedEffectReferences = unresolved
        });
    }

    private static object ReadEffect(Iw3PcZone zone, Iw3PcAsset asset, List<string> children)
    {
        int root = asset.Offset;
        int looping = I32(zone, root + 16), oneShot = I32(zone, root + 20), emission = I32(zone, root + 24);
        int count = checked(looping + oneShot + emission);
        if (looping < 0 || oneShot < 0 || emission < 0)
            throw new InvalidDataException($"Invalid IW3 PC FX element count in '{asset.Name}'.");
        int table = zone.Pointer(root + 28);
        if (table == 0 && count != 0)
            throw new InvalidDataException($"IW3 PC FX '{asset.Name}' has no elements.");
        var elements = new object[count];
        for (int i = 0; i < count; i++)
            elements[i] = ReadElement(zone, table + i * 252, children);
        return new
        {
            asset = AssetName(asset.Name), flags = I32(zone, root + 4),
            totalSize = I32(zone, root + 8), msecLoopingLife = I32(zone, root + 12),
            elemDefCountLooping = looping, elemDefCountOneShot = oneShot,
            elemDefCountEmission = emission, elemDefs = elements
        };
    }

    private static object ReadElement(Iw3PcZone zone, int row, List<string> children)
    {
        int type = B(zone, row + 176), count = B(zone, row + 177);
        int velIntervals = B(zone, row + 178), visIntervals = B(zone, row + 179);
        int velocities = zone.Pointer(row + 180), states = zone.Pointer(row + 184);
        object[]? velSamples = velocities == 0 ? null : Enumerable.Range(0, velIntervals + 1)
            .Select(i => VelocitySample(zone, velocities + i * 96)).ToArray();
        object[]? visSamples = states == 0 ? null : Enumerable.Range(0, visIntervals + 1)
            .Select(i => VisualSample(zone, states + i * 48)).ToArray();
        object[]? visuals;
        if (type == 9)
        {
            int table = zone.Pointer(row + 188);
            visuals = table == 0 ? null : Enumerable.Range(0, count).Select(i => (object)new
            {
                materials = new[] { RefAsset(zone, table + i * 8, 0), RefAsset(zone, table + i * 8 + 4, 0) }
            }).ToArray();
        }
        else if (count > 1)
        {
            int table = zone.Pointer(row + 188);
            visuals = table == 0 ? null : Enumerable.Range(0, count)
                .Select(i => ReadVisual(zone, table + i * 4, type, children)).ToArray();
        }
        else visuals = count == 0 ? [] : [ReadVisual(zone, row + 188, type, children)];
        int trail = zone.Pointer(row + 244);
        return new
        {
            flags = I32(zone, row), spawn = new { word0 = I32(zone, row + 4), word1 = I32(zone, row + 8) },
            spawnRange = FloatRange(zone, row + 12), fadeInRange = FloatRange(zone, row + 20),
            fadeOutRange = FloatRange(zone, row + 28), spawnFrustumCullRadius = F32(zone, row + 36),
            spawnDelayMsec = IntRange(zone, row + 40), lifeSpanMsec = IntRange(zone, row + 48),
            spawnOrigin = Ranges(zone, row + 56), spawnOffsetRadius = FloatRange(zone, row + 80),
            spawnOffsetHeight = FloatRange(zone, row + 88), spawnAngles = Ranges(zone, row + 96),
            angularVelocity = Ranges(zone, row + 120), initialRotation = FloatRange(zone, row + 144),
            gravity = FloatRange(zone, row + 152), reflectionFactor = FloatRange(zone, row + 160),
            atlas = new
            {
                behavior = B(zone, row + 168), index = B(zone, row + 169), fps = B(zone, row + 170),
                loopCount = B(zone, row + 171), colIndexBits = B(zone, row + 172),
                rowIndexBits = B(zone, row + 173), entryCount = I16(zone, row + 174)
            },
            elemType = type, visualCount = count, velIntervalCount = velIntervals,
            visStateIntervalCount = visIntervals, velSamples, visSamples, visuals,
            collMins = Vec3(zone, row + 192), collMaxs = Vec3(zone, row + 204),
            effectOnImpact = EffectRef(zone, row + 216, children),
            effectOnDeath = EffectRef(zone, row + 220, children),
            effectEmitted = EffectRef(zone, row + 224, children),
            emitDist = FloatRange(zone, row + 228), emitDistVariance = FloatRange(zone, row + 236),
            trailDef = trail == 0 ? null : Trail(zone, trail), sortOrder = B(zone, row + 248),
            lightingFrac = B(zone, row + 249), useItemClip = B(zone, row + 250)
        };
    }

    private static object ReadVisual(Iw3PcZone zone, int field, int type, List<string> children) => type switch
    {
        <= 4 => new { kind = "material", asset = RefAsset(zone, field, 0) },
        5 => new { kind = "model", asset = RefAsset(zone, field, 0) },
        6 or 7 => new { kind = "none", asset = (object?)null },
        8 => new { kind = "sound", name = Optional(zone, field) },
        10 => new { kind = "effect", asset = EffectRef(zone, field, children) },
        _ => throw new NotSupportedException($"Unsupported IW3 PC FX visual type {type}.")
    };

    private static object? RefAsset(Iw3PcZone zone, int field, int nameOffset)
    {
        int asset = zone.Pointer(field);
        return asset == 0 ? null : AssetName(zone.String(asset + nameOffset));
    }

    private static object? EffectRef(Iw3PcZone zone, int field, List<string> children)
    {
        string? name = Optional(zone, field);
        if (name is not null) children.Add(Iw3PcAsset.CanonicalName(name));
        return name is null ? null : AssetName(name);
    }

    private static object AssetName(string name) => new { name = Iw3PcAsset.CanonicalName(name) };
    private static string? Optional(Iw3PcZone zone, int field) =>
        zone.Pointer(field) == 0 ? null : zone.String(field);

    private static object VelocitySample(Iw3PcZone zone, int row) =>
        new { local = VelocityFrame(zone, row), world = VelocityFrame(zone, row + 48) };
    private static object VelocityFrame(Iw3PcZone zone, int row) =>
        new { velocity = VecRange(zone, row), totalDelta = VecRange(zone, row + 24) };
    private static object VecRange(Iw3PcZone zone, int row) =>
        new { @base = Vec3(zone, row), amplitude = Vec3(zone, row + 12) };
    private static object VisualSample(Iw3PcZone zone, int row) =>
        new { @base = VisualState(zone, row), amplitude = VisualState(zone, row + 24) };
    private static object VisualState(Iw3PcZone zone, int row) =>
        new { color = Enumerable.Range(0, 4).Select(i => B(zone, row + i)).ToArray(),
            rotationDelta = F32(zone, row + 4),
            rotationTotal = F32(zone, row + 8), size = new[] { F32(zone, row + 12), F32(zone, row + 16) },
            scale = F32(zone, row + 20) };
    private static object Trail(Iw3PcZone zone, int row)
    {
        int vertCount = I32(zone, row + 12), indCount = I32(zone, row + 20);
        int verts = zone.Pointer(row + 16), inds = zone.Pointer(row + 24);
        if (vertCount < 0 || indCount < 0) throw new InvalidDataException("Negative IW3 FX trail count.");
        return new
        {
            scrollTimeMsec = I32(zone, row), repeatDist = I32(zone, row + 4),
            splitDist = I32(zone, row + 8), vertCount, indCount,
            verts = verts == 0 ? null : Enumerable.Range(0, vertCount).Select(i =>
            {
                int cell = verts + i * 20;
                return new { pos = new[] { F32(zone, cell), F32(zone, cell + 4) },
                    normal = new[] { F32(zone, cell + 8), F32(zone, cell + 12) },
                    texCoord = F32(zone, cell + 16) };
            }).ToArray(),
            inds = inds == 0 ? null : Enumerable.Range(0, indCount).Select(i => U16(zone, inds + i * 2)).ToArray()
        };
    }

    private static object FloatRange(Iw3PcZone zone, int row) =>
        new { @base = F32(zone, row), amplitude = F32(zone, row + 4) };
    private static object IntRange(Iw3PcZone zone, int row) =>
        new { @base = I32(zone, row), amplitude = I32(zone, row + 4) };
    private static object[] Ranges(Iw3PcZone zone, int row) =>
        Enumerable.Range(0, 3).Select(i => FloatRange(zone, row + i * 8)).ToArray();
    private static float[] Vec3(Iw3PcZone zone, int row) =>
        [F32(zone, row), F32(zone, row + 4), F32(zone, row + 8)];
    private static int B(Iw3PcZone zone, int row) => zone.Bytes(row, 1)[0];
    private static short I16(Iw3PcZone zone, int row) => BinaryPrimitives.ReadInt16LittleEndian(zone.Bytes(row, 2));
    private static int U16(Iw3PcZone zone, int row) => BinaryPrimitives.ReadUInt16LittleEndian(zone.Bytes(row, 2));
    private static int I32(Iw3PcZone zone, int row) => BinaryPrimitives.ReadInt32LittleEndian(zone.Bytes(row, 4));
    private static float F32(Iw3PcZone zone, int row) => BinaryPrimitives.ReadSingleLittleEndian(zone.Bytes(row, 4));
}

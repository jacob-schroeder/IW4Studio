using System.Collections.ObjectModel;
using IW4.FastFiles.Emitters.Assets;
using IW4.Studio.MapEditor.Editing.Identity;

namespace IW4.Studio.MapEditor.Compilation.Lighting;

/// <summary>
/// Immutable M5-A allocation of the shared ComWorld/GfxWorld primary-light
/// index space. Row zero is compiler-owned and authored non-sun rows are
/// ordered solely by stable source ID.
/// </summary>
public sealed class PrimaryLightOrdinalPlan
{
    public const string CompilerIdentity =
        "iw4-studio.primary-light.ordinal-plan.row-zero-stable-source-id@1";

    public const int SentinelOrdinal = 0;
    public const int NoSunPrimaryLightIndex = 0;
    public const int MaximumPrimaryLightCount = (int)byte.MaxValue + 1;

    private static readonly ComPrimaryLightBuildData Sentinel =
        new(
            Type: 0,
            CanUseShadowMap: 0,
            Exponent: 0,
            Unused: 0,
            Color: new Float3BuildData(0, 0, 0),
            Direction: new Float3BuildData(0, 0, 0),
            Origin: new Float3BuildData(0, 0, 0),
            Radius: 0,
            CosHalfFovOuter: 0,
            CosHalfFovInner: 0,
            CosHalfFovExpanded: 0,
            RotationLimit: 0,
            TranslationLimit: 0,
            DefName: null);

    private readonly IReadOnlyList<AuthoredPrimaryLightSource>
        _orderedSources;
    private readonly IReadOnlyList<ComPrimaryLightBuildData>
        _comPrimaryLights;
    private readonly IReadOnlyDictionary<MapObjectId, int>
        _ordinalBySourceId;

    private PrimaryLightOrdinalPlan(
        IReadOnlyList<AuthoredPrimaryLightSource> orderedSources,
        IReadOnlyList<ComPrimaryLightBuildData> comPrimaryLights,
        IReadOnlyDictionary<MapObjectId, int> ordinalBySourceId)
    {
        _orderedSources = orderedSources;
        _comPrimaryLights = comPrimaryLights;
        _ordinalBySourceId = ordinalBySourceId;
    }

    public IReadOnlyList<AuthoredPrimaryLightSource> OrderedSources =>
        _orderedSources;

    public IReadOnlyList<ComPrimaryLightBuildData> ComPrimaryLights =>
        _comPrimaryLights;

    public IReadOnlyDictionary<MapObjectId, int> OrdinalBySourceId =>
        _ordinalBySourceId;

    public int AuthoredSourceCount => OrderedSources.Count;

    public int PrimaryLightCount => ComPrimaryLights.Count;

    public static PrimaryLightOrdinalPlan Create(
        IEnumerable<AuthoredPrimaryLightSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        AuthoredPrimaryLightSource[] materialized =
            sources.ToArray();
        if (materialized.Any(source => source is null))
        {
            throw new ArgumentException(
                "An authored primary-light collection cannot contain null sources.",
                nameof(sources));
        }
        if (materialized.Length >
            MaximumPrimaryLightCount - 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sources),
                $"The byte-indexed primary-light graph admits at most " +
                $"{MaximumPrimaryLightCount - 1} authored rows after its " +
                "compiler-owned sentinel.");
        }

        AuthoredPrimaryLightSource[] ordered = materialized
            .OrderBy(
                source => source.SourceId.ToString(),
                StringComparer.Ordinal)
            .ToArray();
        MapObjectId[] duplicateIds = ordered
            .GroupBy(source => source.SourceId)
            .Where(group => group.Count() != 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateIds.Length != 0)
        {
            throw new ArgumentException(
                "Authored primary-light source IDs must be unique: " +
                string.Join(", ", duplicateIds),
                nameof(sources));
        }

        var rows =
            new ComPrimaryLightBuildData[ordered.Length + 1];
        rows[SentinelOrdinal] = Sentinel;
        var ordinalBySourceId =
            new Dictionary<MapObjectId, int>(ordered.Length);
        for (int sourceIndex = 0;
             sourceIndex < ordered.Length;
             sourceIndex++)
        {
            int ordinal = checked(sourceIndex + 1);
            AuthoredPrimaryLightSource source = ordered[sourceIndex];
            rows[ordinal] = source.Compile();
            ordinalBySourceId.Add(source.SourceId, ordinal);
        }

        return new PrimaryLightOrdinalPlan(
            Array.AsReadOnly(ordered),
            Array.AsReadOnly(rows),
            new ReadOnlyDictionary<MapObjectId, int>(
                ordinalBySourceId));
    }

    public bool TryGetOrdinal(
        MapObjectId sourceId,
        out int ordinal) =>
        _ordinalBySourceId.TryGetValue(sourceId, out ordinal);

    public int GetOrdinal(MapObjectId sourceId) =>
        _ordinalBySourceId.TryGetValue(sourceId, out int ordinal)
            ? ordinal
            : throw new KeyNotFoundException(
                $"Primary-light source '{sourceId}' is not part of this ordinal plan.");

    public AuthoredPrimaryLightSource GetSource(int ordinal)
    {
        if (ordinal <= SentinelOrdinal ||
            ordinal >= PrimaryLightCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ordinal),
                "Only authored, non-sentinel primary-light ordinals have source objects.");
        }

        return OrderedSources[ordinal - 1];
    }
}

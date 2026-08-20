using System.Numerics;

using IW4.Assets.Assets.ComWorld;
using IW4.Assets.Assets.GfxMap;
using IW4.Render.SceneBuilding;
using IW4.Render.Scheduling.Dpvs;
using IW4.Render.Scheduling.Lighting;

namespace IW4.Render.Scheduling.Shadows;

/// <summary>
/// Scene-lifetime spot-caster membership plus the exact normal-camera
/// four-entry selection used by every renderer backend. The planner owns no
/// atlas state and publishes nothing; its returned plans remain valid until
/// the next call to <see cref="CreateFramePlans"/>.
/// </summary>
internal sealed class MapRenderSpotShadowPlanner
{
    private static readonly Vector3 LuminanceWeights =
        new(0.2989f, 0.587f, 0.114f);

    private readonly MapRenderWorldSceneSource _source;
    private readonly MapRenderWorldEvent20SceneLightFrameInput _sceneLights;
    private readonly MapRenderSceneTechniqueVariantCatalog _techniques;
    private readonly SpotCasterMembership?[] _membershipByLight;
    private readonly int _surfaceCount;
    private readonly int _staticModelCount;
    private readonly HashSet<int> _visibleLightIndices = [];
    private readonly List<SpotCandidate> _candidates = [];
    private readonly List<MapRenderSpotShadowPlan> _plans = [];

    private MapRenderSpotShadowPlanner(
        MapRenderWorldSceneSource source,
        MapRenderWorldEvent20SceneLightFrameInput sceneLights,
        MapRenderSceneTechniqueVariantCatalog techniques,
        SpotCasterMembership?[] membershipByLight,
        int surfaceCount,
        int staticModelCount)
    {
        _source = source;
        _sceneLights = sceneLights;
        _techniques = techniques;
        _membershipByLight = membershipByLight;
        _surfaceCount = surfaceCount;
        _staticModelCount = staticModelCount;
    }

    internal static MapRenderSpotShadowPlanner? TryCreate(
        MapRenderWorldSceneSource source,
        MapRenderWorldEvent20SceneLightFrameInput sceneLights,
        MapRenderSceneTechniqueVariantCatalog techniques)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sceneLights);
        ArgumentNullException.ThrowIfNull(techniques);

        GfxWorldAsset world = source.World;
        if (world.SurfaceCount < 0 ||
            world.Dpvs.SModelCount > int.MaxValue ||
            !source.AssetLookup.HasCanonicalAssetPoolRevision(
                sceneLights.AssetPoolRevision))
        {
            return null;
        }

        int surfaceCount = world.SurfaceCount;
        int staticModelCount = checked((int)world.Dpvs.SModelCount);
        var membershipByLight = new SpotCasterMembership?[world.ShadowGeom.Count];
        for (int sceneLightIndex = 0;
             sceneLightIndex < membershipByLight.Length;
             sceneLightIndex++)
        {
            membershipByLight[sceneLightIndex] = TryCreateMembership(
                world,
                sceneLightIndex,
                surfaceCount,
                staticModelCount);
        }

        int firstNonSunPrimaryLightIndex = checked(
            world.SunPrimaryLightIndex + 1);
        bool hasEligibleSpot = false;
        for (int sceneLightIndex = firstNonSunPrimaryLightIndex;
             sceneLightIndex < sceneLights.SceneLightCount &&
             sceneLightIndex < membershipByLight.Length;
             sceneLightIndex++)
        {
            MapRenderWorldEvent20SceneLight light =
                sceneLights.GetSceneLight(sceneLightIndex);
            if (light.Type == GfxLightType.Spot && light.CanUseShadowMap)
            {
                hasEligibleSpot = true;
                break;
            }
        }
        if (!hasEligibleSpot)
            return null;

        return new MapRenderSpotShadowPlanner(
            source,
            sceneLights,
            techniques,
            membershipByLight,
            surfaceCount,
            staticModelCount);
    }

    internal bool MatchesFrameCardinality(
        MapRenderWorldDpvsThreeViewFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return frame.Camera.SurfaceCount == _surfaceCount &&
            frame.Camera.StaticModelCount == _staticModelCount &&
            _source.AssetLookup.HasCanonicalAssetPoolRevision(
                _sceneLights.AssetPoolRevision);
    }

    internal IReadOnlyList<MapRenderSpotShadowPlan> CreateFramePlans(
        MapRenderWorldDpvsThreeViewFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        _plans.Clear();
        if (!MatchesFrameCardinality(frame))
            return _plans;

        _visibleLightIndices.Clear();
        ReadOnlySpan<uint> visibleSurfaceWords = frame.Camera.SurfaceBitSpan;
        for (int wordIndex = 0;
             wordIndex < visibleSurfaceWords.Length;
             wordIndex++)
        {
            uint pending = visibleSurfaceWords[wordIndex];
            while (pending != 0)
            {
                int bitInWord = BitOperations.LeadingZeroCount(pending);
                int surfaceIndex = checked(wordIndex * 32 + bitInWord);
                pending &= ~(0x8000_0000u >> bitInWord);
                if (surfaceIndex >= frame.Camera.SurfaceCount)
                    break;

                if ((uint)surfaceIndex <
                        (uint)_techniques.WorldSurfaces.Count &&
                    _techniques.WorldSurfaces[surfaceIndex] is { } variants)
                {
                    _visibleLightIndices.Add(variants.PrimaryLightIndex);
                }
            }
        }

        _candidates.Clear();
        foreach (int sceneLightIndex in _visibleLightIndices)
        {
            if (sceneLightIndex <= _source.World.SunPrimaryLightIndex ||
                sceneLightIndex >= _sceneLights.SceneLightCount ||
                (uint)sceneLightIndex >= (uint)_membershipByLight.Length ||
                _membershipByLight[sceneLightIndex] is not { } membership)
            {
                continue;
            }

            MapRenderWorldEvent20SceneLight light =
                _sceneLights.GetSceneLight(sceneLightIndex);
            if (light.Type != GfxLightType.Spot ||
                !light.CanUseShadowMap ||
                !MapRenderSpotShadowProjectionCalculator.TryCreateAtlasEntry(
                    light,
                    sceneLightIndex,
                    atlasSlot: 0,
                    fade: 1f,
                    out _))
            {
                continue;
            }

            Vector3 eyeReference = frame.Projection.CameraOrigin +
                64f * frame.Projection.CameraForward;
            Vector3 focusDelta = light.Origin - eyeReference +
                (-light.Radius * 0.125f) * light.Direction;
            float luminance = Vector3.Dot(light.Color, LuminanceWeights);
            float score =
                light.Radius * luminance / (focusDelta.Length() + 1f);
            if (!float.IsFinite(score))
                continue;

            _candidates.Add(new SpotCandidate(
                sceneLightIndex,
                light,
                score,
                membership));
        }

        _candidates.Sort(CompareCandidates);
        foreach (SpotCandidate candidate in _candidates)
        {
            int atlasSlot = _plans.Count;
            if (!MapRenderSpotShadowProjectionCalculator.TryCreateAtlasEntry(
                    candidate.Light,
                    candidate.SceneLightIndex,
                    atlasSlot,
                    fade: 1f,
                    out MapRenderSpotShadowAtlasEntry? entry) ||
                entry is null)
            {
                continue;
            }
            _plans.Add(new MapRenderSpotShadowPlan(
                entry,
                candidate.Membership.WorldSurfaceIndices,
                candidate.Membership.StaticDrawInstances));
            if (_plans.Count ==
                MapRenderSpotShadowAtlasLayout.MaximumEntryCount)
            {
                break;
            }
        }
        return _plans;
    }

    private static SpotCasterMembership? TryCreateMembership(
        GfxWorldAsset world,
        int sceneLightIndex,
        int surfaceCount,
        int staticModelCount)
    {
        if ((uint)sceneLightIndex >= (uint)world.ShadowGeom.Count ||
            world.ShadowGeom[sceneLightIndex] is not { } geometry ||
            geometry.SurfaceCount != geometry.SortedSurfIndex.Count ||
            geometry.SModelCount != geometry.SModelIndex.Count)
        {
            return null;
        }

        var surfaces = new int[geometry.SortedSurfIndex.Count];
        for (int index = 0; index < surfaces.Length; index++)
        {
            int surfaceIndex = geometry.SortedSurfIndex[index];
            if ((uint)surfaceIndex >= (uint)surfaceCount)
                return null;
            surfaces[index] = surfaceIndex;
        }

        var statics = new MapRenderSunShadowStaticCasterIdentity[
            geometry.SModelIndex.Count];
        for (int index = 0; index < statics.Length; index++)
        {
            int objectIndex = geometry.SModelIndex[index];
            if ((uint)objectIndex >= (uint)staticModelCount)
                return null;
            statics[index] = new MapRenderSunShadowStaticCasterIdentity(
                objectIndex,
                objectIndex,
                objectIndex);
        }
        return new SpotCasterMembership(surfaces, statics);
    }

    private static int CompareCandidates(
        SpotCandidate left,
        SpotCandidate right)
    {
        int score = right.Score.CompareTo(left.Score);
        return score != 0
            ? score
            : left.SceneLightIndex.CompareTo(right.SceneLightIndex);
    }

    private readonly record struct SpotCasterMembership(
        int[] WorldSurfaceIndices,
        MapRenderSunShadowStaticCasterIdentity[] StaticDrawInstances);

    private readonly record struct SpotCandidate(
        int SceneLightIndex,
        MapRenderWorldEvent20SceneLight Light,
        float Score,
        SpotCasterMembership Membership);
}

internal readonly record struct MapRenderSpotShadowPlan(
    MapRenderSpotShadowAtlasEntry Entry,
    int[] WorldSurfaceIndices,
    MapRenderSunShadowStaticCasterIdentity[] StaticDrawInstances);

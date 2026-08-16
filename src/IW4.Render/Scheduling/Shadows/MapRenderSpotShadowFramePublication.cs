using System.Numerics;
using IW4.Assets.Assets.ComWorld;
using IW4.Render.Scheduling.Dpvs;
using IW4.Render.Scheduling.Lighting;
using IW4.Render.Transforms;

namespace IW4.Render.Scheduling.Shadows;

/// <summary>The normal PS3 spot-shadow target is four vertical 512-square tiles.</summary>
public static class MapRenderSpotShadowAtlasLayout
{
    public const int TileSize = 512;
    public const int Width = TileSize;
    public const int Height = TileSize * MaximumEntryCount;
    public const int MaximumEntryCount = 4;
}

/// <summary>
/// Exact per-light projection payload retained from allocation through
/// same-revision atlas publication.
/// </summary>
public sealed class MapRenderSpotShadowAtlasEntry
{
    internal MapRenderSpotShadowAtlasEntry(
        int sceneLightIndex,
        int atlasSlot,
        Matrix4x4 casterViewProjection,
        Matrix4x4 shadowLookupMatrix,
        float fade)
    {
        if (sceneLightIndex <= 0)
            throw new ArgumentOutOfRangeException(nameof(sceneLightIndex));
        if ((uint)atlasSlot >=
            MapRenderSpotShadowAtlasLayout.MaximumEntryCount)
        {
            throw new ArgumentOutOfRangeException(nameof(atlasSlot));
        }
        if (!RenderMatrixValidation.IsFinite(casterViewProjection))
        {
            throw new ArgumentException(
                "The spot-shadow caster projection must be finite.",
                nameof(casterViewProjection));
        }
        if (!RenderMatrixValidation.IsFinite(shadowLookupMatrix))
        {
            throw new ArgumentException(
                "The spot-shadow lookup matrix must be finite.",
                nameof(shadowLookupMatrix));
        }
        if (!float.IsFinite(fade) || fade is < 0f or > 1f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fade),
                "The spot-shadow fade must be between zero and one.");
        }

        SceneLightIndex = sceneLightIndex;
        AtlasSlot = atlasSlot;
        CasterViewProjection = casterViewProjection;
        ShadowLookupMatrix = shadowLookupMatrix;
        Fade = fade;
    }

    public int SceneLightIndex { get; }

    public int AtlasSlot { get; }

    public Matrix4x4 CasterViewProjection { get; }

    /// <summary>
    /// World-coordinate lookup source. Draw-time derived-matrix resolution
    /// applies the normal-camera eye offset to this exact matrix.
    /// </summary>
    public Matrix4x4 ShadowLookupMatrix { get; }

    public float Fade { get; }
}

/// <summary>
/// Pure PS3 spot projection and normal four-tile atlas lookup calculation.
/// Invalid light rows fail closed instead of publishing a guessed projection.
/// </summary>
internal static class MapRenderSpotShadowProjectionCalculator
{
    private const float NearPlane = 1f;
    private static readonly float MinimumOuterCosine =
        BitConverter.Int32BitsToSingle(unchecked((int)0x3A83126F));
    private static readonly float MaximumOuterCosine =
        BitConverter.Int32BitsToSingle(unchecked((int)0x3F7FBE77));

    internal static bool TryCreateAtlasEntry(
        MapRenderWorldEvent20SceneLight light,
        int sceneLightIndex,
        int atlasSlot,
        float fade,
        out MapRenderSpotShadowAtlasEntry? entry)
    {
        ArgumentNullException.ThrowIfNull(light);
        if (sceneLightIndex <= 0)
            throw new ArgumentOutOfRangeException(nameof(sceneLightIndex));
        if ((uint)atlasSlot >=
            MapRenderSpotShadowAtlasLayout.MaximumEntryCount)
        {
            throw new ArgumentOutOfRangeException(nameof(atlasSlot));
        }
        if (!float.IsFinite(fade) || fade is < 0f or > 1f)
            throw new ArgumentOutOfRangeException(nameof(fade));

        entry = null;
        if (light.Type != GfxLightType.Spot ||
            !light.CanUseShadowMap ||
            !IsFinite(light.Origin) ||
            !IsFinite(light.Direction) ||
            !float.IsFinite(light.Radius) ||
            !(light.Radius > NearPlane) ||
            !float.IsFinite(light.CosHalfFovOuter))
        {
            return false;
        }

        float directionLength = light.Direction.Length();
        if (!(directionLength > 0f) || !float.IsFinite(directionLength))
            return false;

        Vector3 axis0 = -light.Direction / directionLength;
        if (!TryCreatePerpendicular(axis0, out Vector3 axis2))
            return false;
        Vector3 axis1 = Vector3.Cross(axis2, axis0);
        if (!IsFinite(axis1))
            return false;

        float outerCosine = Math.Clamp(
            light.CosHalfFovOuter,
            MinimumOuterCosine,
            MaximumOuterCosine);
        float tanHalfFov =
            MathF.Sqrt(1f - outerCosine * outerCosine) / outerCosine;
        if (!(tanHalfFov > 0f) || !float.IsFinite(tanHalfFov))
            return false;

        Matrix4x4 view =
            Matrix4x4.CreateTranslation(-light.Origin) *
            RenderViewerMatrixMath.CreateRotationOnlyView(
                axis0,
                axis1,
                axis2);
        Matrix4x4 projection =
            RenderViewerMatrixMath.CreateFiniteProjection(
                tanHalfFov,
                tanHalfFov,
                NearPlane,
                light.Radius);
        Matrix4x4 casterViewProjection = view * projection;
        Matrix4x4 lookup = CreateNormalAtlasLookup(
            casterViewProjection,
            atlasSlot);
        entry = new MapRenderSpotShadowAtlasEntry(
            sceneLightIndex,
            atlasSlot,
            casterViewProjection,
            lookup,
            fade);
        return true;
    }

    private static bool TryCreatePerpendicular(
        Vector3 forward,
        out Vector3 perpendicular)
    {
        float xSquared = forward.X * forward.X;
        float ySquared = forward.Y * forward.Y;
        float zSquared = forward.Z * forward.Z;
        int leastAxis = xSquared > ySquared ? 1 : 0;
        float selectedSquared = leastAxis == 0 ? xSquared : ySquared;
        if (selectedSquared > zSquared)
            leastAxis = 2;

        float selected = leastAxis switch
        {
            0 => forward.X,
            1 => forward.Y,
            _ => forward.Z
        };
        perpendicular = -forward * selected;
        switch (leastAxis)
        {
            case 0:
                perpendicular.X += 1f;
                break;
            case 1:
                perpendicular.Y += 1f;
                break;
            default:
                perpendicular.Z += 1f;
                break;
        }

        float length = perpendicular.Length();
        if (!(length > 0f) || !float.IsFinite(length))
            return false;
        perpendicular /= length;
        return IsFinite(perpendicular);
    }

    private static Matrix4x4 CreateNormalAtlasLookup(
        Matrix4x4 viewProjection,
        int atlasSlot)
    {
        const float xScale = 0.5f;
        const float xShift = 0.5f;
        const float yScale = -0.125f;
        float yShift = (2 * atlasSlot + 1) * 0.125f;
        return new Matrix4x4(
            viewProjection.M11 * xScale + viewProjection.M14 * xShift,
            viewProjection.M12 * yScale + viewProjection.M14 * yShift,
            viewProjection.M13,
            viewProjection.M14,
            viewProjection.M21 * xScale + viewProjection.M24 * xShift,
            viewProjection.M22 * yScale + viewProjection.M24 * yShift,
            viewProjection.M23,
            viewProjection.M24,
            viewProjection.M31 * xScale + viewProjection.M34 * xShift,
            viewProjection.M32 * yScale + viewProjection.M34 * yShift,
            viewProjection.M33,
            viewProjection.M34,
            viewProjection.M41 * xScale + viewProjection.M44 * xShift,
            viewProjection.M42 * yScale + viewProjection.M44 * yShift,
            viewProjection.M43,
            viewProjection.M44);
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);
}

/// <summary>
/// Renderer-agnostic authority that every planned local spot tile completed
/// for one exact three-view frame.
/// </summary>
public sealed class MapRenderSpotShadowAtlasReadyState
{
    private readonly MapRenderSpotShadowAtlasEntry[] _entries;
    private readonly Dictionary<int, MapRenderSpotShadowAtlasEntry>
        _entriesBySceneLight;

    internal MapRenderSpotShadowAtlasReadyState(
        MapRenderWorldDpvsThreeViewFrame frame,
        IReadOnlyList<MapRenderSpotShadowAtlasEntry> entries)
    {
        Frame = frame ?? throw new ArgumentNullException(nameof(frame));
        ArgumentNullException.ThrowIfNull(entries);
        _entries = entries.Select(entry => entry ??
            throw new ArgumentException(
                "Spot-shadow publication cannot contain null entries.",
                nameof(entries))).ToArray();
        if (_entries.Length is < 1 or >
            MapRenderSpotShadowAtlasLayout.MaximumEntryCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(entries),
                $"A spot-shadow publication must contain between one and {MapRenderSpotShadowAtlasLayout.MaximumEntryCount} entries.");
        }
        if (_entries.Select(entry => entry.SceneLightIndex).Distinct().Count() !=
            _entries.Length)
        {
            throw new ArgumentException(
                "Spot-shadow scene-light identities must be unique.",
                nameof(entries));
        }
        if (_entries.Select(entry => entry.AtlasSlot).Distinct().Count() !=
            _entries.Length)
        {
            throw new ArgumentException(
                "Spot-shadow atlas slots must be unique.",
                nameof(entries));
        }

        _entriesBySceneLight = _entries.ToDictionary(
            entry => entry.SceneLightIndex);
        Entries = Array.AsReadOnly(_entries);
    }

    public long Revision => Frame.Revision;

    public MapRenderWorldDpvsThreeViewFrame Frame { get; }

    public IReadOnlyList<MapRenderSpotShadowAtlasEntry> Entries { get; }

    public bool TryGetEntry(
        int sceneLightIndex,
        out MapRenderSpotShadowAtlasEntry? entry)
    {
        if (sceneLightIndex < 0)
        {
            entry = null;
            return false;
        }

        return _entriesBySceneLight.TryGetValue(sceneLightIndex, out entry);
    }
}

/// <summary>
/// Same-revision publication gate for the normal spot-shadow atlas. The ready
/// token is created only after every planned light tile records completion.
/// </summary>
public sealed class MapRenderSpotShadowFramePublication
{
    private readonly object _gate = new();
    private readonly MapRenderSpotShadowAtlasEntry[] _entries;
    private readonly Dictionary<int, int> _entryIndexBySceneLight;
    private int _completedEntryMask;
    private MapRenderSpotShadowAtlasReadyState? _atlasReady;

    internal MapRenderSpotShadowFramePublication(
        MapRenderWorldDpvsThreeViewFrame frame,
        IReadOnlyList<MapRenderSpotShadowAtlasEntry> entries)
    {
        Frame = frame ?? throw new ArgumentNullException(nameof(frame));
        ArgumentNullException.ThrowIfNull(entries);
        _entries = entries.Select(entry => entry ??
            throw new ArgumentException(
                "Spot-shadow publication cannot contain null entries.",
                nameof(entries))).ToArray();

        // Reuse the ready-state validation before any backend draw starts.
        _ = new MapRenderSpotShadowAtlasReadyState(Frame, _entries);
        _entryIndexBySceneLight = _entries
            .Select((entry, index) => (entry.SceneLightIndex, index))
            .ToDictionary(item => item.SceneLightIndex, item => item.index);
        Entries = Array.AsReadOnly(_entries);
    }

    public MapRenderWorldDpvsThreeViewFrame Frame { get; }

    public long Revision => Frame.Revision;

    public IReadOnlyList<MapRenderSpotShadowAtlasEntry> Entries { get; }

    public bool RecordEntryDrawCompleted(
        long revision,
        int sceneLightIndex)
    {
        if (revision != Revision)
        {
            throw new InvalidOperationException(
                $"Spot tile completion revision {revision} does not match frame {Revision}.");
        }
        if (!_entryIndexBySceneLight.TryGetValue(
                sceneLightIndex,
                out int entryIndex))
        {
            throw new ArgumentOutOfRangeException(
                nameof(sceneLightIndex),
                "The completed scene light was not planned for this spot atlas.");
        }

        int mask = 1 << entryIndex;
        lock (_gate)
        {
            if ((_completedEntryMask & mask) != 0)
                return false;

            _completedEntryMask |= mask;
            int completeMask = (1 << _entries.Length) - 1;
            if (_completedEntryMask == completeMask)
            {
                _atlasReady = new MapRenderSpotShadowAtlasReadyState(
                    Frame,
                    _entries);
            }
            return true;
        }
    }

    public bool TryGetAtlasReady(
        out MapRenderSpotShadowAtlasReadyState? atlasReady)
    {
        lock (_gate)
        {
            atlasReady = _atlasReady;
            return atlasReady is not null;
        }
    }
}

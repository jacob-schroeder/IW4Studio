using IW4.Render.Scheduling.Dpvs;

namespace IW4.Render.Scheduling;

/// <summary>
/// Immutable current-view inputs consumed by the PS3 Event 0x0E world-surface
/// classifier. These are dynamic DPVS results, not authored surface flags or
/// scene-light shadow-map allocation bits.
/// </summary>
public sealed class MapRenderWorldSurfaceVisibilityState
{
    private readonly uint[]? _includedSurfaceBits;
    private readonly uint[]? _pageZeroMembershipBits1;
    private readonly uint[]? _pageZeroMembershipBits2;
    private readonly MapRenderWorldDpvsViewVisibility? _cameraView;
    private readonly MapRenderWorldDpvsViewVisibility? _partition0View;
    private readonly MapRenderWorldDpvsViewVisibility? _partition1View;
    private IReadOnlyList<uint>? _includedSurfaceBitView;
    private IReadOnlyList<uint>? _pageZeroMembershipBitView1;
    private IReadOnlyList<uint>? _pageZeroMembershipBitView2;

    public MapRenderWorldSurfaceVisibilityState(
        ReadOnlySpan<uint> includedSurfaceBits,
        ReadOnlySpan<uint> pageZeroMembershipBits1,
        ReadOnlySpan<uint> pageZeroMembershipBits2,
        int surfaceCount)
    {
        if (surfaceCount < 0)
            throw new ArgumentOutOfRangeException(nameof(surfaceCount));

        int requiredWordCount = WordCount(surfaceCount);
        ValidateCoverage(
            includedSurfaceBits,
            requiredWordCount,
            nameof(includedSurfaceBits));
        ValidateCoverage(
            pageZeroMembershipBits1,
            requiredWordCount,
            nameof(pageZeroMembershipBits1));
        ValidateCoverage(
            pageZeroMembershipBits2,
            requiredWordCount,
            nameof(pageZeroMembershipBits2));

        _includedSurfaceBits =
            includedSurfaceBits[..requiredWordCount].ToArray();
        _pageZeroMembershipBits1 =
            pageZeroMembershipBits1[..requiredWordCount].ToArray();
        _pageZeroMembershipBits2 =
            pageZeroMembershipBits2[..requiredWordCount].ToArray();
        SurfaceCount = surfaceCount;
    }

    /// <summary>
    /// Retains the immutable three-view owners instead of copying their
    /// already-frozen bitsets again at frame-publication time.
    /// </summary>
    internal MapRenderWorldSurfaceVisibilityState(
        MapRenderWorldDpvsViewVisibility camera,
        MapRenderWorldDpvsViewVisibility sunShadowPartition0,
        MapRenderWorldDpvsViewVisibility sunShadowPartition1)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(sunShadowPartition0);
        ArgumentNullException.ThrowIfNull(sunShadowPartition1);
        ValidateRole(camera, MapRenderWorldDpvsViewIndex.Camera);
        ValidateRole(
            sunShadowPartition0,
            MapRenderWorldDpvsViewIndex.SunShadowPartition0);
        ValidateRole(
            sunShadowPartition1,
            MapRenderWorldDpvsViewIndex.SunShadowPartition1);
        if (camera.SurfaceCount != sunShadowPartition0.SurfaceCount ||
            camera.SurfaceCount != sunShadowPartition1.SurfaceCount)
        {
            throw new ArgumentException(
                "All three DPVS views must describe the same world-surface population.");
        }

        _cameraView = camera;
        _partition0View = sunShadowPartition0;
        _partition1View = sunShadowPartition1;
        SurfaceCount = camera.SurfaceCount;
    }

    public int SurfaceCount { get; }

    /// <summary>DPVS-static view 0. A clear bit excludes the surface.</summary>
    public IReadOnlyList<uint> IncludedSurfaceBits =>
        GetOrCreateView(
            _cameraView,
            _includedSurfaceBits,
            ref _includedSurfaceBitView);

    /// <summary>DPVS-static view 1. A set bit selects Event20 page 0.</summary>
    public IReadOnlyList<uint> PageZeroMembershipBits1 =>
        GetOrCreateView(
            _partition0View,
            _pageZeroMembershipBits1,
            ref _pageZeroMembershipBitView1);

    /// <summary>DPVS-static view 2. A set bit selects Event20 page 0.</summary>
    public IReadOnlyList<uint> PageZeroMembershipBits2 =>
        GetOrCreateView(
            _partition1View,
            _pageZeroMembershipBits2,
            ref _pageZeroMembershipBitView2);

    public MapRenderWorldSurfacePageMembership Classify(int surfaceIndex)
    {
        if ((uint)surfaceIndex >= (uint)SurfaceCount)
            throw new ArgumentOutOfRangeException(nameof(surfaceIndex));

        if (!TestMsbFirstBit(IncludedSurfaceBitSpan, surfaceIndex))
            return MapRenderWorldSurfacePageMembership.Excluded;

        return TestMsbFirstBit(
                   PageZeroMembershipBitSpan1,
                   surfaceIndex) ||
               TestMsbFirstBit(
                   PageZeroMembershipBitSpan2,
                   surfaceIndex)
            ? MapRenderWorldSurfacePageMembership.PageZero
            : MapRenderWorldSurfacePageMembership.PageOne;
    }

    private ReadOnlySpan<uint> IncludedSurfaceBitSpan =>
        _cameraView is not null
            ? _cameraView.SurfaceBitSpan
            : _includedSurfaceBits;

    private ReadOnlySpan<uint> PageZeroMembershipBitSpan1 =>
        _partition0View is not null
            ? _partition0View.SurfaceBitSpan
            : _pageZeroMembershipBits1;

    private ReadOnlySpan<uint> PageZeroMembershipBitSpan2 =>
        _partition1View is not null
            ? _partition1View.SurfaceBitSpan
            : _pageZeroMembershipBits2;

    private static bool TestMsbFirstBit(
        ReadOnlySpan<uint> words,
        int surfaceIndex)
    {
        uint mask = 0x8000_0000u >> (surfaceIndex & 31);
        return (words[surfaceIndex >> 5] & mask) != 0;
    }

    private static int WordCount(int count) =>
        checked((int)(((long)count + 31) / 32));

    private static void ValidateCoverage(
        ReadOnlySpan<uint> words,
        int requiredWordCount,
        string parameterName)
    {
        if (words.Length < requiredWordCount)
        {
            throw new ArgumentException(
                "Bit storage does not cover every world surface.",
                parameterName);
        }
    }

    private static void ValidateRole(
        MapRenderWorldDpvsViewVisibility view,
        MapRenderWorldDpvsViewIndex expected)
    {
        if (view.ViewIndex != expected)
        {
            throw new ArgumentException(
                $"Expected DPVS view {expected}, received {view.ViewIndex}.");
        }
    }

    private static IReadOnlyList<uint> GetOrCreateView(
        MapRenderWorldDpvsViewVisibility? owner,
        uint[]? words,
        ref IReadOnlyList<uint>? storage)
    {
        IReadOnlyList<uint>? view = Volatile.Read(ref storage);
        if (view is not null)
            return view;

        IReadOnlyList<uint> created = owner is not null
            ? owner.SurfaceBits
            : Array.AsReadOnly(
                words ??
                throw new InvalidOperationException(
                    "Visibility state retained neither an immutable view owner nor copied words."));
        return Interlocked.CompareExchange(
                ref storage,
                created,
                comparand: null) ??
            created;
    }
}

namespace IW4.Render.Scheduling.Dpvs;

public sealed class MapRenderWorldDpvsViewVisibility
{
    private readonly uint[] _surfaceBits;
    private readonly uint[] _staticModelBits;
    private IReadOnlyList<uint>? _surfaceBitView;
    private IReadOnlyList<uint>? _staticModelBitView;

    internal MapRenderWorldDpvsViewVisibility(
        MapRenderWorldDpvsViewIndex viewIndex,
        uint[] surfaceBits,
        uint[] staticModelBits,
        int surfaceCount,
        int staticModelCount)
    {
        ArgumentNullException.ThrowIfNull(surfaceBits);
        ArgumentNullException.ThrowIfNull(staticModelBits);
        if (!Enum.IsDefined(viewIndex))
            throw new ArgumentOutOfRangeException(nameof(viewIndex));
        if (surfaceCount < 0)
            throw new ArgumentOutOfRangeException(nameof(surfaceCount));
        if (staticModelCount < 0)
            throw new ArgumentOutOfRangeException(nameof(staticModelCount));
        int requiredSurfaceWords = WordCount(surfaceCount);
        int requiredStaticModelWords = WordCount(staticModelCount);
        if (surfaceBits.Length < requiredSurfaceWords)
        {
            throw new ArgumentException(
                "Surface visibility storage does not cover every surface.",
                nameof(surfaceBits));
        }
        if (staticModelBits.Length < requiredStaticModelWords)
        {
            throw new ArgumentException(
                "Static-model visibility storage does not cover every instance.",
                nameof(staticModelBits));
        }

        ViewIndex = viewIndex;
        SurfaceCount = surfaceCount;
        StaticModelCount = staticModelCount;
        _surfaceBits = surfaceBits[..requiredSurfaceWords].ToArray();
        _staticModelBits = staticModelBits[..requiredStaticModelWords].ToArray();
    }

    public MapRenderWorldDpvsViewIndex ViewIndex { get; }

    public int SurfaceCount { get; }

    public int StaticModelCount { get; }

    public IReadOnlyList<uint> SurfaceBits =>
        GetOrCreateReadOnlyView(
            _surfaceBits,
            ref _surfaceBitView);

    public IReadOnlyList<uint> StaticModelBits =>
        GetOrCreateReadOnlyView(
            _staticModelBits,
            ref _staticModelBitView);

    internal ReadOnlySpan<uint> SurfaceBitSpan => _surfaceBits;

    internal ReadOnlySpan<uint> StaticModelBitSpan => _staticModelBits;

    /// <summary>
    /// Completes a camera view before publication by folding in the disjoint
    /// camera-sky lane. This is intentionally not a general mutation API:
    /// callers own the just-produced static-cull result exclusively, and the
    /// resulting view becomes immutable before it crosses the worker/render
    /// handoff.
    /// </summary>
    internal void MergeCameraSkyBeforePublication(
        MapRenderWorldDpvsCameraSkyVisibility sky)
    {
        ArgumentNullException.ThrowIfNull(sky);
        if (ViewIndex != MapRenderWorldDpvsViewIndex.Camera ||
            SurfaceCount != sky.SurfaceCount ||
            _surfaceBits.Length != sky.SurfaceBitSpan.Length)
        {
            throw new InvalidOperationException(
                "Only a cardinality-matched unpublished camera view can absorb camera-sky visibility.");
        }

        ReadOnlySpan<uint> skyBits = sky.SurfaceBitSpan;
        for (int wordIndex = 0;
             wordIndex < _surfaceBits.Length;
             wordIndex++)
        {
            _surfaceBits[wordIndex] |= skyBits[wordIndex];
        }
    }

    private static int WordCount(int count) =>
        checked((int)(((long)count + 31) / 32));

    private static IReadOnlyList<uint> GetOrCreateReadOnlyView(
        uint[] words,
        ref IReadOnlyList<uint>? storage)
    {
        IReadOnlyList<uint>? view = Volatile.Read(ref storage);
        if (view is not null)
            return view;

        IReadOnlyList<uint> created = Array.AsReadOnly(words);
        return Interlocked.CompareExchange(
                ref storage,
                created,
                comparand: null) ??
            created;
    }
}

namespace IW4.Render.Scheduling.Dpvs;

public sealed class MapRenderWorldDpvsCameraSkyVisibility
{
    private readonly uint[] _surfaceBits;
    private IReadOnlyList<uint>? _surfaceBitView;

    internal MapRenderWorldDpvsCameraSkyVisibility(
        uint[] surfaceBits,
        int surfaceCount)
    {
        _surfaceBits = (uint[])surfaceBits.Clone();
        SurfaceCount = surfaceCount;
    }

    public IReadOnlyList<uint> SurfaceBits
    {
        get
        {
            IReadOnlyList<uint>? view =
                Volatile.Read(ref _surfaceBitView);
            if (view is not null)
                return view;

            IReadOnlyList<uint> created =
                Array.AsReadOnly(_surfaceBits);
            return Interlocked.CompareExchange(
                    ref _surfaceBitView,
                    created,
                    comparand: null) ??
                created;
        }
    }

    public int SurfaceCount { get; }

    internal ReadOnlySpan<uint> SurfaceBitSpan => _surfaceBits;
}

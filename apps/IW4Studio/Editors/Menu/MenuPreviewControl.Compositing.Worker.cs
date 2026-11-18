using Avalonia;
using Avalonia.Threading;
using IW4.Studio.Desktop.Documents.MenuEditing.Preview;
using IW4.Studio.Desktop.Rendering;

namespace IW4.Studio.Desktop.Editors.Menu;

public sealed partial class MenuPreviewControl
{
    private CpuCompositeRequest? _latestCpuComposite;
    private CpuCompositeRequest? _completedCpuComposite;
    private CpuCompositeRequest? _displayedCpuComposite;
    private CpuCompositeRequest? _activeCpuComposite;
    private CancellationTokenSource? _cpuCompositeCancellation;
    private CpuCompositeRaster? _idleCpuCompositeRaster;

    private CpuCompositeRequest RequestCpuComposite(CpuCompositeRequest request)
    {
        if (_latestCpuComposite is { } latest && request.HasSameImage(latest))
            return latest;

        _latestCpuComposite = request;
        if (_displayedCpuComposite is { } displayed && !request.HasSameTarget(displayed))
            ReleaseCpuCompositeBitmap();
        if (!request.HasSameTarget(_completedCpuComposite))
            _completedCpuComposite = null;
        if (_activeCpuComposite is { } active && !request.HasSameTarget(active))
            _cpuCompositeCancellation?.Cancel();

        StartCpuCompositeIfNeeded();
        return request;
    }

    private void StartCpuCompositeIfNeeded()
    {
        if (_activeCpuComposite is not null || !_isAttached ||
            _latestCpuComposite is not { } request || request.HasSameImage(_completedCpuComposite))
        {
            return;
        }

        _activeCpuComposite = request;
        var cancellation = new CancellationTokenSource();
        _cpuCompositeCancellation = cancellation;
        CpuCompositeRaster? reusable = _idleCpuCompositeRaster;
        _idleCpuCompositeRaster = null;
        _ = ComputeCpuCompositeAsync(request, reusable, cancellation);
    }

    private async Task ComputeCpuCompositeAsync(
        CpuCompositeRequest request,
        CpuCompositeRaster? reusable,
        CancellationTokenSource cancellation)
    {
        CpuCompositeRaster? result = null;
        try
        {
            result = await Task.Run(() =>
            {
                CpuCompositeRaster raster = reusable is { } existing &&
                    existing.Width == request.Width && existing.Height == request.Height
                        ? existing
                        : new CpuCompositeRaster(request.Width, request.Height);
                return RasterizeCpuComposite(raster, request, cancellation.Token) ? raster : null;
            }).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException or IndexOutOfRangeException)
        {
            // A malformed preview source falls back to the ordinary drawing path.
        }

        Dispatcher.UIThread.Post(
            () => CompleteCpuComposite(request, result, cancellation),
            DispatcherPriority.Render);
    }

    private void CompleteCpuComposite(
        CpuCompositeRequest request,
        CpuCompositeRaster? result,
        CancellationTokenSource cancellation)
    {
        _activeCpuComposite = null;
        _cpuCompositeCancellation = null;
        bool canPresent = !cancellation.IsCancellationRequested && _isAttached &&
            _latestCpuComposite is { } latest && request.HasSameTarget(latest);
        cancellation.Dispose();

        if (canPresent && result is not null)
        {
            // Publish completed frames even if the animation has moved. The
            // latest request takes the freed worker slot after the UI copy.
            UploadCpuComposite(result);
            _displayedCpuComposite = request;
            _completedCpuComposite = request;
            _idleCpuCompositeRaster = result;
            InvalidateVisual();
        }
        else if (canPresent && request.HasSameImage(_latestCpuComposite))
        {
            ReleaseCpuCompositeBitmap();
            _completedCpuComposite = request;
            InvalidateVisual();
        }

        StartCpuCompositeIfNeeded();
    }

    private void ReleaseCpuCompositeSurface()
    {
        _cpuCompositeCancellation?.Cancel();
        _latestCpuComposite = null;
        _completedCpuComposite = null;
        _idleCpuCompositeRaster = null;
        ReleaseCpuCompositeBitmap();
    }

    private readonly record struct CpuCompositeDraw(
        Rect Bounds,
        MenuPreviewMaterial Material,
        MenuPreviewMaterialSnapshot Snapshot,
        ReadOnlyMemory<byte> Pixels);

    private sealed record CpuCompositeRequest(Rect NativeStage, int Width, int Height, CpuCompositeDraw[] Draws)
    {
        public bool HasSameImage(CpuCompositeRequest? other) =>
            other is not null && Width == other.Width && Height == other.Height &&
            NativeStage == other.NativeStage && Draws.AsSpan().SequenceEqual(other.Draws);

        public bool HasSameTarget(CpuCompositeRequest? other)
        {
            if (other is null || NativeStage != other.NativeStage || Draws.Length != other.Draws.Length)
                return false;

            for (int i = 0; i < Draws.Length; i++)
            {
                CpuCompositeDraw current = Draws[i];
                CpuCompositeDraw previous = other.Draws[i];
                if (current.Material.NodeId != previous.Material.NodeId ||
                    !ReferenceEquals(current.Snapshot, previous.Snapshot) || !current.Pixels.Equals(previous.Pixels))
                {
                    return false;
                }
            }
            return true;
        }
    }
}

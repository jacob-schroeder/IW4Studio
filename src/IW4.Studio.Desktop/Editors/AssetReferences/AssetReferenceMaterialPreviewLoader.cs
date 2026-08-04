using IW4.Studio.Rendering;

namespace IW4.Studio.Desktop.Editors.AssetReferences;

/// <summary>
/// Limits concurrent Material thumbnail work for one reference-picker dialog.
/// It reuses the Menu renderer's canonical texture resolution rather than
/// introducing a second material/image decoding path.
/// </summary>
internal sealed class AssetReferenceMaterialPreviewLoader : IDisposable
{
    private readonly IMenuPreviewMaterialResolver _resolver;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly SemaphoreSlim _slots = new(initialCount: 4, maxCount: 4);
    private bool _disposed;

    public AssetReferenceMaterialPreviewLoader(
        IMenuPreviewMaterialResolver resolver)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    public async Task<AssetReferenceMaterialPreviewLoadResult> LoadAsync(
        string materialName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(materialName);
        CancellationToken cancellationToken = _cancellation.Token;
        if (_disposed)
            return AssetReferenceMaterialPreviewLoadResult.Canceled();

        try
        {
            await _slots.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                MenuPreviewMaterialResolution resolution =
                    await _resolver.ResolveAsync(
                        materialName,
                        cancellationToken).ConfigureAwait(false);
                if (resolution.Snapshot is not { } snapshot)
                {
                    return AssetReferenceMaterialPreviewLoadResult.Failed(
                        resolution.Failure ??
                        "Material preview is unavailable.");
                }

                return AssetReferenceMaterialPreviewLoadResult.Resolved(
                    snapshot.GetPngBytesCopy(),
                    $"{snapshot.ImageName} · " +
                    $"{snapshot.Width:N0} × {snapshot.Height:N0}");
            }
            finally
            {
                _slots.Release();
            }
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
            return AssetReferenceMaterialPreviewLoadResult.Canceled();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return AssetReferenceMaterialPreviewLoadResult.Failed(
                $"Material preview could not be loaded: {exception.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _cancellation.Cancel();
        _cancellation.Dispose();
    }
}

internal sealed record AssetReferenceMaterialPreviewLoadResult(
    byte[]? PngBytes,
    string Message,
    bool IsCanceled)
{
    public static AssetReferenceMaterialPreviewLoadResult Resolved(
        byte[] pngBytes,
        string message) =>
        new(
            pngBytes ?? throw new ArgumentNullException(nameof(pngBytes)),
            message,
            IsCanceled: false);

    public static AssetReferenceMaterialPreviewLoadResult Failed(
        string message) =>
        new(null, message, IsCanceled: false);

    public static AssetReferenceMaterialPreviewLoadResult Canceled() =>
        new(null, string.Empty, IsCanceled: true);
}

namespace IW4.Studio.Rendering;

/// <summary>
/// Presentation-neutral material source used by the Menu preview control.
/// Higher-fidelity backends can implement this contract without entering the
/// Menu document or renderer-neutral scene projection.
/// </summary>
public interface IMenuPreviewMaterialResolver
{
    /// <summary>
    /// Changes whenever resolving the same material names may produce
    /// different results.
    /// </summary>
    long Revision { get; }

    Task<MenuPreviewMaterialResolution> ResolveAsync(
        string materialName,
        CancellationToken cancellationToken = default);
}

using IW4.FastFiles.Zone;

namespace IW4.FastFiles.Emitters.Emission;

/// <summary>One error that prevents a valid asset body from being emitted.</summary>
public sealed record EmissionError(
    string Path,
    string Message,
    int? RowIndex = null,
    XAssetType? AssetType = null,
    XFileBlockType? Block = null)
{
    public override string ToString() =>
        $"{(RowIndex is { } row ? $"row {row}: " : string.Empty)}{Path}: {Message}";
}

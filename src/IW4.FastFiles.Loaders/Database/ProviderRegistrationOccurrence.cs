using IW4.FastFiles.Zone;

namespace IW4.FastFiles.Loaders.Database;

/// <summary>
/// The concrete serialized source that produced one incoming XAsset provider.
/// Source epoch is captured before provider-body loading so TEMP reuse cannot
/// change which source occurrence is emitted to the symbolic linker.
/// </summary>
public readonly record struct ProviderRegistrationOccurrence(
    XBlockAddress SourcePointerCell,
    long SourceEpoch,
    XBlockAddress? InsertProviderCell);

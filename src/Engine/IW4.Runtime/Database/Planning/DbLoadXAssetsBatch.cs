namespace IW4.Runtime.Database.Planning;

/// <summary>
/// One ordered DB_LoadXAssets request batch.
/// </summary>
/// <param name="SynchronizeBefore">
/// The native executable calls DB_SyncXAssets immediately before this batch.
/// This is distinct from <paramref name="Synchronous"/>, which is the
/// synchronous argument passed to DB_LoadXAssets itself.
/// </param>
public sealed record DbLoadXAssetsBatch(
    string Name,
    bool Synchronous,
    IReadOnlyList<DbZonePlanRequest> Requests,
    bool SynchronizeBefore = false);

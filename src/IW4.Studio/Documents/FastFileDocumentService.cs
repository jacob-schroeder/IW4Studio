using IW4.FastFiles.Loaders.Database;
using IW4.FastFiles.Zone;

namespace IW4.Studio.Documents;

/// <summary>
/// Opens one isolated fastfile into an immutable source-layout replay object.
/// </summary>
public sealed class FastFileDocumentService
{
    public FastFileDocumentService()
    {
    }

    public FastFileWorkspace Open(FastFileDocumentOpenRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Mode is not Isolated)
            throw new NotSupportedException("The initial Studio boundary supports isolated opens only.");

        // One direct call deliberately creates one XZone and one frozen
        // ZoneObjectFile for unchanged source-layout replay. Studio retains
        // neither a canonical link request nor draft authoring state.
        var loadSession = new DbLoadSession();
        LoadedXZone loadedZone = loadSession.DB_LoadXZone(
            request.Path,
            XZoneFlags.DB_ZONE_DEV);
        return new FastFileWorkspace(
            new FastFileDocument(request, loadedZone),
            loadSession);
    }
}

using IW4.FastFiles.Loaders.Database;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;

namespace IW4.Studio.Documents;

/// <summary>
/// Opens one isolated fastfile or creates one source-independent blank
/// workspace. Imported runtime state is frozen into linker-owned semantics
/// before the workspace is returned.
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

        var loadSession = new DbLoadSession();
        try
        {
            LoadedXZone loadedZone = loadSession.DB_LoadXZone(
                request.Path,
                XZoneFlags.DB_ZONE_DEV);
            LinkAssetPool assets = loadSession.FreezeLinkAssetPool();
            IReadOnlyList<LinkRoot> roots = loadedZone.FreezeLinkRoots();
            var linkRequest = new ZoneLinkRequest(
                assets,
                roots,
                loadedZone.Header.LanguageMask,
                loadedZone.Header.SelectedLanguageMask);
            return new FastFileWorkspace(
                new FastFileDocument(request, loadedZone, linkRequest),
                loadSession);
        }
        catch
        {
            loadSession.Dispose();
            throw;
        }
    }

    /// <summary>Creates an empty semantic workspace with an exact PS3 language selection.</summary>
    public FastFileWorkspace CreateBlank(
        uint languageMask,
        uint selectedLanguageMask)
    {
        var linkRequest = new ZoneLinkRequest(
            new LinkAssetPool(Array.Empty<LinkAssetProviderSource>()),
            Array.Empty<LinkRoot>(),
            languageMask,
            selectedLanguageMask);
        return new FastFileWorkspace(new FastFileDocument(linkRequest));
    }
}

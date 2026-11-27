using IW4.Game.Database;
using IW4.Loaders.Database;
using IW4.Loaders.Database.Planning;
using IW4.Game.Zone;
using IW4.Linker.Contracts;
using IW4.Linker.Packaging;
using IW4.Runtime.Diagnostics;
using IW4.Runtime.Assets;

namespace IW4.Studio.Documents;

/// <summary>
/// Opens an isolated fastfile or the loader-owned default dependency lifecycle.
/// </summary>
public sealed class FastFileDocumentService
{
    private readonly Action<XAssetLoadProgress>? _assetProgress;

    public FastFileDocumentService()
    {
    }

    public FastFileDocumentService(Action<XAssetLoadProgress>? assetProgress)
    {
        _assetProgress = assetProgress;
    }

    public FastFileWorkspace OpenWithDefaultProfile(string input)
    {
        string path = Path.GetFullPath(input);
        FastFileOpenMode mode;
        try
        {
            mode = new ZonePlan(FastFileOpenProfiles.ResolveForTarget(path));
        }
        catch (NotSupportedException)
        {
            mode = Isolated.Instance;
        }
        return Open(
            new FastFileDocumentOpenRequest(path, mode));
    }

    public FastFileWorkspace Open(FastFileDocumentOpenRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var loadSession = new DbLoadSession(_assetProgress);
        try
        {
            return request.Mode switch
            {
                Isolated => OpenIsolated(request, loadSession),
                ZonePlan plan => OpenDependencies(request, plan, loadSession),
                _ => throw new NotSupportedException("The requested fastfile open mode is not supported.")
            };
        }
        catch
        {
            loadSession.Dispose();
            throw;
        }
    }

    private static FastFileWorkspace OpenIsolated(
        FastFileDocumentOpenRequest request,
        DbLoadSession session)
    {
        LoadedXZone target = session.DB_LoadXZone(request.Path, XZoneFlags.DB_ZONE_DEV);
        var targetZone = new WorkspaceZone(target, request.Path, true, true);
        return new FastFileWorkspace(
            new FastFileDocument(request, targetZone, session.CaptureTargetProviders(target)),
            session,
            [targetZone],
            zonePlanProfileName: null,
            new FastFileDependencyGraph([new FastFileDependencyNode(
                targetZone.PhysicalPath, DbDependencyRequestLoadStatus.Loaded, true)]));
    }

    private static FastFileWorkspace OpenDependencies(
        FastFileDocumentOpenRequest request,
        ZonePlan plan,
        DbLoadSession session)
    {
        string directory = DbZoneCatalog.ResolveDependencyDirectory(request.Path);
        IReadOnlyList<XAssetProviderContribution>? targetProviders = null;
        LinkAssetPool? preparedAssets = null;
        DbDependencyLoadExecution execution = DbDefaultZoneDependencyLoader.Execute(
            session,
            request.Path,
            plan.ProfileName,
            directory,
            DbZoneCatalog.ResolveAdditionalDependencyDirectories(directory),
            target =>
            {
                targetProviders = session.CaptureTargetProviders(target);
                preparedAssets = target.PrepareTargetProviders(targetProviders);
            },
            onlyBeforeFurtherZoneChanges: true);
        WorkspaceZone[] zones = execution.LoadedZones
            .Select(zone => new WorkspaceZone(
                zone.LoadResult, zone.PhysicalPath, zone.IsTarget, zone.IsActive))
            .ToArray();
        var graph = new FastFileDependencyGraph(execution.Requests.Select(request =>
            new FastFileDependencyNode(
                request.PhysicalPath,
                request.Status,
                request.IsTarget)));
        WorkspaceZone target = zones.Single(zone => zone.IsTarget);
        targetProviders ??= session.CaptureTargetProviders(target.LoadResult);
        return new FastFileWorkspace(
            new FastFileDocument(request, target, targetProviders, preparedAssets),
            session,
            zones,
            plan.ProfileName,
            graph);
    }

    /// <summary>Creates an empty semantic workspace with an exact PS3 language selection.</summary>
    public FastFileWorkspace CreateBlank(
        uint languageMask,
        uint selectedLanguageMask)
    {
        DbHeaderAuthoringMetadata headerMetadata =
            DbHeaderAuthoringMetadata.Canonical;
        var linkRequest = new ZoneLinkRequest(
            new LinkAssetPool(Array.Empty<LinkAssetProviderSource>()),
            Array.Empty<LinkRoot>(),
            languageMask,
            selectedLanguageMask,
            scriptStrings: []);
        return new FastFileWorkspace(new FastFileDocument(
            linkRequest,
            headerMetadata));
    }
}

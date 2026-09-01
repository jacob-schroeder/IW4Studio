using IW4.FastFiles.Database;
using IW4.FastFiles.Loaders.Database;
using IW4.FastFiles.Loaders.Database.Planning;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;
using IW4.Runtime.Diagnostics;

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

    public FastFileWorkspace Open(FastFileDocumentOpenRequest request)
    {
        return OpenCore(request, protectSourceIdentity: true);
    }

    /// <summary>
    /// Opens a newly authored output as a real loaded workspace without
    /// protecting that output as an immutable imported source alias.
    /// </summary>
    public FastFileWorkspace OpenAuthoredOutput(FastFileDocumentOpenRequest request)
    {
        return OpenCore(request, protectSourceIdentity: false);
    }

    private FastFileWorkspace OpenCore(
        FastFileDocumentOpenRequest request,
        bool protectSourceIdentity)
    {
        ArgumentNullException.ThrowIfNull(request);
        var loadSession = new DbLoadSession(_assetProgress);
        try
        {
            return request.Mode switch
            {
                Isolated => OpenIsolated(
                    request,
                    loadSession,
                    protectSourceIdentity),
                ZonePlan plan => OpenDependencies(
                    request,
                    plan,
                    loadSession,
                    protectSourceIdentity),
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
        DbLoadSession session,
        bool protectSourceIdentity)
    {
        LoadedXZone target = session.DB_LoadXZone(request.Path, XZoneFlags.DB_ZONE_DEV);
        LinkAssetPool targetAssets = session.FreezeLinkAssetPool(target);
        return CreateWorkspace(
            request,
            session,
            [new WorkspaceZone(target, request.Path, true, true)],
            profileName: null,
            new FastFileDependencyGraph([new FastFileDependencyNode(
                Path.GetFullPath(request.Path), DbDependencyRequestLoadStatus.Loaded, true)]),
            targetAssets,
            protectSourceIdentity);
    }

    private static FastFileWorkspace OpenDependencies(
        FastFileDocumentOpenRequest request,
        ZonePlan plan,
        DbLoadSession session,
        bool protectSourceIdentity)
    {
        string directory = ResolveDependencyDirectory(request.Path);
        DbDependencyLoadExecution execution = DbDefaultZoneDependencyLoader.Execute(
            session,
            request.Path,
            plan.ProfileName,
            directory,
            ResolveAdditionalDependencyDirectories(directory));
        WorkspaceZone[] zones = execution.LoadedZones
            .Select(zone => new WorkspaceZone(
                zone.LoadResult, zone.PhysicalPath, zone.IsTarget, zone.IsActive))
            .ToArray();
        var graph = new FastFileDependencyGraph(execution.Requests.Select(request =>
            new FastFileDependencyNode(
                request.PhysicalPath,
                request.Status,
                request.IsTarget)));
        return CreateWorkspace(
            request,
            session,
            zones,
            plan.ProfileName,
            graph,
            execution.TargetAssets,
            protectSourceIdentity);
    }

    private static FastFileWorkspace CreateWorkspace(
        FastFileDocumentOpenRequest request,
        DbLoadSession session,
        IReadOnlyList<WorkspaceZone> zones,
        string? profileName,
        FastFileDependencyGraph graph,
        LinkAssetPool targetAssets,
        bool protectSourceIdentity)
    {
        ArgumentNullException.ThrowIfNull(targetAssets);
        WorkspaceZone target = zones.Single(zone => zone.IsTarget);
        var linkRequest = new ZoneLinkRequest(
            targetAssets,
            target.LoadResult.FreezeLinkRoots(),
            target.LoadResult.Header.LanguageMask,
            target.LoadResult.Header.SelectedLanguageMask,
            target.LoadResult.XAssetList.ScriptStrings.Select(entry => entry.Value));
        return new FastFileWorkspace(
            new FastFileDocument(
                request,
                target,
                linkRequest,
                protectSourceIdentity),
            session,
            zones,
            profileName,
            graph);
    }

    private static readonly string[] AdditionalDependencyDirectoryNames = ["mappack1", "mappack2"];

    internal static string ResolveDependencyDirectory(string targetPath)
    {
        string containingDirectory = Path.GetDirectoryName(Path.GetFullPath(targetPath))
            ?? throw new InvalidDataException($"Fastfile path '{targetPath}' has no containing directory.");
        return AdditionalDependencyDirectoryNames.Contains(Path.GetFileName(containingDirectory), StringComparer.OrdinalIgnoreCase)
            ? Directory.GetParent(containingDirectory)?.FullName
                ?? throw new InvalidDataException($"Fastfile path '{targetPath}' has no dependency root.")
            : containingDirectory;
    }

    internal static IEnumerable<string> ResolveAdditionalDependencyDirectories(string directory) =>
        AdditionalDependencyDirectoryNames
            .Select(name => Path.Combine(directory, name))
            .Where(Directory.Exists);

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

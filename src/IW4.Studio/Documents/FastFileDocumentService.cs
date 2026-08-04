using IW4.FastFiles.Loaders.Database;
using IW4.FastFiles.Loaders.Database.Planning;
using IW4.Runtime.Database;
using IW4.Runtime.Database.Planning;
using IW4.Runtime.Diagnostics;

namespace IW4.Studio.Documents;

/// <summary>
/// Backend-neutral application service for opening a fastfile document. The
/// Studio-selected mode is dispatched explicitly: isolated opens invoke one
/// DB_LoadXZone call, while named dependency plans are resolved by Runtime and
/// executed by Loaders.
/// </summary>
public sealed class FastFileDocumentService
{
    private static readonly string[] AdditionalDependencyDirectoryNames =
    [
        "mappack1",
        "mappack2"
    ];

    private readonly FastFileDocumentServiceOptions _options;
    private readonly Action<XAssetLoadProgress>? _assetProgress;

    public FastFileDocumentService(
        FastFileDocumentServiceOptions? options = null,
        Action<XAssetLoadProgress>? assetProgress = null)
    {
        _options = options ?? FastFileDocumentServiceOptions.Default;
        _assetProgress = assetProgress;
    }

    public FastFileWorkspace Open(FastFileDocumentOpenRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var session = new DbLoadSession(_assetProgress);

        return request.Mode switch
        {
            Isolated => OpenIsolated(request, session),
            ZonePlan plan => OpenZonePlan(request, plan, session),
            _ => throw new NotSupportedException(
                $"Studio fastfile open mode '{request.Mode.GetType().FullName}' is not supported.")
        };
    }

    private static FastFileWorkspace OpenIsolated(
        FastFileDocumentOpenRequest request,
        DbLoadSession session)
    {
        // Isolated is intentionally a direct load. It is not a dependency
        // plan and must never be rewritten as StructuralSingleZone.
        LoadedXZone target = session.DB_LoadXZone(request.Path);
        TargetZoneSourceSnapshot targetSource = TargetZoneSourceSnapshot.Capture(
            target,
            request.Path);
        return CreateWorkspace(
            request,
            session,
            [target],
            [Path.GetFullPath(request.Path)],
            target,
            targetSource,
            zonePlanProfileName: null,
            dependencyGraph: new FastFileDependencyGraph(
            [
                new FastFileDependencyNode(
                    request.Path,
                    FastFileDependencyLoadStatus.Loaded,
                    isTarget: true)
            ]));
    }

    private FastFileWorkspace OpenZonePlan(
        FastFileDocumentOpenRequest request,
        ZonePlan selectedPlan,
        DbLoadSession session)
    {
        if (!string.Equals(
                selectedPlan.ProfileName,
                FastFileOpenProfiles.DefaultMp,
                StringComparison.Ordinal) &&
            !string.Equals(
                selectedPlan.ProfileName,
                FastFileOpenProfiles.DefaultSp,
                StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"Studio dependency profile '{selectedPlan.ProfileName}' is not available. " +
                $"Supported profiles are '{FastFileOpenProfiles.DefaultMp}' and " +
                $"'{FastFileOpenProfiles.DefaultSp}'.");
        }

        string dependencyDirectory = ResolveDependencyDirectory(request.Path);
        var catalog = new DbZoneCatalog(
            dependencyDirectory,
            ResolveAdditionalDependencyDirectories(dependencyDirectory));
        DbZoneLoadPlan activePlan = selectedPlan.ProfileName switch
        {
            FastFileOpenProfiles.DefaultMp => new DefaultMpZoneLoadPlanner(catalog).Build(
                request.Path,
                DbZonePlanScope.StableRuntime),
            FastFileOpenProfiles.DefaultSp => new DefaultSpZoneLoadPlanner(catalog).Build(
                request.Path,
                DbZonePlanScope.StableRuntime),
            _ => throw new InvalidOperationException(
                "The dependency profile was validated before plan dispatch.")
        };
        TargetZoneSourceSnapshot? targetSource = null;
        DbZonePlanExecutionResult execution =
            DbLoadPlanExecutor.Execute(
                activePlan,
                session,
                (planRequest, loadedZone) =>
                {
                    if (!planRequest.IsTarget)
                        return;
                    if (targetSource is not null)
                    {
                        throw new InvalidDataException(
                            "The dependency plan produced more than one physical target load.");
                    }

                    targetSource = TargetZoneSourceSnapshot.Capture(
                        loadedZone,
                        planRequest.FastFilePath
                            ?? throw new InvalidDataException(
                                "The target dependency-plan request has no physical fastfile path."));
                });

        DbZonePlanRequest[] loadedRequests = activePlan.RequestsInScope
            .Where(planRequest => planRequest.IsLoad && planRequest.FileExists)
            .ToArray();
        if (loadedRequests.Length != execution.LoadedZones.Count)
        {
            throw new InvalidDataException(
                "The dependency-plan execution did not preserve one physical source path " +
                "for every loaded zone.");
        }

        string[] physicalPaths = loadedRequests
            .Select(planRequest => planRequest.FastFilePath
                ?? throw new InvalidDataException(
                    $"Load request '{planRequest.ZoneInfo.Name}' has no fastfile path."))
            .ToArray();
        return CreateWorkspace(
            request,
            session,
            execution.LoadedZones,
            physicalPaths,
            execution.Target,
            targetSource ?? throw new InvalidDataException(
                "The dependency plan completed without capturing its target source snapshot."),
            selectedPlan.ProfileName,
            CreateDependencyGraph(activePlan));
    }

    private string ResolveDependencyDirectory(string targetPath)
    {
        if (_options.DependencyDirectory is not null)
            return _options.DependencyDirectory;

        string containingDirectory =
            Path.GetDirectoryName(Path.GetFullPath(targetPath))
            ?? throw new InvalidDataException(
                $"Fastfile path '{targetPath}' has no containing directory.");

        if (!AdditionalDependencyDirectoryNames.Contains(
                Path.GetFileName(containingDirectory),
                StringComparer.OrdinalIgnoreCase))
        {
            return containingDirectory;
        }

        return Directory.GetParent(containingDirectory)?.FullName
            ?? throw new InvalidDataException(
                $"Map-pack fastfile path '{targetPath}' has no dependency root.");
    }

    private static IEnumerable<string> ResolveAdditionalDependencyDirectories(
        string dependencyDirectory) =>
        AdditionalDependencyDirectoryNames
            .Select(name => Path.Combine(dependencyDirectory, name))
            .Where(Directory.Exists);

    private static FastFileWorkspace CreateWorkspace(
        FastFileDocumentOpenRequest request,
        DbLoadSession session,
        IReadOnlyList<LoadedXZone> loadedZones,
        IReadOnlyList<string> physicalPaths,
        LoadedXZone target,
        TargetZoneSourceSnapshot targetSource,
        string? zonePlanProfileName,
        FastFileDependencyGraph dependencyGraph)
    {
        if (loadedZones.Count != physicalPaths.Count)
        {
            throw new ArgumentException(
                "Every loaded zone must retain one physical source path.",
                nameof(physicalPaths));
        }
        ArgumentNullException.ThrowIfNull(targetSource);

        int targetIndex = loadedZones
            .Select((loadedZone, index) => (LoadedZone: loadedZone, Index: index))
            .Single(entry => ReferenceEquals(entry.LoadedZone, target))
            .Index;
        string targetPhysicalPath = Path.GetFullPath(physicalPaths[targetIndex]);
        if (!string.Equals(
                targetSource.PhysicalPath,
                targetPhysicalPath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The captured target source snapshot does not match the target physical path.");
        }

        HashSet<DbZoneHandle> activeHandles = session.ActiveZones
            .Select(zone => zone.Handle)
            .ToHashSet();
        WorkspaceZone[] workspaceZones = loadedZones
            .Select((loadedZone, index) => new WorkspaceZone(
                loadedZone,
                physicalPaths[index],
                ReferenceEquals(loadedZone, target),
                activeHandles.Contains(loadedZone.Context.ZoneOwner)))
            .ToArray();
        WorkspaceZone targetZone = workspaceZones.Single(
            zone => zone.IsTarget);
        var document = new FastFileDocument(request, targetZone, targetSource);

        return new FastFileWorkspace(
            document,
            session.Runtime,
            workspaceZones,
            zonePlanProfileName,
            dependencyGraph);
    }

    private static FastFileDependencyGraph CreateDependencyGraph(
        DbZoneLoadPlan plan) =>
        new(plan.RequestsInScope
            .Where(request => request.IsLoad)
            .Select(request => new FastFileDependencyNode(
                request.FastFilePath
                    ?? throw new InvalidDataException(
                        $"Load request '{request.ZoneInfo.Name}' has no fastfile path."),
                request.FileExists
                    ? FastFileDependencyLoadStatus.Loaded
                    : request.MissingIsNonFatal
                        ? FastFileDependencyLoadStatus.SkippedOptional
                        : FastFileDependencyLoadStatus.MissingRequired,
                request.IsTarget)));
}

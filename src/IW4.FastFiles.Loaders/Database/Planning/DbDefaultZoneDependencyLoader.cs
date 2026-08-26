using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;
using IW4.Runtime.Database.Planning;

namespace IW4.FastFiles.Loaders.Database.Planning;

/// <summary>Loader-owned entry point for the engine's default zone lifecycles.</summary>
public static class DbDefaultZoneDependencyLoader
{
    public const string DefaultMpProfile = "default_mp";
    public const string DefaultSpProfile = "default_sp";
    private const int Ps3VertexShaderAssetLimit = 1024;

    public static string ResolveProfile(string targetNameOrPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetNameOrPath);
        if (DefaultMpZoneLoadPlanner.SupportsTarget(targetNameOrPath))
            return DefaultMpProfile;
        if (DefaultSpZoneLoadPlanner.SupportsTarget(targetNameOrPath))
            return DefaultSpProfile;

        string targetName = DbZoneCatalog.NormalizeZoneName(targetNameOrPath);
        throw new NotSupportedException(
            $"Zone '{targetName}' does not match the default multiplayer or single-player dependency lifecycle. Open it in isolation.");
    }

    public static DbDependencyLoadExecution Execute(
        DbLoadSession session,
        string targetPath,
        string profileName,
        string dependencyDirectory,
        IEnumerable<string> additionalDependencyDirectories)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(dependencyDirectory);
        ArgumentNullException.ThrowIfNull(additionalDependencyDirectories);

        var catalog = new DbZoneCatalog(
            dependencyDirectory,
            additionalDependencyDirectories);
        DbZoneLoadPlan plan = profileName switch
        {
            DefaultMpProfile => new DefaultMpZoneLoadPlanner(catalog).Build(
                targetPath, DbZonePlanScope.StableRuntime),
            DefaultSpProfile => new DefaultSpZoneLoadPlanner(catalog).Build(
                targetPath, DbZonePlanScope.StableRuntime),
            _ => throw new NotSupportedException(
                $"Studio dependency profile '{profileName}' is not available.")
        };
        LinkAssetPool? targetAssets = null;
        DbZonePlanExecutionResult execution = DbLoadPlanExecutor.Execute(
            plan,
            session,
            (request, loaded) =>
            {
                if (request.IsTarget)
                    targetAssets = session.FreezeLinkAssetPool(loaded);
            });
        LinkAssetPool frozenTargetAssets = targetAssets ?? throw new InvalidDataException(
            "The dependency plan completed without freezing its target providers.");
        DbZonePlanRequest[] loadedRequests = plan.RequestsInScope
            .Where(request => request.IsLoad && request.FileExists)
            .ToArray();
        if (loadedRequests.Length != execution.LoadedZones.Count)
        {
            throw new InvalidDataException(
                "The dependency-plan execution did not preserve one physical source path for every loaded zone.");
        }

        var activeOwners = session.ActiveZones
            .Select(zone => zone.Handle)
            .ToArray();
        DbDependencyLoadedZone[] zones = execution.LoadedZones
            .Select((loaded, index) => new DbDependencyLoadedZone(
                loaded,
                loadedRequests[index].FastFilePath
                    ?? throw new InvalidDataException("A loaded dependency request has no physical path."),
                ReferenceEquals(loaded, execution.Target),
                activeOwners.Contains(loaded.Context.ZoneOwner)))
            .ToArray();
        return new DbDependencyLoadExecution(
            Array.AsReadOnly(zones),
            frozenTargetAssets,
            Array.AsReadOnly(plan.RequestsInScope
                .Where(request => request.IsLoad)
                .Select(request => new DbDependencyRequestStatus(
                    request.FastFilePath
                        ?? throw new InvalidDataException("A dependency request has no physical path."),
                    request.FileExists
                        ? DbDependencyRequestLoadStatus.Loaded
                        : request.MissingIsNonFatal
                            ? DbDependencyRequestLoadStatus.SkippedOptional
                            : throw new InvalidDataException(
                                "A completed dependency plan contains a missing required request."),
                    request.IsTarget))
                .ToArray()));
    }

    /// <summary>
    /// Fresh-loads a staged multiplayer candidate through the engine's stable
    /// default lifecycle while enforcing the native PS3 vertex-shader pool at
    /// every completed zone-load boundary.
    /// </summary>
    public static LoadedXZone LoadDefaultMpCandidateForValidation(
        DbLoadSession session,
        string targetZoneName,
        string candidatePath,
        string dependencyDirectory,
        IEnumerable<string> additionalDependencyDirectories)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetZoneName);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidatePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(dependencyDirectory);
        ArgumentNullException.ThrowIfNull(additionalDependencyDirectories);

        var catalog = new DbZoneCatalog(
            dependencyDirectory,
            additionalDependencyDirectories);
        DbZoneLoadPlan plan = new DefaultMpZoneLoadPlanner(catalog)
            .BuildWithTargetOverride(
                targetZoneName,
                candidatePath,
                DbZonePlanScope.StableRuntime);
        DbZonePlanExecutionResult execution = DbLoadPlanExecutor.Execute(
            plan,
            session,
            (_, loaded) => ValidatePs3VertexShaderCapacity(
                session,
                loaded.Zone.Name));
        return execution.Target;
    }

    public static void ValidatePs3VertexShaderCapacity(
        DbLoadSession session,
        string loadedZoneName)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(loadedZoneName);

        int fullVertexShaderCount = session.AssetPool.Slots
            .SelectMany(slot => slot.Providers)
            .Count(provider =>
                provider.AssetType == XAssetType.VertexShader &&
                !provider.IsReferencePlaceholder);
        if (fullVertexShaderCount <= Ps3VertexShaderAssetLimit)
            return;

        throw new InvalidDataException(
            $"PS3 vertex-shader asset capacity was exceeded after loading zone " +
            $"'{loadedZoneName}': {fullVertexShaderCount} full providers are resident; " +
            $"the limit is {Ps3VertexShaderAssetLimit}.");
    }
}

public enum DbDependencyRequestLoadStatus { Loaded, SkippedOptional }

public sealed record DbDependencyRequestStatus(
    string PhysicalPath,
    DbDependencyRequestLoadStatus Status,
    bool IsTarget);

public sealed record DbDependencyLoadedZone(
    LoadedXZone LoadResult,
    string PhysicalPath,
    bool IsTarget,
    bool IsActive);

public sealed record DbDependencyLoadExecution(
    IReadOnlyList<DbDependencyLoadedZone> LoadedZones,
    LinkAssetPool TargetAssets,
    IReadOnlyList<DbDependencyRequestStatus> Requests);

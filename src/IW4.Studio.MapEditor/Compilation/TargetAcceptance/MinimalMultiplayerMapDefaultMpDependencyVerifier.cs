using System.Collections.ObjectModel;
using System.Security.Cryptography;
using IW4.Assets.Assets.GfxMap;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.RawFile;
using IW4.Assets.Assets.StringTable;
using IW4.Assets.Assets.TechniqueSet;
using IW4.FastFiles.Emitters.Assets;
using IW4.FastFiles.Emitters.Linking;
using IW4.FastFiles.Loaders.Database;
using IW4.FastFiles.Loaders.Database.Planning;
using IW4.FastFiles.Zone;
using IW4.Runtime.Assets;
using IW4.Runtime.Database;
using IW4.Runtime.Database.Planning;
using IW4.Studio.Documents;
using IW4.Studio.MapEditor.Compilation.Lighting;

namespace IW4.Studio.MapEditor.Compilation.TargetAcceptance;

public enum MinimalMultiplayerMapDefaultMpDependencyAuthority
{
    ManagedDefaultMpDependencyPlanEvidenceOnly = 0
}

/// <summary>
/// Evidence that the staged generated zone reopens through the real Default
/// MP lifecycle and resolves its external Material to the official common_mp
/// full definition. It is not retail-target authority.
/// </summary>
public sealed class MinimalMultiplayerMapDefaultMpDependencyEvidence
{
    private readonly IReadOnlyList<string> _loadedZoneNames;
    private readonly IReadOnlyList<string> _activeZoneNames;
    private readonly IReadOnlyList<ZoneAssetKey>
        _resolvedTransitiveAssets;

    internal MinimalMultiplayerMapDefaultMpDependencyEvidence(
        MinimalMultiplayerMapPreAcceptanceFastFileArtifact sourceArtifact,
        IEnumerable<string> loadedZoneNames,
        IEnumerable<string> activeZoneNames,
        IEnumerable<ZoneAssetKey> resolvedTransitiveAssets)
    {
        SourceArtifact = sourceArtifact ??
            throw new ArgumentNullException(nameof(sourceArtifact));
        ArgumentNullException.ThrowIfNull(loadedZoneNames);
        ArgumentNullException.ThrowIfNull(resolvedTransitiveAssets);
        _loadedZoneNames =
            new ReadOnlyCollection<string>(
                loadedZoneNames.ToArray());
        ArgumentNullException.ThrowIfNull(activeZoneNames);
        _activeZoneNames =
            new ReadOnlyCollection<string>(
                activeZoneNames.ToArray());
        _resolvedTransitiveAssets =
            new ReadOnlyCollection<ZoneAssetKey>(
                resolvedTransitiveAssets.ToArray());
    }

    public MinimalMultiplayerMapDefaultMpDependencyAuthority Authority =>
        MinimalMultiplayerMapDefaultMpDependencyAuthority
            .ManagedDefaultMpDependencyPlanEvidenceOnly;

    public bool DefaultMpDependencyPlanAccepted => true;

    public bool ExternalMaterialFullProviderResolved => true;

    public bool RequiredProbePassMaterialGraphResolved => true;

    public bool GeneratedSurfaceMaterialResolved => true;

    public bool GameplayModelSupportResolved =>
        SourceArtifact
            .SourceCompilation
            .RuntimeSupport
            .GameplayModelSupportCompiled;

    public bool TargetConsumerAccepted => false;

    public bool PersistenceAuthorized => false;

    public MinimalMultiplayerMapPreAcceptanceFastFileArtifact
        SourceArtifact { get; }

    public IReadOnlyList<string> LoadedZoneNames => _loadedZoneNames;

    public IReadOnlyList<string> ActiveZoneNames => _activeZoneNames;

    public IReadOnlyList<ZoneAssetKey> ResolvedTransitiveAssets =>
        _resolvedTransitiveAssets;
}

/// <summary>
/// Validates the generated candidate against an immutable official corpus.
/// The candidate must already be staged outside the corpus directory under a
/// non-destructive path.
/// </summary>
public static class MinimalMultiplayerMapDefaultMpDependencyVerifier
{
    public static MinimalMultiplayerMapDefaultMpDependencyEvidence Verify(
        MinimalMultiplayerMapPreAcceptanceFastFileArtifact artifact,
        string dependencyDirectory,
        string stagedCandidatePath)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentException.ThrowIfNullOrWhiteSpace(dependencyDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(stagedCandidatePath);

        string dependencyRoot =
            Path.GetFullPath(dependencyDirectory);
        string stagedPath =
            Path.GetFullPath(stagedCandidatePath);
        if (!Directory.Exists(dependencyRoot))
        {
            throw new DirectoryNotFoundException(
                $"Default MP dependency directory '{dependencyRoot}' " +
                "does not exist.");
        }
        if (!File.Exists(stagedPath))
        {
            throw new FileNotFoundException(
                "The staged target candidate does not exist.",
                stagedPath);
        }

        var catalog = new DbZoneCatalog(dependencyRoot);
        string officialTargetPath =
            Path.GetFullPath(
                catalog.ExpectedPath(artifact.TargetZoneName));
        if (string.Equals(
                stagedPath,
                officialTargetPath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The pre-acceptance candidate must not overwrite the " +
                "official corpus target.",
                nameof(stagedCandidatePath));
        }

        GfxWorldTargetMaterialDependencyEvidence dependency =
            artifact
                .SourceCompilation
                .MaterialResolution
                .Dependency;
        RequireFileDigest(
            stagedPath,
            artifact.FastFileSha256,
            "staged candidate");
        RequireFileDigest(
            catalog.ExpectedPath(dependency.ProviderZoneName),
            dependency.ProviderFastFileSha256.Value,
            dependency.ProviderZoneName);
        RequireFileDigest(
            catalog.ExpectedPath(dependency.ConsumerEvidenceZoneName),
            dependency.ConsumerEvidenceFastFileSha256.Value,
            dependency.ConsumerEvidenceZoneName);

        DbZoneLoadPlan plan =
            new DefaultMpZoneLoadPlanner(catalog)
                .BuildWithTargetOverride(
                    artifact.TargetZoneName,
                    stagedPath,
                    DbZonePlanScope.StableRuntime);
        RequireDependencyOrder(
            plan,
            dependency.ProviderZoneName,
            artifact.TargetZoneName);

        var session = new DbLoadSession(
            selectedLanguageMask: 1);
        LoadedXZone? providerZone = null;
        var loadedZoneNames = new List<string>();
        DbZonePlanExecutionResult execution =
            DbLoadPlanExecutor.Execute(
                plan,
                session,
                (request, loaded) =>
                {
                    loadedZoneNames.Add(
                        request.ZoneInfo.Name ??
                        throw new InvalidDataException(
                            "A loaded Default MP request has no zone name."));
                    if (string.Equals(
                            request.ZoneInfo.Name,
                            dependency.ProviderZoneName,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        providerZone = loaded;
                    }
                });
        if (providerZone is null)
        {
            throw new InvalidDataException(
                "The Default MP plan did not load the required common " +
                "material provider.");
        }
        RequireStableRuntimeZones(
            session,
            execution,
            artifact.TargetZoneName);
        RequireNoManagedLoadWarnings(execution);
        DbZoneHandle[] activeZoneOwners =
            session.ActiveZones
                .Select(value => value.Handle)
                .ToArray();

        XAssetSlot materialSlot =
            RequireActiveFullProvider(
                session,
                dependency.AssetKey,
                activeZoneOwners);
        XAssetProviderContribution[] fullProviders =
            materialSlot.Providers
                .Where(value => !value.IsReferencePlaceholder)
                .ToArray();
        XAssetProviderContribution[] placeholders =
            materialSlot.Providers
                .Where(value => value.IsReferencePlaceholder)
                .ToArray();
        if (materialSlot.ActiveProvider.Owner !=
                providerZone.Context.ZoneOwner ||
            fullProviders.Length != 1 ||
            fullProviders[0].Owner != providerZone.Context.ZoneOwner ||
            placeholders.Length != 1 ||
            placeholders[0].Owner !=
                execution.Target.Context.ZoneOwner)
        {
            throw new InvalidDataException(
                "The target Material did not retain one active common_mp " +
                "full provider and one inactive generated-target external " +
                "placeholder. " +
                $"Expected owner {providerZone.Context.ZoneOwner}; active " +
                $"owner {materialSlot.ActiveProvider.Owner}; providers [" +
                string.Join(
                    ", ",
                    materialSlot.Providers.Select(value =>
                        $"{value.Owner}/placeholder=" +
                        $"{value.IsReferencePlaceholder}")) +
                "].");
        }
        if (materialSlot.CanonicalAsset is not
                MaterialAsset material)
        {
            throw new InvalidDataException(
                "The resolved target Material has the wrong runtime type.");
        }

        RequireCompatibleMaterial(material, dependency);
        ZoneAssetKey[] gameplayModelDependencies =
            RequireGameplayModelSupportGraph(
                session,
                execution.Target,
                artifact.SourceCompilation.RuntimeSupport,
                activeZoneOwners);
        ZoneAssetKey[] transitiveAssets =
            dependency.ResolvedTransitiveAssetKeys
                .Select(key =>
                {
                    _ = RequireActiveFullProvider(
                        session,
                        key,
                        activeZoneOwners);
                    return key;
                })
                .Concat(gameplayModelDependencies)
                .Distinct()
                .OrderBy(value => value.Type)
                .ThenBy(
                    value => value.LogicalName,
                    StringComparer.Ordinal)
                .ToArray();
        RequireGeneratedTarget(
            session,
            execution.Target,
            artifact,
            material);

        return new MinimalMultiplayerMapDefaultMpDependencyEvidence(
            artifact,
            loadedZoneNames,
            session.ActiveZones.Select(value => value.Zone.Name),
            transitiveAssets);
    }

    private static ZoneAssetKey[] RequireGameplayModelSupportGraph(
        DbLoadSession session,
        LoadedXZone target,
        MinimalMultiplayerMapRuntimeSupportCompilation runtimeSupport,
        IReadOnlyCollection<DbZoneHandle> activeZoneOwners)
    {
        MapGameplayModelSupportCompilation? gameplay =
            runtimeSupport.GameplayModelSupport;
        if (gameplay is null)
        {
            return [];
        }

        var dependencies = new HashSet<ZoneAssetKey>();
        foreach ((ZoneAssetKey key, IXAssetBuildData buildData) in
                 gameplay.RuntimeDefinitionKeys.Zip(
                     gameplay.RuntimeDefinitionBuildData))
        {
            XAssetSlot definitionSlot =
                RequireActiveFullProvider(
                    session,
                    key,
                    activeZoneOwners);
            XAssetProviderContribution[] fullProviders =
                definitionSlot.Providers
                    .Where(value =>
                        !value.IsReferencePlaceholder)
                    .ToArray();
            XAssetProviderContribution[] targetProviders =
                fullProviders
                    .Where(value =>
                        value.Owner == target.Context.ZoneOwner)
                    .ToArray();
            if (definitionSlot.ActiveProvider.Owner !=
                    target.Context.ZoneOwner ||
                targetProviders.Length != 1)
            {
                throw new InvalidDataException(
                    "An imported gameplay support definition did not resolve " +
                    "to its generated-target full provider. " +
                    $"Asset '{key}', target owner " +
                    $"{target.Context.ZoneOwner}, providers [" +
                    string.Join(
                        ", ",
                        definitionSlot.Providers.Select(value =>
                            $"{value.Owner}/placeholder=" +
                            $"{value.IsReferencePlaceholder}")) +
                    "].");
            }

            IReadOnlyList<ZoneAssetDependency> definitionDependencies =
                ZoneAssetDependencyCollectorRegistry
                    .Default
                    .RequireCollect(buildData);
            if (definitionDependencies.Any(value =>
                    !value.IsExternal))
            {
                throw new InvalidDataException(
                    $"Imported gameplay support definition '{key}' contains " +
                    "a non-external nested dependency.");
            }

            foreach (ZoneAssetDependency dependency in
                     definitionDependencies)
            {
                _ = RequireActiveFullProvider(
                    session,
                    dependency.Target,
                    activeZoneOwners);
                dependencies.Add(dependency.Target);
            }
        }

        return dependencies
            .OrderBy(value => value.Type)
            .ThenBy(
                value => value.LogicalName,
                StringComparer.Ordinal)
            .ToArray();
    }

    private static void RequireStableRuntimeZones(
        DbLoadSession session,
        DbZonePlanExecutionResult execution,
        string targetZoneName)
    {
        string loadZoneName = $"{targetZoneName}_load";
        string[] activeNames =
            session.ActiveZones
                .Select(value => value.Zone.Name)
                .ToArray();
        XZoneFlags retiredFlags =
            XZoneFlags.DB_ZONE_UI |
            XZoneFlags.DB_ZONE_LOAD |
            XZoneFlags.DB_ZONE_DEV;
        if (session.ActiveZones.Any(value =>
                (value.Zone.Flags & retiredFlags) !=
                    XZoneFlags.None) ||
            activeNames.Any(value =>
                string.Equals(
                    value,
                    loadZoneName,
                    StringComparison.OrdinalIgnoreCase)) ||
            !activeNames.Any(value =>
                string.Equals(
                    value,
                    targetZoneName,
                    StringComparison.OrdinalIgnoreCase)) ||
            !activeNames.Any(value =>
                string.Equals(
                    value,
                    "common_mp",
                    StringComparison.OrdinalIgnoreCase)) ||
            !session.ActiveZones.Any(value =>
                value.Handle == execution.Target.Context.ZoneOwner))
        {
            throw new InvalidDataException(
                "The generated target did not reach the stable Default MP " +
                "runtime registry after UI/game and load/dev retirement. " +
                $"Active zones: [{string.Join(", ", activeNames)}].");
        }
    }

    private static void RequireNoManagedLoadWarnings(
        DbZonePlanExecutionResult execution)
    {
        string[] warnings =
            execution.LoadedZones
                .SelectMany(zone =>
                    zone.Warnings.Select(warning =>
                        $"{zone.Zone.Name}: {warning}"))
                .ToArray();
        if (warnings.Length != 0)
        {
            throw new InvalidDataException(
                "The Default MP dependency lifecycle produced managed " +
                $"loader warnings: {string.Join("; ", warnings)}");
        }
    }

    private static void RequireGeneratedTarget(
        DbLoadSession session,
        LoadedXZone target,
        MinimalMultiplayerMapPreAcceptanceFastFileArtifact artifact,
        MaterialAsset material)
    {
        MinimalMultiplayerMapManagedRoundTripVerifier
            .RequireResolvedTargetGraph(
                artifact.SourceCompilation.Candidate,
                artifact.SourceCompilation.RuntimeSupport,
                session,
                target,
                material);
    }

    private static void RequireCompatibleMaterial(
        MaterialAsset material,
        GfxWorldTargetMaterialDependencyEvidence dependency)
    {
        string semanticDigest =
            RelocationInvariantAssetSemanticDigest
                .ComputeLoadedMaterial(material);
        MaterialTechniqueSetAsset? techniqueSet =
            material.TechniqueSet;
        MaterialTechniqueAsset? primary =
            Technique(
                techniqueSet,
                dependency.PrimaryTechnique.Slot);
        MaterialTechniqueAsset? dynamicFog =
            Technique(
                techniqueSet,
                dependency.DynamicFogTechnique.Slot);
        bool exact =
            string.Equals(
                semanticDigest,
                dependency.MaterialSemanticSha256.Value,
                StringComparison.Ordinal) &&
            string.Equals(
                material.Info.Name,
                dependency.AssetKey.LogicalName,
                StringComparison.Ordinal) &&
            material.Info.SortKey == dependency.SortKey &&
            material.CameraRegion == dependency.CameraRegion &&
            material.Info.GameFlags == dependency.GameFlags &&
            material.StateFlags == dependency.StateFlags &&
            techniqueSet is not null &&
            string.Equals(
                techniqueSet.Name,
                dependency.TechniqueSetName,
                StringComparison.Ordinal) &&
            techniqueSet.WorldVertexFormat ==
                dependency.WorldVertexFormat &&
            TechniqueMatches(
                primary,
                dependency.PrimaryTechnique) &&
            TechniqueMatches(
                dynamicFog,
                dependency.DynamicFogTechnique) &&
            material.Textures.Count == 1 &&
            material.Textures[0].NameHash ==
                dependency.MaterialSamplerNameHash &&
            material.Textures[0].Semantic ==
                dependency.MaterialSamplerSemantic &&
            string.Equals(
                material.Textures[0].Image?.Name,
                dependency.MaterialSamplerImageName,
                StringComparison.Ordinal) &&
            TechniqueStateMatches(
                material,
                dependency.PrimaryTechnique.Slot,
                dependency.StateLoadBits) &&
            TechniqueStateMatches(
                material,
                dependency.DynamicFogTechnique.Slot,
                dependency.StateLoadBits);
        if (!exact)
        {
            throw new InvalidDataException(
                "The active common_mp Material graph or its detached " +
                "relocation-invariant semantic digest differs from the " +
                "immutable target compatibility evidence.");
        }
    }

    private static MaterialTechniqueAsset? Technique(
        MaterialTechniqueSetAsset? techniqueSet,
        int slot) =>
        techniqueSet?.TechniqueSlots
            .SingleOrDefault(value => value.Index == slot)
            ?.Technique;

    private static bool TechniqueMatches(
        MaterialTechniqueAsset? technique,
        GfxWorldTargetMaterialTechniqueEvidence evidence)
    {
        if (technique is null ||
            !string.Equals(
                technique.Name,
                evidence.TechniqueName,
                StringComparison.Ordinal) ||
            technique.Flags != evidence.Flags ||
            technique.PassCount != evidence.PassCount ||
            technique.Passes.Count != evidence.PassCount)
        {
            return false;
        }

        var pass = technique.Passes[0];
        return string.Equals(
                   pass.VertexShader?.Name,
                   evidence.VertexShaderName,
                   StringComparison.Ordinal) &&
               string.Equals(
                   pass.PixelShader?.Name,
                   evidence.PixelShaderName,
                   StringComparison.Ordinal) &&
               pass.VertexDeclaration is { } declaration &&
               declaration.StreamCount ==
                   evidence.VertexRoutes.Count &&
               declaration.Routing
                   .Take(declaration.StreamCount)
                   .SequenceEqual(evidence.VertexRoutes);
    }

    private static bool TechniqueStateMatches(
        MaterialAsset material,
        int techniqueSlot,
        IReadOnlyList<uint> expectedLoadBits)
    {
        if ((uint)techniqueSlot >=
                (uint)material.StateBitsEntries.Count)
        {
            return false;
        }

        int stateBitsIndex =
            material.StateBitsEntries[techniqueSlot]
                .StateBitsIndex;
        return (uint)stateBitsIndex <
                   (uint)material.StateBits.Count &&
               material.StateBits[stateBitsIndex]
                   .LoadBits
                   .SequenceEqual(expectedLoadBits);
    }

    private static XAssetSlot RequireActiveFullProvider(
        DbLoadSession session,
        ZoneAssetKey key,
        IReadOnlyCollection<DbZoneHandle> activeZoneOwners)
    {
        XAssetSlot[] matches =
            session.AssetPool.Slots
                .Where(value =>
                    value.AssetType == key.Type &&
                    string.Equals(
                        value.Name,
                        key.LogicalName,
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();
        if (matches.Length != 1 ||
            matches[0].ActiveProvider.IsReferencePlaceholder ||
            !activeZoneOwners.Contains(
                matches[0].ActiveProvider.Owner))
        {
            throw new InvalidDataException(
                "Stable Default MP did not resolve one active full " +
                $"provider owned by a resident zone for '{key}'.");
        }
        return matches[0];
    }

    private static void RequireDependencyOrder(
        DbZoneLoadPlan plan,
        string providerZoneName,
        string targetZoneName)
    {
        string[] names =
            plan.RequestsInScope
                .Where(value => value.IsLoad && value.FileExists)
                .Select(value => value.ZoneInfo.Name!)
                .ToArray();
        int provider = Array.FindIndex(
            names,
            value => string.Equals(
                value,
                providerZoneName,
                StringComparison.OrdinalIgnoreCase));
        int target = Array.FindIndex(
            names,
            value => string.Equals(
                value,
                targetZoneName,
                StringComparison.OrdinalIgnoreCase));
        if (provider < 0 || target < 0 || provider >= target)
        {
            throw new InvalidDataException(
                "The Default MP plan does not load the target Material " +
                "provider before the generated map.");
        }
    }

    private static void RequireFileDigest(
        string path,
        string expectedSha256,
        string label)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Required {label} FastFile does not exist.",
                path);
        }
        using FileStream stream = File.OpenRead(path);
        string actual = Convert.ToHexString(SHA256.HashData(stream))
            .ToLowerInvariant();
        if (!string.Equals(
                actual,
                expectedSha256,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"{label} SHA-256 '{actual}' does not match the required " +
                $"evidence '{expectedSha256}'.");
        }
    }
}

/// <summary>
/// Non-destructive staging helper for the bounded pre-acceptance artifact.
/// Existing files are never overwritten.
/// </summary>
public static class MinimalMultiplayerMapPreAcceptanceArtifactStager
{
    public static void StageNewForManagedDependencyVerification(
        MinimalMultiplayerMapPreAcceptanceFastFileArtifact artifact,
        string protectedDependencyDirectory,
        string destinationPath)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            protectedDependencyDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        string path = Path.GetFullPath(destinationPath);
        string protectedRoot =
            Path.GetFullPath(protectedDependencyDirectory);
        if (!Directory.Exists(protectedRoot))
        {
            throw new DirectoryNotFoundException(
                $"Protected dependency directory '{protectedRoot}' does " +
                "not exist.");
        }
        string? directory = Path.GetDirectoryName(path);
        if (directory is null || !Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException(
                $"Artifact staging directory '{directory}' does not exist.");
        }
        RequirePhysicalDirectoryPath(
            protectedRoot,
            nameof(protectedDependencyDirectory));
        RequirePhysicalDirectoryPath(
            directory,
            nameof(destinationPath));
        if (IsInsideOrEqual(path, protectedRoot))
        {
            throw new ArgumentException(
                "A generated pre-acceptance artifact cannot be staged " +
                "inside the protected official dependency corpus.",
                nameof(destinationPath));
        }

        byte[] bytes = artifact.GetFastFileBytesCopy();
        using (var stream = new FileStream(
                   path,
                   FileMode.CreateNew,
                   FileAccess.Write,
                   FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }

        try
        {
            using FileStream stream = File.OpenRead(path);
            string sha256 =
                Convert.ToHexString(SHA256.HashData(stream))
                    .ToLowerInvariant();
            if (stream.Length != artifact.FastFileByteLength ||
                !string.Equals(
                    sha256,
                    artifact.FastFileSha256,
                    StringComparison.Ordinal))
            {
                throw new IOException(
                    "The staged target artifact failed its length or " +
                    "SHA-256 verification.");
            }
        }
        catch
        {
            File.Delete(path);
            throw;
        }
    }

    private static bool IsInsideOrEqual(
        string candidatePath,
        string directoryPath)
    {
        string root =
            Path.TrimEndingDirectorySeparator(directoryPath);
        string candidate =
            Path.TrimEndingDirectorySeparator(candidatePath);
        return string.Equals(
                   candidate,
                   root,
                   StringComparison.OrdinalIgnoreCase) ||
               candidate.StartsWith(
                   root + Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static void RequirePhysicalDirectoryPath(
        string directoryPath,
        string parameterName)
    {
        for (DirectoryInfo? cursor = new(directoryPath);
             cursor is not null;
             cursor = cursor.Parent)
        {
            if (cursor.LinkTarget is not null ||
                (cursor.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new ArgumentException(
                    "Artifact staging paths cannot traverse symbolic-link " +
                    "or reparse-point directories.",
                    parameterName);
            }
        }
    }
}

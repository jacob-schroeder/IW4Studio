using IW4.FastFiles.Database;
using IW4.FastFiles.Database.Streaming;
using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Assets;
using IW4.FastFiles.Emitters.Linking;
using IW4.FastFiles.Emitters.Packaging;

namespace IW4.Studio.Documents;

public sealed record NewZoneDependencyImpact(
    ZoneAssetKey Owner,
    string OwnerPath,
    bool IsRequired);

/// <summary>Stable inbound-edge preview shown before deletion or conversion
/// of an owned target to an external reference.</summary>
public sealed class NewZoneDependencyImpactPreview
{
    private readonly IReadOnlyList<NewZoneDependencyImpact> _impacts;

    internal NewZoneDependencyImpactPreview(
        ZoneAssetKey target,
        ZoneAssetReferenceIntent currentIntent,
        IEnumerable<NewZoneDependencyImpact> impacts)
    {
        Target = target;
        CurrentIntent = currentIntent;
        _impacts = Array.AsReadOnly(impacts
            .Distinct()
            .OrderBy(impact => impact.Owner.Type)
            .ThenBy(impact => impact.Owner.LogicalName, StringComparer.Ordinal)
            .ThenBy(impact => impact.OwnerPath, StringComparer.Ordinal)
            .ThenByDescending(impact => impact.IsRequired)
            .ToArray());
    }

    public ZoneAssetKey Target { get; }

    public ZoneAssetReferenceIntent CurrentIntent { get; }

    public IReadOnlyList<NewZoneDependencyImpact> Impacts => _impacts;

    public bool HasRequiredInboundDependencies => _impacts.Any(impact => impact.IsRequired);

    public bool CanRemoveWithoutExternalizing => !HasRequiredInboundDependencies;
}

/// <summary>
/// Mutable source-independent authoring surface for a new zone. It contains
/// only detached build data, logical dependencies, script strings, and
/// explicit output/container policies.
/// </summary>
public sealed class NewZoneDocument
{
    private sealed record DocumentEntry(
        ZoneAssetReferenceIntent Intent,
        IXAssetBuildData? BuildData,
        IReadOnlyList<ZoneAssetDependency> Dependencies);

    private readonly ZoneBuildGraphBuilder _graph = new();
    private readonly Dictionary<ZoneAssetKey, DocumentEntry> _entries = [];
    private readonly IReadOnlyList<string?> _scriptStrings;

    public NewZoneDocument(
        GreenfieldContainerPolicy? containerPolicy = null,
        IEnumerable<string?>? scriptStrings = null,
        ZoneLinkOutputPolicy? outputPolicy = null,
        ZoneLinkLayoutPolicy? layoutPolicy = null)
    {
        ContainerPolicy = containerPolicy ?? GreenfieldContainerPolicy.Canonical;
        OutputPolicy = outputPolicy ?? ZoneLinkOutputPolicy.Canonical;
        LayoutPolicy = layoutPolicy ?? ZoneLinkLayoutPolicy.Canonical;
        _scriptStrings = Array.AsReadOnly((scriptStrings ?? []).ToArray());

        if (OutputPolicy.PreferImportedOrder ||
            OutputPolicy.PreserveImportedAssetOrder ||
            OutputPolicy.PreserveImportedScriptStringOrder ||
            !OutputPolicy.RequireDeterministicPackageMetadata)
        {
            throw new ArgumentException(
                "A new-zone document requires source-independent ordering and deterministic package metadata.",
                nameof(outputPolicy));
        }
    }

    public GreenfieldContainerPolicy ContainerPolicy { get; }

    public ZoneLinkOutputPolicy OutputPolicy { get; }

    public ZoneLinkLayoutPolicy LayoutPolicy { get; }

    public int Count => _entries.Count;

    public NewZoneDocument AddOwned(
        ZoneAssetKey key,
        IXAssetBuildData buildData,
        IEnumerable<ZoneAssetDependency>? dependencies = null)
    {
        ArgumentNullException.ThrowIfNull(buildData);
        EnsureNewKey(key);
        EnsureSidecarPolicyAllowsOwned(key, buildData);
        ZoneAssetDependency[] copiedDependencies = CopyDependencies(dependencies);

        _graph.AddOwned(key, buildData, copiedDependencies);
        _entries.Add(
            key,
            new DocumentEntry(
                ZoneAssetReferenceIntent.Owned,
                buildData,
                Array.AsReadOnly(copiedDependencies)));
        return this;
    }

    public NewZoneDocument AddExternal(ZoneAssetKey key)
    {
        EnsureNewKey(key);
        _graph.AddExternal(key);
        _entries.Add(
            key,
            new DocumentEntry(
                ZoneAssetReferenceIntent.External,
                null,
                Array.Empty<ZoneAssetDependency>()));
        return this;
    }

    public NewZoneDocument Replace(
        ZoneAssetKey key,
        IXAssetBuildData buildData,
        IEnumerable<ZoneAssetDependency>? dependencies = null)
    {
        ArgumentNullException.ThrowIfNull(buildData);
        if (!_entries.TryGetValue(key, out DocumentEntry? existing))
            throw new KeyNotFoundException($"Cannot replace missing new-zone entry '{key}'.");
        EnsureSidecarPolicyAllowsOwned(key, buildData);
        ZoneAssetDependency[] copiedDependencies = dependencies is null
            ? existing.Dependencies.ToArray()
            : CopyDependencies(dependencies);

        _graph.ReplaceOwned(key, buildData, copiedDependencies);
        _entries[key] = new DocumentEntry(
            ZoneAssetReferenceIntent.Owned,
            buildData,
            Array.AsReadOnly(copiedDependencies));
        return this;
    }

    public NewZoneDependencyImpactPreview PreviewDependencyImpact(ZoneAssetKey target)
    {
        if (!_entries.TryGetValue(target, out DocumentEntry? targetEntry))
            throw new KeyNotFoundException($"Cannot preview missing new-zone entry '{target}'.");

        NewZoneDependencyImpact[] impacts = _entries
            .SelectMany(pair => pair.Value.Dependencies
                .Where(dependency => dependency.Target == target)
                .Select(dependency => new NewZoneDependencyImpact(
                    pair.Key,
                    string.IsNullOrWhiteSpace(dependency.OwnerPath)
                        ? "<unspecified>"
                        : dependency.OwnerPath!,
                    dependency.IsRequired)))
            .ToArray();
        return new NewZoneDependencyImpactPreview(
            target,
            targetEntry.Intent,
            impacts);
    }

    public NewZoneDocument Remove(
        ZoneAssetKey key,
        ZoneRemoveDanglingPolicy policy = ZoneRemoveDanglingPolicy.Reject)
    {
        if (!Enum.IsDefined(policy))
            throw new ArgumentOutOfRangeException(nameof(policy));

        NewZoneDependencyImpactPreview preview = PreviewDependencyImpact(key);
        _graph.Remove(key, policy);

        if (preview.HasRequiredInboundDependencies &&
            policy == ZoneRemoveDanglingPolicy.ReplaceTargetWithExternal)
        {
            _entries[key] = new DocumentEntry(
                ZoneAssetReferenceIntent.External,
                null,
                Array.Empty<ZoneAssetDependency>());
        }
        else
        {
            _entries.Remove(key);
        }
        return this;
    }

    public bool Contains(ZoneAssetKey key) => _entries.ContainsKey(key);

    public ZoneLinkRequest FreezeRequest() =>
        _graph.Freeze(_scriptStrings, OutputPolicy, LayoutPolicy);

    public DbHeader CreateEnvelope() =>
        GreenfieldEnvelopeFactory.Create(ContainerPolicy);

    public DbHeader CreateEnvelope(
        IEnumerable<DbHeaderImageStreamEntry>
            selectedLanguageImageStreamEntries) =>
        GreenfieldEnvelopeFactory.Create(
            ContainerPolicy,
            selectedLanguageImageStreamEntries);

    private static ZoneAssetDependency[] CopyDependencies(
        IEnumerable<ZoneAssetDependency>? dependencies) =>
        (dependencies ?? [])
        .Select(dependency => dependency ??
            throw new ArgumentException("New-zone dependencies cannot contain null.", nameof(dependencies)))
        .ToArray();

    private void EnsureNewKey(ZoneAssetKey key)
    {
        if (_entries.ContainsKey(key))
            throw new InvalidDataException($"A new-zone entry with logical key '{key}' already exists.");
    }

    private void EnsureSidecarPolicyAllowsOwned(
        ZoneAssetKey key,
        IXAssetBuildData buildData)
    {
        if (ContainerPolicy.SidecarPolicy == GreenfieldSidecarPolicy.Disallow &&
            buildData is IGfxImageBuildData image &&
            image.StreamData.Any(value => value.HasStreamingData))
        {
            throw new InvalidDataException(
                $"Owned greenfield asset '{key}' contains streamed image data, but the container policy disallows sidecar output.");
        }
    }
}

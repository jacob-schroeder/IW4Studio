using System.Collections.ObjectModel;
using IW4.Studio.Documents;
using IW4.Studio.MapEditor.Compilation.Bundles;

namespace IW4.Studio.MapEditor.Compilation.Persistence;

/// <summary>
/// Describes whether a Studio draft row participates in the compiled-map
/// authority graph. Only unrelated rows are currently safe to compose with
/// narrow Map Editor patches.
/// </summary>
internal enum CompiledMapDraftScope
{
    Unrelated,
    AssetOwner,
    ResolvedDependency
}

internal sealed record StudioDraftCompositionClassification(
    AssetRowChange Change,
    CompiledMapDraftScope Scope,
    IReadOnlyList<string> Authorities)
{
    public bool CanCompose =>
        Scope == CompiledMapDraftScope.Unrelated;
}

/// <summary>
/// Fail-closed composition policy for shared Studio drafts. Reopened
/// candidate validation proves staged fidelity, but it cannot prove that an
/// arbitrary map-owner or resolved-dependency mutation preserves derived BSP
/// data. Those rows remain owned by explicit Map Editor capabilities.
/// </summary>
internal static class StudioDraftCompositionPolicy
{
    public static IReadOnlyList<StudioDraftCompositionClassification>
        Classify(
            CompiledMapBundle bundle,
            AssetChangeSet changeSet)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(changeSet);

        return new ReadOnlyCollection<
            StudioDraftCompositionClassification>(
            changeSet.Changes
                .Select(change => Classify(bundle, change))
                .ToArray());
    }

    public static IReadOnlyList<string> Validate(
        CompiledMapBundle bundle,
        FastFileEditingSaveSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.DocumentId != bundle.SourceDocumentId)
        {
            return
            [
                "The Studio draft snapshot belongs to a different target " +
                "document than the compiled-map authority."
            ];
        }

        return Classify(bundle, snapshot.ChangeSet)
            .Where(result => !result.CanCompose)
            .Select(FormatDiagnostic)
            .ToArray();
    }

    private static StudioDraftCompositionClassification Classify(
        CompiledMapBundle bundle,
        AssetRowChange change)
    {
        string[] ownerAuthorities = bundle.Assets
            .Where(asset => asset.OwnerRow == change.RowIdentity)
            .Select(asset =>
                $"{asset.Kind} owner '{asset.AssetName}'")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (ownerAuthorities.Length != 0)
        {
            return new StudioDraftCompositionClassification(
                change,
                CompiledMapDraftScope.AssetOwner,
                Array.AsReadOnly(ownerAuthorities));
        }

        string[] dependencyAuthorities = bundle.Dependencies
            .Where(dependency =>
                dependency.IsResolved &&
                dependency.TargetSourceOrdinal ==
                    change.RowIdentity.SerializedIndex)
            .Select(dependency =>
                $"{dependency.OwnerAssetType} '{dependency.OwnerAssetName}' " +
                $"{dependency.OwnerPath} -> {dependency.TargetAssetType} " +
                $"'{dependency.TargetAssetName}'")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (dependencyAuthorities.Length != 0)
        {
            return new StudioDraftCompositionClassification(
                change,
                CompiledMapDraftScope.ResolvedDependency,
                Array.AsReadOnly(dependencyAuthorities));
        }

        return new StudioDraftCompositionClassification(
            change,
            CompiledMapDraftScope.Unrelated,
            Array.Empty<string>());
    }

    private static string FormatDiagnostic(
        StudioDraftCompositionClassification classification)
    {
        string scope = classification.Scope switch
        {
            CompiledMapDraftScope.AssetOwner =>
                "compiled-map asset owner",
            CompiledMapDraftScope.ResolvedDependency =>
                "resolved compiled-map dependency",
            _ => throw new InvalidOperationException(
                "Only non-composable draft scopes produce diagnostics.")
        };
        AssetRowChange change = classification.Change;
        string identity =
            change.OriginalSerializedName ??
            "(unnamed)";
        return
            $"Studio draft row #{change.RowIdentity.SerializedIndex} " +
            $"({change.SerializedType} '{identity}') is a {scope}: " +
            $"{string.Join(", ", classification.Authorities)}. Arbitrary " +
            "drafts in the compiled-map authority graph cannot be composed " +
            "safely; use an explicit Map Editor capability or revert that " +
            "Studio draft before Map Save As.";
    }
}

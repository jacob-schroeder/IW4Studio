using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;
using IW4.Linker.Linking;
using IW4.Linker.Packaging;
using IW4.Studio.Documents;

namespace MapConverter.Game.IW3.PC.Bootstrap;

internal sealed record Iw4BootstrapXModelGraph(
    IReadOnlyList<XModelAsset> Models,
    LinkAssetPool Providers,
    IReadOnlySet<uint> ReferencedImageFileIndices)
{
    internal IReadOnlySet<AssetKey> ModelKeys { get; } = Models
        .Select(AssetKey.FromDefinition)
        .ToHashSet();
}

/// <summary>
/// Reduces an IW4 PS3 gameplay fastfile to the exact owned XModel closure
/// requested by the conversion. The reduced provider pool can then be merged
/// into a greenfield map without retaining unrelated bootstrap-map assets.
/// </summary>
internal static class Iw4BootstrapXModelLoader
{
    private const uint OutputLanguageMask = 1;

    internal static Iw4BootstrapXModelGraph Load(
        string fastFilePath,
        IReadOnlyList<string> modelNames,
        string scratchDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fastFilePath);
        ArgumentNullException.ThrowIfNull(modelNames);
        ArgumentException.ThrowIfNullOrWhiteSpace(scratchDirectory);
        if (modelNames.Count == 0)
            throw new ArgumentException("At least one bootstrap XModel is required.", nameof(modelNames));

        string[] names = modelNames
            .Select(name => string.IsNullOrWhiteSpace(name)
                ? throw new ArgumentException("Bootstrap XModel names cannot be empty.", nameof(modelNames))
                : name)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (names.Length != modelNames.Count)
            throw new ArgumentException("Bootstrap XModel names must be unique.", nameof(modelNames));

        ReducedBootstrapFastFile reduced = LinkReducedFastFile(fastFilePath, names);
        string reducedPath = Path.Combine(scratchDirectory, "iw4-bootstrap-xmodels.ff");
        File.WriteAllBytes(reducedPath, reduced.Bytes);

        using FastFileWorkspace workspace = new FastFileDocumentService().Open(
            new FastFileDocumentOpenRequest(reducedPath, Isolated.Instance));
        ValidateLanguage(workspace.InitialLinkRequest, reducedPath);
        ValidateModels(
            workspace.InitialLinkRequest,
            reducedPath,
            names,
            requireOwnedRoots: true);

        XModelAsset[] references = names
            .Select(name => new XModelAsset { Name = name })
            .ToArray();
        return new Iw4BootstrapXModelGraph(
            Array.AsReadOnly(references),
            workspace.InitialLinkRequest.Assets,
            reduced.ReferencedImageFileIndices);
    }

    private static ReducedBootstrapFastFile LinkReducedFastFile(
        string fastFilePath,
        IReadOnlyList<string> modelNames)
    {
        FastFileOpenMode mode = new ZonePlan(
            FastFileOpenProfiles.ResolveForTarget(fastFilePath));
        using FastFileWorkspace workspace = new FastFileDocumentService().Open(
            new FastFileDocumentOpenRequest(fastFilePath, mode));
        ValidateLanguage(workspace.InitialLinkRequest, fastFilePath);
        ValidateModels(
            workspace.InitialLinkRequest,
            fastFilePath,
            modelNames,
            requireOwnedRoots: false);

        LinkRoot[] roots = modelNames
            .Select((name, index) =>
            {
                AssetKey key = XModelKey(name);
                LinkAssetProvider provider = workspace.InitialLinkRequest.Assets
                    .Providers.Single(candidate =>
                        candidate.Key == key && !candidate.IsReferencePlaceholder);
                return new LinkRoot(
                    $"mapconverter:bootstrap:xmodel:{index}",
                    XAssetType.XModel,
                    LinkRootIntent.Owned,
                    key,
                    provider.OriginalSerializedName,
                    opaqueHeader: null);
            })
            .ToArray();
        var request = new ZoneLinkRequest(
            workspace.InitialLinkRequest.Assets,
            roots,
            workspace.InitialLinkRequest.LanguageMask,
            workspace.InitialLinkRequest.SelectedLanguageMask,
            workspace.InitialLinkRequest.ScriptStrings);
        ZoneLinkResult link = new ZoneLinker().Link(request);
        if (!link.Succeeded || link.DecodedBytes is not { } decodedBytes)
        {
            throw new InvalidDataException(
                $"IW4 bootstrap XModel link failed: {string.Join("; ", link.Errors)}");
        }

        FastFilePackagingResult package = new FastFilePackager().PackageGreenfield(
            decodedBytes,
            link.LanguageMask,
            link.SelectedLanguageMask,
            link.ImageStreamLanguageTables);
        if (!package.Succeeded || package.Bytes is not { } bytes)
        {
            throw new InvalidDataException(
                "IW4 bootstrap XModel packaging failed: " +
                string.Join("; ", package.Errors.Select(error =>
                    $"{error.Code}: {error.Message}")));
        }
        IReadOnlySet<uint> referencedImageFileIndices = link
            .ImageStreamLanguageTables
            .SelectMany(table => table.ImageStreamEntries)
            .Where(entry => !entry.IsEmpty)
            .Select(entry => entry.FileIndex)
            .ToHashSet();
        return new ReducedBootstrapFastFile(
            bytes.ToArray(),
            referencedImageFileIndices);
    }

    private static void ValidateModels(
        ZoneLinkRequest request,
        string sourcePath,
        IReadOnlyList<string> modelNames,
        bool requireOwnedRoots)
    {
        foreach (string name in modelNames)
        {
            AssetKey key = XModelKey(name);
            int rootCount = request.Roots.Count(root =>
                root.SerializedType == XAssetType.XModel &&
                root.Intent == LinkRootIntent.Owned &&
                root.Asset == key &&
                string.Equals(root.OriginalSerializedName, name, StringComparison.Ordinal));
            int providerCount = request.Assets.Providers.Count(provider =>
                provider.Key == key &&
                provider.SerializedType == XAssetType.XModel &&
                !provider.IsReferencePlaceholder &&
                string.Equals(
                    provider.OriginalSerializedName,
                    name,
                    StringComparison.Ordinal));
            if ((requireOwnedRoots && rootCount != 1) || providerCount != 1)
            {
                throw new InvalidDataException(
                    $"IW4 bootstrap fastfile '{sourcePath}' does not contain " +
                    $"exactly one usable full XModel provider named '{name}'.");
            }
        }
    }

    private static void ValidateLanguage(
        ZoneLinkRequest request,
        string sourcePath)
    {
        if (request.LanguageMask != OutputLanguageMask ||
            request.SelectedLanguageMask != OutputLanguageMask)
        {
            throw new InvalidDataException(
                $"IW4 bootstrap fastfile '{sourcePath}' uses language masks " +
                $"0x{request.LanguageMask:x}/0x{request.SelectedLanguageMask:x}; " +
                $"MapConverter requires 0x{OutputLanguageMask:x}.");
        }
    }

    private static AssetKey XModelKey(string name) => AssetKey.FromWireName(
        CanonicalAssetFamily.FromSerializedType(XAssetType.XModel),
        name);

    private sealed record ReducedBootstrapFastFile(
        byte[] Bytes,
        IReadOnlySet<uint> ReferencedImageFileIndices);
}

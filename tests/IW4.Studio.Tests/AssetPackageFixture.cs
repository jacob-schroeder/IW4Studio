using IW4.Game.Assets;
using IW4.Linker.Contracts;
using IW4.Linker.Linking;
using IW4.Linker.Packaging;
using IW4.Studio.Documents;
using Xunit;

namespace IW4.Studio.Tests;

internal static class AssetPackageFixture
{
    internal static byte[] FastFile(IReadOnlyList<BaseAsset> definitions, IEnumerable<BaseAsset> roots)
    {
        var pool = new LinkAssetPool(definitions.Select(asset => new LinkAssetProviderSource(asset)));
        var rows = roots.Select((asset, index) => new LinkRoot($"row-{index}", asset.SerializedAssetType,
            asset.SerializedAssetName!.StartsWith(',') ? LinkRootIntent.External : LinkRootIntent.Owned,
            AssetKey.FromDefinition(asset), asset.SerializedAssetName, null));
        return FastFile(new ZoneLinkRequest(pool, rows, 1, 1, []));
    }

    internal static byte[] FastFile(ZoneLinkRequest request)
    {
        var linked = new ZoneLinker().Link(ZoneLinker.ProjectDependencyRootOrder(request));
        Assert.True(linked.Succeeded, string.Join(Environment.NewLine, linked.Errors));
        var packed = new FastFilePackager().PackageGreenfield(linked.DecodedBytes!.Value, linked.LanguageMask,
            linked.SelectedLanguageMask, linked.ImageStreamLanguageTables);
        Assert.True(packed.Succeeded, string.Join(Environment.NewLine, packed.Errors));
        return packed.Bytes!.Value.ToArray();
    }

    internal static void Export(string fastFile, string package, string root)
    {
        using var session = new FastFileEditingSession(new FastFileDocumentService().Open(new(fastFile, Isolated.Instance)));
        var preview = session.PreviewAssetPackageExport([session.Document.Rows.Single(row => row.OriginalName == root)]);
        Assert.DoesNotContain(preview.Rows, row => row.Status == AssetPackageStatus.Blocked);
        preview.WriteTo(package);
    }
}

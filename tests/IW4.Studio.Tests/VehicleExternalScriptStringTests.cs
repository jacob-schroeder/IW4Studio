using IW4.Game.Assets.Vehicle;
using IW4.Game.ScriptStrings;
using IW4.Linker.Contracts;
using IW4.Studio.Documents;
using Xunit;

namespace IW4.Studio.Tests;

public sealed class VehicleExternalScriptStringTests
{
    [Theory]
    [InlineData(null, false, false)]
    [InlineData(null, true, false)]
    [InlineData("", true, false)]
    [InlineData("stock-first-entry", true, false)]
    [InlineData("stock-first-entry", true, true)]
    public void External_vehicle_preserves_identical_resolved_tags(
        string? tagText, bool materializedTags, bool predeclareNull)
    {
        var vehicle = new VehicleDefAsset
        {
            Name = ",vehicle_placeholder",
            TrophyTags = materializedTags
                ? Enumerable.Range(0, VehicleDefAsset.ScriptStringCount)
                    .Select(_ => new ScriptStringReference(0, tagText, default, default))
                    .ToArray()
                : Array.Empty<ScriptStringReference>()
        };
        string directory = Path.Combine(Path.GetTempPath(),
            "IW4-VehicleExternal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "vehicle.ff");
            var request = new ZoneLinkRequest(
                new LinkAssetPool([new LinkAssetProviderSource(vehicle)]),
                [new LinkRoot("vehicle", vehicle.SerializedAssetType, LinkRootIntent.External,
                    AssetKey.FromDefinition(vehicle), vehicle.Name, null)],
                1, 1, predeclareNull ? new string?[] { null } : Array.Empty<string?>());
            File.WriteAllBytes(path, AssetPackageFixture.FastFile(request));

            using FastFileWorkspace workspace = new FastFileDocumentService().Open(
                new(path, Isolated.Instance));
            string?[] expectedTable = predeclareNull && tagText is not null
                ? [null, tagText]
                : [tagText];
            Assert.Equal(expectedTable,
                workspace.LoadedZone.XAssetList.ScriptStrings.Select(entry => entry.Value));
            VehicleDefAsset loaded = Assert.IsType<VehicleDefAsset>(
                Assert.Single(workspace.LoadedZone.LoadedAssets).Asset);
            Assert.Equal(VehicleDefAsset.ScriptStringCount, loaded.TrophyTags.Count);
            Assert.All(loaded.TrophyTags, tag =>
            {
                Assert.Equal((ushort)(predeclareNull && tagText is not null ? 1 : 0),
                    tag.RawLocalIndex);
                Assert.Equal(tagText, tag.Text);
                Assert.True(tag.HasResolvedValue);
            });

            string reopenedPath = Path.Combine(directory, "vehicle-relinked.ff");
            File.WriteAllBytes(reopenedPath,
                AssetPackageFixture.FastFile(workspace.InitialLinkRequest));
            using FastFileWorkspace reopened = new FastFileDocumentService().Open(
                new(reopenedPath, Isolated.Instance));
            VehicleDefAsset relinked = Assert.IsType<VehicleDefAsset>(
                Assert.Single(reopened.LoadedZone.LoadedAssets).Asset);
            Assert.All(relinked.TrophyTags, tag => Assert.Equal(tagText, tag.Text));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

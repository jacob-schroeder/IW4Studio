using System.Globalization;
using IW4.FastFiles.Zone;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Workbench.Tools.ZoneDetails;

/// <summary>
/// Read-only projection of the decoded target zone's serialized metadata.
/// </summary>
public sealed class ZoneDetailsToolViewModel
{
    public ZoneDetailsToolViewModel(FastFileWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var loadedZone = workspace.LoadedZone;
        IReadOnlyList<uint> blockSizes = loadedZone.XFile.BlockSizes;

        ZoneName = loadedZone.Zone.Name;
        BlockStreams = Array.AsReadOnly(
            Enumerable.Range(0, XFile.BlockCount)
                .Select(index => new ZoneBlockStreamViewModel(
                    (XFileBlockType)index,
                    index < blockSizes.Count
                        ? blockSizes[index]
                        : 0))
                .ToArray());
        ScriptStrings = Array.AsReadOnly(
            loadedZone.Context.ZoneScriptStrings.Entries
                .Select(value => new ZoneScriptStringViewModel(value.Index, value.Value))
                .ToArray());
        ScriptStringCount = ScriptStrings.Count.ToString("N0", CultureInfo.InvariantCulture);
        AssetCount = loadedZone.XAssetList.Assets.Count.ToString("N0", CultureInfo.InvariantCulture);
    }

    public string ZoneName { get; }

    public IReadOnlyList<ZoneBlockStreamViewModel> BlockStreams { get; }

    public string ScriptStringCount { get; }

    public IReadOnlyList<ZoneScriptStringViewModel> ScriptStrings { get; }

    public string AssetCount { get; }
}

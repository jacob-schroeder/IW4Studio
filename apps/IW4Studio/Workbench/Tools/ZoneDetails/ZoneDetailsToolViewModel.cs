using System.Globalization;
using IW4.Game.Zone;
using IW4.Studio.Documents;
using IW4.Studio.Desktop.ViewModels;

namespace IW4.Studio.Desktop.Workbench.Tools.ZoneDetails;

public sealed class ZoneDetailsToolViewModel : ObservableObject
{
    private readonly FastFileEditingSession _session;

    public ZoneDetailsToolViewModel(FastFileEditingSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        var loadedZone = session.Workspace.LoadedZone;
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
        RefreshScriptStrings(session.LinkRequest.ScriptStrings);
        AssetCount = loadedZone.XAssetList.Assets.Count.ToString("N0", CultureInfo.InvariantCulture);
    }

    public string ZoneName { get; }

    public IReadOnlyList<ZoneBlockStreamViewModel> BlockStreams { get; }

    public string ScriptStringCount => ScriptStrings.Count.ToString("N0", CultureInfo.InvariantCulture);

    public IReadOnlyList<ZoneScriptStringViewModel> ScriptStrings { get; private set; } = [];

    public string AssetCount { get; }

    internal ScriptStringEditSnapshot CaptureScriptStrings() => _session.CaptureScriptStrings();

    internal void ApplyScriptStrings(ScriptStringEditSnapshot snapshot, IReadOnlyList<string?> values)
    {
        bool changed = _session.ApplyScriptStrings(snapshot, values);
        RefreshScriptStrings(changed ? _session.LinkRequest.ScriptStrings : values);
    }

    private void RefreshScriptStrings(IReadOnlyList<string?> values)
    {
        ScriptStrings = values
            .Select((text, index) => new ZoneScriptStringViewModel(index, text))
            .ToArray();
        OnPropertyChanged(nameof(ScriptStrings));
        OnPropertyChanged(nameof(ScriptStringCount));
    }
}

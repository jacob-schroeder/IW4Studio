using IW4.Game.Assets;
using IW4.Game.Assets.RawFile;
using IW4.Game.Assets.XModel;
using IW4.Game.Zone;
using IW4.Linker.Contracts;

namespace IW4.Studio.Documents;

public sealed partial class AssetEditorSession
{
    public bool CanOverrideCharacter => Mode == WorkspaceAssetAccess.ReadOnly &&
        Entry.Origin == WorkspaceAssetOrigin.DependencyOnly && Definition is RawFileAsset { Name: { } name } &&
        _session.ValidateNewAssetName(XAssetType.RawFile, name, allowDependencyOverride: true) is null;

    public string OverrideCharacter()
    {
        ThrowIfClosed();
        if (!CanOverrideCharacter || Definition is not RawFileAsset { Name: { } name } source)
            throw new InvalidOperationException("The character cannot be overridden in this fastfile.");
        _session.AddAsset(source, new RawFileAdapter(), allowDependencyOverride: true);
        return name;
    }

    public void AddCharacterModel(XModelAssemblyCompileResult model)
    {
        RequireEditableCharacterScript();
        if (!model.IsSuccess)
            throw new InvalidDataException(string.Join(" ", model.Issues.Select(issue => issue.Message)));
        _session.AddAsset(model.Definition, new XModelAdapter(), model.Providers);
    }

    public void CopyCharacterAlias(RawFileAsset original, string newPath)
    {
        RequireEditableCharacterScript();
        var copy = new RawFileAsset
        {
            Name = newPath, Len = original.Len, CompressedLen = original.CompressedLen,
            Buffer = original.Buffer?.ToArray()
        };
        _session.AddAsset(copy, new RawFileAdapter());
    }

    private void RequireEditableCharacterScript()
    {
        ThrowIfClosed();
        if (!CanEdit || _adapter.AssetType != XAssetType.RawFile)
            throw new InvalidOperationException("An editable character script is required.");
    }
}

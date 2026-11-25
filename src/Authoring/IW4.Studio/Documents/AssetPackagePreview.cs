using IW4.Game.Assets;
using IW4.Game.Zone;
using IW4.Linker.Contracts;
using IW4.Loaders.Database;
using IW4.Studio.Documents.AssetReferences;

namespace IW4.Studio.Documents;

public enum AssetPackageStatus { Included, Required, Add, Reuse, Conflict, Blocked }
public enum AssetPackageConflictChoice { KeepExisting, Replace }

public sealed record AssetPackageRow(AssetKey? Key, XAssetType Type, string Name,
    AssetPackageStatus Status, string Detail, IReadOnlyList<string> UsedBy, bool CanReplace = false);

public sealed class AssetPackageExportPreview
{
    private readonly byte[]? _archive;

    internal AssetPackageExportPreview(IReadOnlyList<AssetPackageRow> rows, byte[]? bytes)
    {
        Rows = Array.AsReadOnly(rows.ToArray());
        _archive = bytes;
    }

    public IReadOnlyList<AssetPackageRow> Rows { get; }

    public void WriteTo(string path)
    {
        if (_archive is null)
            throw new InvalidOperationException("Resolve the blocked assets before exporting.");
        string destination = Path.GetFullPath(path);
        string temporary = Path.Combine(Path.GetDirectoryName(destination)!, $".{Guid.NewGuid():N}.iw4pkg.tmp");
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                file.Write(_archive);
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}

// A named stream resolver uses the fastfile's physical directory. Keep it alive while
// an import dialog is open and while an editing session owns content from this package.
internal sealed class AssetPackageZone : IDisposable
{
    private readonly DirectoryInfo _directory;
    internal FastFileWorkspace Workspace { get; }
    internal LoadedXZone Zone => Workspace.LoadedZone;
    internal ZoneLinkRequest Request => Workspace.InitialLinkRequest;

    internal AssetPackageZone(byte[] fastFile, byte[]? imagePackage, byte[]? soundPackage)
    {
        _directory = Directory.CreateTempSubdirectory("IW4Studio-package-");
        try
        {
            string path = Path.Combine(_directory.FullName, "zone.ff");
            File.WriteAllBytes(path, fastFile);
            if (imagePackage is not null)
                File.WriteAllBytes(Path.ChangeExtension(path, ".pak"), imagePackage);
            if (soundPackage is not null)
                File.WriteAllBytes(Path.Combine(_directory.FullName, "zone_snd.pak"), soundPackage);
            Workspace = new FastFileDocumentService().Open(new(path, Isolated.Instance));
        }
        catch
        {
            _directory.Delete(true);
            throw;
        }
    }

    public void Dispose()
    {
        Workspace.Dispose();
        _directory.Delete(true);
    }
}

internal sealed record AssetPackageImportItem(AssetPackageRow Row, LinkRoot? Root,
    LinkAssetProvider? Provider, BaseAsset? Definition, WorkspaceAssetCatalogEntry? Destination);

public sealed class AssetPackageImportPreview : IDisposable
{
    private AssetPackageZone? _package;
    internal AssetPackageZone Package => _package ?? throw new ObjectDisposedException(nameof(AssetPackageImportPreview));
    internal Guid DocumentId { get; }
    internal long Revision { get; }
    internal WorkspaceAssetDependencyIndex Dependencies { get; }
    internal IReadOnlyList<AssetPackageImportItem> Items { get; }
    public IReadOnlyList<AssetPackageRow> Rows { get; }

    internal AssetPackageImportPreview(Guid documentId, long revision, AssetPackageZone package,
        IReadOnlyList<AssetPackageImportItem> items, WorkspaceAssetDependencyIndex dependencies)
    {
        DocumentId = documentId;
        Revision = revision;
        _package = package;
        Items = items;
        Dependencies = dependencies;
        Rows = Array.AsReadOnly(items.Select(item => item.Row).ToArray());
    }

    public void Dispose() => Interlocked.Exchange(ref _package, null)?.Dispose();
}

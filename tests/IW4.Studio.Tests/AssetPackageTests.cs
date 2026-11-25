using System.IO.Compression;
using IW4.Game.Assets.Image;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.TechniqueSet;
using IW4.Game.Zone;
using IW4.Linker.Contracts;
using IW4.Game.Assets;
using IW4.Studio.Documents;
using Xunit;

namespace IW4.Studio.Tests;

public sealed class AssetPackageTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("IW4.AssetPackage.");
    private readonly List<AssetPackageImportPreview> _previews = [];
    private static readonly Dictionary<AssetKey, AssetPackageConflictChoice> NoChoices = [];

    [Fact]
    public void Mixed_selection_preserves_null_and_opaque_rows_for_every_serialized_type()
    {
        var opaqueTypes = new[] { XAssetType.UiMap, XAssetType.SndDriverGlobals, XAssetType.AiType,
            XAssetType.MpType, XAssetType.Character, XAssetType.XModelAlias, XAssetType.UiMap };
        var roots = Enum.GetValues<XAssetType>().Where(type => !XAssetTypeDispatchCatalog.IsNativeNoOp(type))
            .Select(type => new LinkRoot($"null-{type}", type, LinkRootIntent.Null, null, null, null))
            .Concat(opaqueTypes.Select((type, index) => new LinkRoot($"opaque-{index}", type,
                LinkRootIntent.OpaqueNative, null, null, 0x12345678))).ToArray();
        string donorPath = FilePath("structural.ff");
        File.WriteAllBytes(donorPath, AssetPackageFixture.FastFile(new(new LinkAssetPool([]), roots, 1, 1, [])));
        using var donor = new FastFileEditingSession(new FastFileDocumentService().Open(new(donorPath, Isolated.Instance)));
        var export = donor.PreviewAssetPackageExport(donor.Document.Rows);
        Assert.All(export.Rows, row => Assert.Equal(AssetPackageStatus.Included, row.Status));
        string package = FilePath("structural.iw4pkg");
        export.WriteTo(package);
        using var target = NewTarget();
        Assert.Equal(roots.Length, target.ImportAssetPackage(Preview(target, package), NoChoices));
        string savedPath = FilePath("structural-received.ff");
        var saved = new TransactionalSaveAsService().SaveAs(target, new(savedPath, false));
        Assert.True(saved.Succeeded, string.Join(Environment.NewLine, saved.Diagnostics));
        using var reopened = new FastFileEditingSession(new FastFileDocumentService().Open(new(savedPath, Isolated.Instance)));
        Assert.Equal(roots.Select(r => (r.SerializedType, r.Intent, r.OpaqueHeader)),
            reopened.Workspace.InitialLinkRequest.Roots.Select(r => (r.SerializedType, r.Intent, r.OpaqueHeader)));
        Assert.Equal(0, reopened.ImportAssetPackage(Preview(reopened, package), NoChoices));
    }

    [Fact]
    public void Export_from_one_session_import_into_another_save_reopen_and_reimport()
    {
        string seed = WritePackage("sign", 42);
        using var source = NewTarget();
        var seedPreview = Preview(source, seed);
        Assert.Equal(3, source.ImportAssetPackage(seedPreview, NoChoices));
        var selected = source.Document.Rows.Single(row => row.AssetType == XAssetType.Material);
        var export = source.PreviewAssetPackageExport([selected]);
        Assert.All(export.Rows, row => Assert.Equal(AssetPackageStatus.Included, row.Status));
        Assert.Equal(3, export.Rows.Count);
        string shared = FilePath("shared.iw4pkg");
        export.WriteTo(shared);

        using var target = NewTarget();
        var preview = Preview(target, shared);
        Assert.All(preview.Rows, row => Assert.Equal(AssetPackageStatus.Add, row.Status));
        long before = target.Revision;
        Assert.Equal(3, target.ImportAssetPackage(preview, NoChoices));
        Assert.Equal(before + 1, target.Revision);
        Assert.Throws<InvalidOperationException>(() => target.ImportAssetPackage(preview, NoChoices));

        string fastfile = FilePath("received.ff");
        SaveAsResult saved = new TransactionalSaveAsService().SaveAs(target, new(fastfile, false));
        Assert.True(saved.Succeeded, string.Join(Environment.NewLine, saved.Diagnostics));
        using var reopened = new FastFileEditingSession(new FastFileDocumentService().Open(new(fastfile, Isolated.Instance)));
        var material = Assert.IsType<MaterialAsset>(Read(reopened, reopened.Document.Rows.Single(row => row.AssetType == XAssetType.Material)));
        Assert.Equal("sign_techset", material.TechniqueSet?.Name);
        Assert.Equal(2, material.Textures.Count);
        Assert.Equal(42, material.Textures[0].Image!.PayloadBytes[0]);
        var repeat = Preview(reopened, shared);
        Assert.All(repeat.Rows, row => Assert.Equal(AssetPackageStatus.Reuse, row.Status));
        Assert.Equal(0, reopened.ImportAssetPackage(repeat, NoChoices));
        Assert.Equal(0, reopened.Revision);
    }

    [Fact]
    public void Shared_image_conflict_requires_a_choice_and_lists_the_existing_material()
    {
        using var target = NewTarget();
        target.ImportAssetPackage(Preview(target, WritePackage("old", 7)), NoChoices);
        string path = WritePackage("new", 99);
        var incoming = Preview(target, path);
        var conflict = Assert.Single(incoming.Rows, row => row.Status == AssetPackageStatus.Conflict);
        Assert.Equal(XAssetType.Image, conflict.Type);
        Assert.Contains(conflict.UsedBy, user => user.Contains("old", StringComparison.Ordinal));
        Assert.Throws<InvalidOperationException>(() => target.ImportAssetPackage(incoming, NoChoices));
        Assert.DoesNotContain(target.Document.Rows, row => row.OriginalName == "new");
        Assert.Equal(1, target.ImportAssetPackage(incoming, new Dictionary<AssetKey, AssetPackageConflictChoice>
            { [conflict.Key!.Value] = AssetPackageConflictChoice.KeepExisting }));
        var kept = Assert.IsType<MaterialAsset>(Read(target, target.Document.Rows.Single(row => row.OriginalName == "new")));
        Assert.Equal(7, kept.Textures[0].Image!.PayloadBytes[0]);

        var replacement = Preview(target, path);
        Assert.Equal(1, target.ImportAssetPackage(replacement, new Dictionary<AssetKey, AssetPackageConflictChoice>
            { [conflict.Key!.Value] = AssetPackageConflictChoice.Replace }));
        Assert.Equal(0, target.ImportAssetPackage(Preview(target, path), NoChoices));
        var existingMaterial = (MaterialAsset)Read(target, target.Document.Rows.Single(row => row.OriginalName == "old"));
        Assert.Equal(99, existingMaterial.Textures[0].Image!.PayloadBytes[0]);
        string ff = FilePath("replacement.ff");
        var saved = new TransactionalSaveAsService().SaveAs(target, new(ff, false));
        Assert.True(saved.Succeeded, string.Join(Environment.NewLine, saved.Diagnostics));
        using var reopened = new FastFileEditingSession(new FastFileDocumentService().Open(new(ff, Isolated.Instance)));
        foreach (var row in reopened.Document.Rows.Where(row => row.AssetType == XAssetType.Material))
            Assert.Equal(99, ((MaterialAsset)Read(reopened, row)).Textures[0].Image!.PayloadBytes[0]);
    }

    [Fact]
    public void Replacement_can_add_a_dependency_to_an_earlier_material_root()
    {
        using var target = NewTarget();
        target.ImportAssetPackage(Preview(target, WritePackage("sign", 7)), NoChoices);
        string path = WritePackage("sign", 9, imageName: "sign/night", vertexFormat: MaterialWorldVertexFormat.MTL_WORLDVERT_TEX_2_NRM_2);
        var preview = Preview(target, path);
        var choices = preview.Rows.Where(row => row.Status == AssetPackageStatus.Conflict)
            .ToDictionary(row => row.Key!.Value, row => AssetPackageConflictChoice.Replace);
        Assert.Equal(2, choices.Count);
        Assert.Equal(3, target.ImportAssetPackage(preview, choices));
        var set = Assert.IsType<MaterialTechniqueSetAsset>(Read(target, target.Document.Rows.Single(row => row.AssetType == XAssetType.Techset)));
        Assert.Equal(MaterialWorldVertexFormat.MTL_WORLDVERT_TEX_2_NRM_2, set.WorldVertexFormat);
        string fastfile = FilePath("new-dependency.ff");
        var saved = new TransactionalSaveAsService().SaveAs(target, new(fastfile, false));
        Assert.True(saved.Succeeded, string.Join(Environment.NewLine, saved.Diagnostics));
        using var reopened = new FastFileEditingSession(new FastFileDocumentService().Open(new(fastfile, Isolated.Instance)));
        Assert.Equal(0, reopened.ImportAssetPackage(Preview(reopened, path), NoChoices));
    }

    [Fact]
    public void Missing_required_asset_blocks_the_whole_import()
    {
        using var target = NewTarget();
        var preview = Preview(target, WritePackage("sign", 5, externalTechset: true));
        Assert.Equal(XAssetType.Techset, Assert.Single(preview.Rows, row => row.Status == AssetPackageStatus.Blocked).Type);
        Assert.Throws<InvalidOperationException>(() => target.ImportAssetPackage(preview, NoChoices));
        Assert.Empty(target.Document.Rows);
        Assert.Equal(0, target.Revision);
    }

    [Fact]
    public void Required_techset_resolves_in_the_receiving_fastfile()
    {
        using var target = NewTarget();
        target.ImportAssetPackage(Preview(target, WritePackage("base", 5)), NoChoices);
        var overlay = Preview(target, WritePackage("overlay", 5, externalTechset: true));
        Assert.Equal(AssetPackageStatus.Reuse, Assert.Single(overlay.Rows, row => row.Type == XAssetType.Techset).Status);
        Assert.Equal(1, target.ImportAssetPackage(overlay, NoChoices));
        Assert.Equal(4, target.Document.Rows.Count);
    }

    [Fact]
    public void Manifest_roots_are_identified_by_key_independently_of_asset_order()
    {
        string path = WritePackage("sign", 7);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
        using (var stream = zip.GetEntry("manifest.json")!.Open())
        {
            var manifest = System.Text.Json.Nodes.JsonNode.Parse(stream)!.AsObject();
            Assert.False(manifest.ContainsKey("version"));
            var members = manifest["members"]!.AsArray();
            var first = members[0];
            members.RemoveAt(0);
            members.Add(first);
            stream.SetLength(0);
            System.Text.Json.JsonSerializer.Serialize(stream, manifest);
        }
        using var target = NewTarget();
        Assert.Equal(3, target.ImportAssetPackage(Preview(target, path), NoChoices));
    }

    [Fact]
    public void Nonseekable_picker_stream_can_be_previewed()
    {
        string package = WritePackage("sign", 5);
        using var wrapper = new MemoryStream();
        using (var zip = new ZipArchive(wrapper, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = zip.CreateEntry("sign.iw4pkg").Open();
            using var source = File.OpenRead(package);
            source.CopyTo(entry);
        }
        wrapper.Position = 0;
        using var opened = new ZipArchive(wrapper, ZipArchiveMode.Read);
        using var input = opened.Entries[0].Open();
        Assert.False(input.CanSeek);
        using var target = NewTarget();
        using var preview = target.PreviewAssetPackageImport(input);
        Assert.All(preview.Rows,
            row => Assert.Equal(AssetPackageStatus.Add, row.Status));
    }

    [Fact]
    public void Malformed_manifest_is_a_package_error()
    {
        string path = WritePackage("sign", 5);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            using var manifest = zip.GetEntry("manifest.json")!.Open();
            manifest.SetLength(0);
            manifest.Write("{"u8);
        }
        using var target = NewTarget();
        Assert.Throws<InvalidDataException>(() => Preview(target, path));
        Assert.Empty(target.Document.Rows);
    }

    [Fact]
    public void Changed_payload_is_rejected_before_the_target_is_modified()
    {
        string path = WritePackage("sign", 5);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            var entry = zip.Entries.Single(e => e.FullName.Equals("zone.ff", StringComparison.Ordinal));
            using var pixels = entry.Open();
            pixels.WriteByte(213);
        }
        using var target = NewTarget();
        Assert.Throws<InvalidDataException>(() => Preview(target, path));
        Assert.Empty(target.Document.Rows);
    }

    private string WritePackage(string name, byte fill, bool externalTechset = false,
        string imageName = "sign/color", MaterialWorldVertexFormat vertexFormat = default)
    {
        var techset = new MaterialTechniqueSetAsset { Name = "sign_techset", WorldVertexFormat = vertexFormat,
            TechniqueSlots = Enumerable.Range(0, (int)MaterialTechniqueType.Count)
                .Select(i => new MaterialTechniqueSlot((MaterialTechniqueType)i, default, null)).ToArray() };
        var image = new GfxImageAsset
        {
            Name = imageName, Format = 0x85, TextureControl1 = 0x1aae4, LevelCount = 3,
            DimensionCount = GfxImageDimension.TwoDimensional, MapType = MapType.TwoDimensional,
            Width = 4, Height = 4, Depth = 1, BaseWidth = 4, BaseHeight = 4, BaseDepth = 1, BaseLevelCount = 3,
            CardMemory = 128, PayloadByteCount = 128,
            PayloadBytes = Enumerable.Repeat(fill, 128).ToArray()
        };
        var texture = new MaterialTextureDef { Image = image, NameStart = (byte)'c', NameEnd = (byte)'p',
            Semantic = TextureSemantic.ColorMap };
        var material = new MaterialAsset
        {
            Info = new MaterialInfo { Name = name },
            TechniqueSet = externalTechset ? new MaterialTechniqueSetAsset { Name = "," + techset.Name } : techset,
            TextureCount = 2,
            Textures = [texture, texture], StateBitsEntries = Enumerable.Repeat(new MaterialStateBitsEntry(255),
                MaterialAsset.TechniqueSlotCount).ToArray()
        };
        string path = FilePath($"{name}.iw4pkg");
        BaseAsset[] roots = [externalTechset ? material.TechniqueSet! : techset, image, material];
        string donor = FilePath(name + ".ff");
        File.WriteAllBytes(donor, AssetPackageFixture.FastFile(roots, roots));
        AssetPackageFixture.Export(donor, path, name);
        return path;
    }

    private AssetPackageImportPreview Preview(FastFileEditingSession session, string path)
    {
        using var input = File.OpenRead(path);
        var preview = session.PreviewAssetPackageImport(input);
        _previews.Add(preview);
        return preview;
    }

    private static IW4.Game.Assets.BaseAsset Read(FastFileEditingSession session, WorkspaceAssetCatalogEntry row) =>
        AssetAuthoringAdapterRegistry.CreateDefault().CreateSurface(session, row).Definition!;

    private static FastFileEditingSession NewTarget() => new(new FastFileDocumentService().CreateBlank(1, 1));
    private string FilePath(string name) => Path.Combine(_directory.FullName, name);
    public void Dispose()
    {
        foreach (var preview in _previews) preview.Dispose();
        _directory.Delete(recursive: true);
    }
}

using System.Buffers.Binary;
using IW4.Game.Assets;
using IW4.Game.Assets.Image;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.TechniqueSet;
using IW4.Game.Zone;
using IW4.Loaders.Database;
using IW4.Linker.Contracts;
using IW4.Linker.Linking;
using IW4.Linker.Packaging;
using IW4.Studio.Documents;
using Xunit;

namespace IW4.Studio.Tests;

public sealed class AssetPackageEmbeddedProviderTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory(
        "IW4.AssetPackageEmbeddedProvider.");

    [Fact]
    public void Export_keeps_target_embedded_shader_when_common_has_same_key()
    {
        byte[] targetShader = WriteTerminalProfile();

        using FastFileWorkspace workspace = new FastFileDocumentService().Open(
            new FastFileDocumentOpenRequest(
                Path.Combine(_directory.FullName, "mp_terminal.ff"),
                new ZonePlan("default_mp")));
        using var session = new FastFileEditingSession(workspace);
        WorkspaceAssetCatalogEntry material = Assert.Single(
            workspace.AssetCatalog.TargetEntries,
            entry => entry.AssetType == XAssetType.Material &&
                     entry.NormalizedName == "terminal_material");

        var export = session.PreviewAssetPackageExport([material]);
        Assert.Equal(AssetPackageStatus.Included, Assert.Single(export.Rows, row => row.Type == XAssetType.PixelShader).Status);
        string archivePath = Path.Combine(_directory.FullName, "terminal.iw4pkg");
        export.WriteTo(archivePath);
        using var archive = File.OpenRead(archivePath);
        var contents = AssetPackageArchive.Read(archive);
        string nativePath = Path.Combine(_directory.FullName, "export.ff");
        File.WriteAllBytes(nativePath, contents.FastFile);
        using var decoded = new FastFileDocumentService().Open(new(nativePath, Isolated.Instance));
        var shader = Assert.Single(decoded.LoadedZone.Observation!.Events.OfType<ZoneProviderRegistered>()
            .Select(p => p.Provider).OfType<MaterialShaderAsset>());
        Assert.Equal(targetShader, shader.Data);
    }

    [Fact]
    public void Import_reuses_target_embedded_shader_before_same_key_common_provider()
    {
        byte[] targetShader = WriteTerminalProfile();

        using FastFileWorkspace workspace = OpenTerminalTarget();
        using var session = new FastFileEditingSession(workspace);
        string incoming = Path.Combine(_directory.FullName, "donor.ff");
        File.WriteAllBytes(incoming, MaterialZone("imported_material", targetShader, externalShader: true));
        string package = Path.ChangeExtension(incoming, ".iw4pkg");
        AssetPackageFixture.Export(incoming, package, "imported_material");
        using var source = File.OpenRead(package);
        using var preview = session.PreviewAssetPackageImport(source);
        Assert.Equal(AssetPackageStatus.Reuse, Assert.Single(preview.Rows, row => row.Type == XAssetType.PixelShader).Status);
        session.ImportAssetPackage(preview, new Dictionary<AssetKey, AssetPackageConflictChoice>());

        string importedPath = Path.Combine(_directory.FullName, "mp_terminal_imported.ff");
        SaveAsResult saved = new TransactionalSaveAsService().SaveAs(
            session,
            new SaveAsRequest(importedPath, AllowOverwrite: false));
        Assert.True(saved.Succeeded, string.Join(Environment.NewLine, saved.Diagnostics));
        using FastFileWorkspace reopened = new FastFileDocumentService().Open(
            new FastFileDocumentOpenRequest(importedPath, Isolated.Instance));
        Assert.DoesNotContain(
            reopened.LoadedZone.LoadedAssets,
            asset => asset.Materialization.RootProvider?.Asset is MaterialShaderAsset shaderRoot &&
                     shaderRoot.Name == "terminal/shared_pixel");
        MaterialShaderAsset shader = Assert.IsType<MaterialShaderAsset>(Assert.Single(
            reopened.LoadedZone.Observation!.Events
                .OfType<ZoneProviderRegistered>()
                .Select(registration => registration.Provider)
                .OfType<MaterialShaderAsset>(),
            asset => asset.Name == "terminal/shared_pixel"));
        Assert.Equal(targetShader, shader.Data);
    }

    private FastFileWorkspace OpenTerminalTarget() =>
        new FastFileDocumentService().Open(new FastFileDocumentOpenRequest(
            Path.Combine(_directory.FullName, "mp_terminal.ff"),
            new ZonePlan("default_mp")));

    private byte[] WriteTerminalProfile()
    {
        byte[] commonShader = CreatePixelShader(0x31);
        byte[] targetShader = CreatePixelShader(0x72);
        WriteShaderZone("common_mp.ff", commonShader);
        File.WriteAllBytes(Path.Combine(_directory.FullName, "mp_terminal.ff"), MaterialZone("terminal_material", targetShader));
        WriteEmptyZone("code_post_gfx_mp.ff");
        WriteEmptyZone("ui_mp.ff");
        WriteEmptyZone("mp_terminal_load.ff");
        return targetShader;
    }

    private void WriteShaderZone(string fileName, byte[] shaderBytes)
    {
        var shader = new MaterialShaderAsset
        {
            Name = "terminal/shared_pixel",
            Kind = MaterialShaderKind.Pixel,
            Data = shaderBytes,
            DataSize = checked((uint)shaderBytes.Length),
            LoadDefTail = new byte[MaterialShaderAsset.GetLoadDefTailSize(MaterialShaderKind.Pixel)]
        };
        var pool = new LinkAssetPool([new LinkAssetProviderSource(shader)]);
        var root = new LinkRoot(
            "shared-pixel",
            XAssetType.PixelShader,
            LinkRootIntent.Owned,
            AssetKey.FromDefinition(shader),
            shader.Name,
            opaqueHeader: null);
        WriteCanonicalFastFile(fileName, new ZoneLinkRequest(pool, [root], 1, 1, []));
    }

    private void WriteEmptyZone(string fileName)
    {
        using FastFileWorkspace blank = new FastFileDocumentService().CreateBlank(1, 1);
        using var session = new FastFileEditingSession(blank);
        SaveAsResult result = new TransactionalSaveAsService().SaveAs(
            session,
            new SaveAsRequest(Path.Combine(_directory.FullName, fileName), AllowOverwrite: false));
        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
    }

    private void WriteCanonicalFastFile(string fileName, ZoneLinkRequest request)
    {
        ZoneLinkResult link = new ZoneLinker().Link(request);
        Assert.True(link.Succeeded, string.Join(Environment.NewLine, link.Errors));
        Assert.True(link.DecodedBytes.HasValue);
        FastFilePackagingResult fastFile = new FastFilePackager().PackageGreenfield(
            link.DecodedBytes.Value,
            link.LanguageMask,
            link.SelectedLanguageMask,
            link.ImageStreamLanguageTables);
        Assert.True(fastFile.Succeeded, string.Join(Environment.NewLine, fastFile.Errors));
        File.WriteAllBytes(
            Path.Combine(_directory.FullName, fileName),
            fastFile.Bytes!.Value.ToArray());
    }

    private static byte[] MaterialZone(string materialName, byte[] shaderBytes, bool externalShader = false)
    {
        const string shaderName = "terminal/shared_pixel";
        var shader = new MaterialShaderAsset
        {
            Name = externalShader ? "," + shaderName : shaderName,
            Kind = MaterialShaderKind.Pixel,
            Data = externalShader ? null : shaderBytes,
            DataSize = externalShader ? 0 : checked((uint)shaderBytes.Length),
            LoadDefTail = new byte[MaterialShaderAsset.GetLoadDefTailSize(MaterialShaderKind.Pixel)]
        };
        var technique = new MaterialTechniqueAsset
        {
            Name = "terminal/shared_technique",
            PassCount = 1,
            Passes = [new MaterialPassAsset { PixelShader = shader }]
        };
        var techset = new MaterialTechniqueSetAsset
        {
            Name = "terminal_techset",
            TechniqueSlots = Enumerable.Range(0, (int)MaterialTechniqueType.Count)
                .Select(index => new MaterialTechniqueSlot(
                    (MaterialTechniqueType)index,
                    default,
                    index == 0 ? technique : null))
                .ToArray()
        };
        var image = new GfxImageAsset
        {
            Name = "terminal/color",
            Format = 0x85,
            TextureControl1 = 0x0001AAE4,
            LevelCount = 1,
            DimensionCount = GfxImageDimension.TwoDimensional,
            MapType = MapType.TwoDimensional,
            Width = 4,
            Height = 4,
            Depth = 1,
            BaseWidth = 4,
            BaseHeight = 4,
            BaseDepth = 1,
            BaseLevelCount = 1,
            PayloadByteCount = 128,
            PayloadBytes = Enumerable.Repeat((byte)0x19, 128).ToArray()
        };
        var texture = new MaterialTextureDef
        {
            Image = image,
            NameStart = (byte)'c',
            NameEnd = (byte)'p',
            Semantic = TextureSemantic.ColorMap
        };
        var material = new MaterialAsset
        {
            Info = new MaterialInfo { Name = materialName },
            TechniqueSet = techset,
            TextureCount = 2,
            Textures = [texture, texture],
            StateBitsEntries = Enumerable.Repeat(
                new MaterialStateBitsEntry(255), MaterialAsset.TechniqueSlotCount).ToArray()
        };
        BaseAsset[] definitions = [image, shader, techset, material];
        BaseAsset[] roots = externalShader ? [image, shader, techset, material] : [image, techset, material];
        return AssetPackageFixture.FastFile(definitions, roots);
    }

    private static byte[] CreatePixelShader(byte marker)
    {
        byte[] bytes = new byte[60];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0x14), 0x20);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0x18), 4);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0x1c), 0x38);
        bytes[0x38] = marker;
        return bytes;
    }

    public void Dispose() => _directory.Delete(recursive: true);
}

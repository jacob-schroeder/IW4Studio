using System.Security.Cryptography;
using IW4.Assets.Assets;
using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.LightDef;
using IW4.Assets.Assets.Localize;
using IW4.Assets.Assets.XAnim;
using IW4.FastFiles.Database;
using IW4.FastFiles.Database.Streaming;
using IW4.FastFiles.Loaders.Database;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Strings;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;
using IW4.Linker.Linking;
using IW4.Linker.Packaging;
using IW4.Linker.SourceLayout;
using IW4.Runtime.Database;
using IW4.Runtime.IO;
using IW4.Studio.Documents;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace IW4.Studio.Tests;

public sealed class StockFastFileRoundTripTests
{
    private const string RepositoryDirectory = "/Users/jacob/Repositories/IW4Studio";
    private const string StockFastFilesDirectory = "/Users/jacob/Repositories/MW2/fastfiles";
    private const string MapPack1Directory = "/Users/jacob/Repositories/MW2/fastfiles/mappack1";
    private const string MapPack2Directory = "/Users/jacob/Repositories/MW2/fastfiles/mappack2";
    private const string StockFastFileAllowlistEnvironmentVariable = "IW4_STOCK_FASTFILE_ALLOWLIST";
    private const string TemporaryDirectoryNamePrefix = "IW4.Studio.Tests.StockFastFileRoundTrip.";
    private const int ComparisonBufferSize = 1024 * 1024;

    public static IEnumerable<object[]> StockFastFiles =>
        DiscoverStockFastFiles().Select(path => new object[] { path });

    [Theory]
    [MemberData(nameof(StockFastFiles))]
    public void Source_layout_relinker_preserves_stock_decoded_zone_and_header_envelope(
        string sourcePath)
    {
        string temporaryDirectory = CreateTemporaryDirectory();
        FileFingerprint? sourceBefore = null;

        try
        {
            sourceBefore = CaptureFingerprint(sourcePath);
            string destinationPath = Path.Combine(
                temporaryDirectory,
                Path.GetFileName(sourcePath));
            EnsureOutputPathIsSafe(destinationPath, temporaryDirectory);

            SourceLayoutReplay source = WithLoadedZone(
                sourcePath,
                selectedLanguageMask: 0,
                (_, loaded) =>
                {
                    SourceLayoutRelinkResult relink =
                        new SourceLayoutRelinker().Relink(loaded.ZoneObjectFile);
                    Assert.True(
                        relink.Succeeded,
                        $"Source-layout relink failed for '{sourcePath}'.{Environment.NewLine}" +
                        string.Join(
                            Environment.NewLine,
                            relink.Errors.Select(error => $"{error.Code}: {error.Message}")));
                    ReadOnlyMemory<byte>? relinked = relink.DecodedBytes;
                    Assert.True(relinked.HasValue);
                    AssertByteSequencesMatch(
                        loaded.ZoneBytes,
                        relinked.Value.Span,
                        $"Source-layout decoded replay mismatch for '{sourcePath}'");

                    FastFilePackagingResult package = new FastFilePackager().Package(
                        relinked.Value,
                        loaded.Header);
                    Assert.True(
                        package.Succeeded,
                        $"Source-layout packaging failed for '{sourcePath}'.{Environment.NewLine}" +
                        string.Join(
                            Environment.NewLine,
                            package.Errors.Select(error => $"{error.Code}: {error.Message}")));
                    ReadOnlyMemory<byte>? packaged = package.Bytes;
                    Assert.True(packaged.HasValue);

                    return new SourceLayoutReplay(
                        loaded.ZoneBytes.ToArray(),
                        CaptureHeaderEnvelope(loaded),
                        packaged.Value.ToArray());
                });

            File.WriteAllBytes(destinationPath, source.PackagedFastFile);
            HeaderEnvelope candidate = WithLoadedZone(
                destinationPath,
                selectedLanguageMask: 0,
                (_, loaded) =>
                {
                    AssertByteSequencesMatch(
                        source.DecodedZone,
                        loaded.ZoneBytes,
                        $"Decoded zone mismatch: source '{sourcePath}', candidate '{destinationPath}'");
                    return CaptureHeaderEnvelope(loaded);
                });

            AssertHeaderEnvelopeMatches(source.Header, candidate, sourcePath, destinationPath);
        }
        finally
        {
            try
            {
                AssertSourceFingerprintUnchanged(sourcePath, sourceBefore);
            }
            finally
            {
                DeleteTemporaryDirectory(temporaryDirectory);
            }
        }
    }

    [Theory]
    [MemberData(nameof(StockFastFiles))]
    public void Canonical_save_as_reloads_as_the_same_semantic_zone(string sourcePath)
    {
        string temporaryDirectory = CreateTemporaryDirectory();
        FileFingerprint? sourceBefore = null;

        try
        {
            sourceBefore = CaptureFingerprint(sourcePath);
            string destinationPath = Path.Combine(
                temporaryDirectory,
                Path.GetFileName(sourcePath));
            EnsureOutputPathIsSafe(destinationPath, temporaryDirectory);

            var documentService = new FastFileDocumentService();
            using FastFileWorkspace workspace = documentService.Open(
                new FastFileDocumentOpenRequest(sourcePath, Isolated.Instance));
            ZoneLinkRequest sourceRequest = workspace.InitialLinkRequest;
            ZoneLinkResult expected = new ZoneLinker().Link(sourceRequest);
            AssertLinkSucceeded(expected, $"Canonical source link failed for '{sourcePath}'");

            using (var editingSession = new FastFileEditingSession(workspace))
            {
                SaveAsResult result = new TransactionalSaveAsService().SaveAs(
                    editingSession,
                    new SaveAsRequest(destinationPath, AllowOverwrite: false));

                Assert.True(
                    result.Succeeded,
                    $"Canonical Save As failed for '{sourcePath}'.{Environment.NewLine}" +
                    string.Join(Environment.NewLine, result.Diagnostics));
            }

            Assert.True(
                File.Exists(destinationPath),
                $"Save As reported success for '{sourcePath}', but did not create '{destinationPath}'.");

            using FastFileWorkspace candidate = documentService.Open(
                new FastFileDocumentOpenRequest(destinationPath, Isolated.Instance));
            ZoneLinkRequest candidateRequest = candidate.InitialLinkRequest;
            ZoneLinkResult actual = new ZoneLinker().Link(candidateRequest);
            AssertLinkSucceeded(actual, $"Canonical candidate relink failed for '{sourcePath}'");

            Assert.Equal(sourceRequest.LanguageMask, candidateRequest.LanguageMask);
            Assert.Equal(sourceRequest.SelectedLanguageMask, candidateRequest.SelectedLanguageMask);
            Assert.Equal(expected.LanguageMask, actual.LanguageMask);
            Assert.Equal(expected.SelectedLanguageMask, actual.SelectedLanguageMask);
            AssertByteSequencesMatch(
                GetDecodedBytes(expected).Span,
                GetDecodedBytes(actual).Span,
                $"Canonical decoded zone mismatch for '{sourcePath}'");
            AssertXFileLayoutMatches(expected.XFile, actual.XFile, sourcePath);
            AssertXFileLayoutMatches(expected.XFile, candidate.LoadedZone.XFile, sourcePath);
            AssertImageStreamLanguageTablesMatch(
                expected.ImageStreamLanguageTables,
                actual.ImageStreamLanguageTables,
                sourcePath);
            AssertOrderedRootSubsequence(sourceRequest.Roots, candidateRequest.Roots, sourcePath);
            AssertCandidateRootRows(candidate.LoadedZone, candidateRequest.Roots, sourcePath);
        }
        finally
        {
            try
            {
                AssertSourceFingerprintUnchanged(sourcePath, sourceBefore);
            }
            finally
            {
                DeleteTemporaryDirectory(temporaryDirectory);
            }
        }
    }

    [Fact]
    public void Blank_save_as_reloads_an_empty_semantic_zone()
    {
        string temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            string destinationPath = Path.Combine(temporaryDirectory, "blank.ff");
            EnsureOutputPathIsSafe(destinationPath, temporaryDirectory);

            var documentService = new FastFileDocumentService();
            using FastFileWorkspace blank = documentService.CreateBlank(1, 1);
            using (var editingSession = new FastFileEditingSession(blank))
                AssertSaveSucceeded(editingSession, destinationPath);

            Assert.False(File.Exists(Path.Combine(temporaryDirectory, "imagefile1.pak")));
            using FastFileWorkspace loaded = documentService.Open(
                new FastFileDocumentOpenRequest(destinationPath, Isolated.Instance));
            Assert.Equal(1u, loaded.InitialLinkRequest.LanguageMask);
            Assert.Equal(1u, loaded.InitialLinkRequest.SelectedLanguageMask);
            Assert.Empty(loaded.InitialLinkRequest.Roots);
            Assert.Empty(loaded.InitialLinkRequest.Assets.Providers);
            Assert.Equal(0, loaded.LoadedZone.XAssetList.AssetCount);
            Assert.Empty(loaded.LoadedZone.XAssetList.Assets);
            Assert.Equal(0, loaded.LoadedZone.XAssetList.ScriptStringCount);
            Assert.Empty(loaded.LoadedZone.XAssetList.ScriptStrings);
            Assert.Empty(loaded.LoadedZone.LoadedAssets);
        }
        finally
        {
            DeleteTemporaryDirectory(temporaryDirectory);
        }
    }

    [Theory]
    [InlineData("ImAgEfIlE0007.PaK")]
    [InlineData("imagefile-custom.pak")]
    [InlineData("imagefile.pak")]
    public void Save_as_refuses_to_create_an_imagefile_package(
        string protectedFileName)
    {
        string temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            string protectedPath = Path.Combine(
                temporaryDirectory,
                protectedFileName);
            EnsureOutputPathIsSafe(protectedPath, temporaryDirectory);

            var documentService = new FastFileDocumentService();
            using FastFileWorkspace blank = documentService.CreateBlank(1, 1);
            using var editingSession = new FastFileEditingSession(blank);
            SaveAsResult result = new TransactionalSaveAsService().SaveAs(
                editingSession,
                new SaveAsRequest(protectedPath, AllowOverwrite: true));

            Assert.False(result.Succeeded);
            Assert.False(result.Cancelled);
            Assert.Contains(
                result.Diagnostics,
                diagnostic => diagnostic.Contains(
                    "imagefile*.pak",
                    StringComparison.OrdinalIgnoreCase));
            Assert.Empty(Directory.EnumerateFileSystemEntries(temporaryDirectory));
        }
        finally
        {
            DeleteTemporaryDirectory(temporaryDirectory);
        }
    }

    [Fact]
    public void Authored_localize_edit_publishes_the_selected_revision()
    {
        string temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            string destinationPath = Path.Combine(temporaryDirectory, "edited.ff");
            EnsureOutputPathIsSafe(destinationPath, temporaryDirectory);

            var documentService = new FastFileDocumentService();
            using FastFileWorkspace blank = documentService.CreateBlank(1, 1);
            using (var editingSession = new FastFileEditingSession(blank))
            {
                AssetAuthoringAdapterRegistry registry =
                    AssetAuthoringAdapterRegistry.CreateDefault();
                WorkspaceAssetCatalogEntry entry = registry.AddAsset(
                    editingSession,
                    XAssetType.Localize,
                    "qual/replace");
                var editor = Assert.IsType<AssetEditorSession>(
                    registry.CreateSurface(editingSession, entry));
                Assert.True(editor.Apply<LocalizeDraft>(draft =>
                    draft.SetValue("new value")));
                editor.Close();
                AssertSaveSucceeded(editingSession, destinationPath);
            }

            using FastFileWorkspace loaded = documentService.Open(
                new FastFileDocumentOpenRequest(destinationPath, Isolated.Instance));
            LocalizeAsset linked = Assert.IsType<LocalizeAsset>(
                Assert.Single(loaded.LoadedZone.LoadedAssets).Asset);
            Assert.Equal("qual/replace", linked.Name);
            Assert.Equal("new value", linked.Value);
            Assert.Single(loaded.InitialLinkRequest.Roots);
            Assert.Single(loaded.InitialLinkRequest.Assets.Providers);
        }
        finally
        {
            DeleteTemporaryDirectory(temporaryDirectory);
        }
    }

    [Fact]
    public void Imported_provider_edit_detaches_changed_text_from_the_frozen_base_identity()
    {
        string temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            string sourcePath = Path.Combine(temporaryDirectory, "imported-source.ff");
            string editedPath = Path.Combine(temporaryDirectory, "imported-edited.ff");
            EnsureOutputPathIsSafe(sourcePath, temporaryDirectory);
            EnsureOutputPathIsSafe(editedPath, temporaryDirectory);

            var original = new LocalizeAsset
            {
                Name = "qual/imported-edit",
                Value = "original"
            };
            var documentService = new FastFileDocumentService();
            WriteCanonicalFastFile(
                new ZoneLinkRequest(
                    new LinkAssetPool([new LinkAssetProviderSource(original)]),
                    [CreateOwnedRoot("imported-edit", original)],
                    languageMask: 1,
                    selectedLanguageMask: 1),
                sourcePath);

            using (FastFileWorkspace importedWorkspace = documentService.Open(
                       new FastFileDocumentOpenRequest(sourcePath, Isolated.Instance)))
            using (var editingSession = new FastFileEditingSession(importedWorkspace))
            {
                WorkspaceAssetCatalogEntry entry = Assert.Single(
                    editingSession.Document.Rows);
                AssetAuthoringAdapterRegistry registry =
                    AssetAuthoringAdapterRegistry.CreateDefault();
                var editor = Assert.IsType<AssetEditorSession>(
                    registry.CreateSurface(editingSession, entry));
                Assert.True(editor.Apply<LocalizeDraft>(draft =>
                    draft.SetValue("edited replacement value")));
                editor.Close();
                AssertSaveSucceeded(editingSession, editedPath);
            }

            using FastFileWorkspace linkedWorkspace = documentService.Open(
                new FastFileDocumentOpenRequest(editedPath, Isolated.Instance));
            LocalizeAsset linked = Assert.IsType<LocalizeAsset>(
                Assert.Single(linkedWorkspace.LoadedZone.LoadedAssets).Asset);
            Assert.Equal(original.Name, linked.Name);
            Assert.Equal("edited replacement value", linked.Value);
        }
        finally
        {
            DeleteTemporaryDirectory(temporaryDirectory);
        }
    }

    [Fact]
    public void Save_as_rejects_a_stale_revision_and_removes_staged_files()
    {
        string temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            string destinationPath = Path.Combine(temporaryDirectory, "stale.ff");
            EnsureOutputPathIsSafe(destinationPath, temporaryDirectory);

            var documentService = new FastFileDocumentService();
            using FastFileWorkspace blank = documentService.CreateBlank(1, 1);
            using var editingSession = new FastFileEditingSession(blank);
            var validator = new CallbackCandidateValidator((_, _) =>
            {
                _ = AssetAuthoringAdapterRegistry.CreateDefault().AddAsset(
                    editingSession,
                    XAssetType.Localize,
                    "qual/stale-revision");
                return [];
            });

            SaveAsResult result = new TransactionalSaveAsService().SaveAs(
                editingSession,
                new SaveAsRequest(
                    destinationPath,
                    AllowOverwrite: false,
                    CandidateValidator: validator));

            Assert.False(result.Succeeded);
            Assert.False(result.Cancelled);
            Assert.Contains(
                result.Diagnostics,
                value => value.Contains("became stale", StringComparison.Ordinal));
            Assert.False(File.Exists(destinationPath));
            Assert.Empty(Directory.EnumerateFileSystemEntries(temporaryDirectory));
        }
        finally
        {
            DeleteTemporaryDirectory(temporaryDirectory);
        }
    }

    [Fact]
    public void Shared_nested_provider_is_emitted_once_and_reused_by_alias_cell()
    {
        string temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            string destinationPath = Path.Combine(temporaryDirectory, "shared-provider.ff");
            EnsureOutputPathIsSafe(destinationPath, temporaryDirectory);

            var sharedImage = new GfxImageAsset { Name = ",qual/shared_image" };
            var lightA = new LightDefAsset
            {
                Name = "qual/light_a",
                Image = sharedImage
            };
            var lightB = new LightDefAsset
            {
                Name = "qual/light_b",
                Image = sharedImage
            };

            WriteCanonicalFastFile(
                new ZoneLinkRequest(
                    new LinkAssetPool([
                        new LinkAssetProviderSource(sharedImage),
                        new LinkAssetProviderSource(lightA),
                        new LinkAssetProviderSource(lightB)
                    ]),
                    [
                        CreateOwnedRoot("light-a", lightA),
                        CreateOwnedRoot("light-b", lightB)
                    ],
                    languageMask: 1,
                    selectedLanguageMask: 1),
                destinationPath);

            var documentService = new FastFileDocumentService();
            using FastFileWorkspace loaded = documentService.Open(
                new FastFileDocumentOpenRequest(destinationPath, Isolated.Instance));
            Assert.Equal(2, loaded.LoadedZone.XAssetList.AssetCount);
            Assert.Equal(2, loaded.LoadedZone.LoadedAssets.Count);
            Assert.Equal(3, loaded.InitialLinkRequest.Assets.Providers.Count);
            Assert.Single(
                loaded.InitialLinkRequest.Assets.Providers,
                provider => provider.SerializedType == XAssetType.Image);

            LightDefAsset loadedA = Assert.IsType<LightDefAsset>(
                loaded.LoadedZone.LoadedAssets[0].Asset);
            LightDefAsset loadedB = Assert.IsType<LightDefAsset>(
                loaded.LoadedZone.LoadedAssets[1].Asset);
            Assert.NotNull(loadedA.Image);
            Assert.Same(loadedA.Image, loadedB.Image);
            Assert.Equal(PointerType.Insert, loadedA.ImagePointer.Type);
            Assert.Equal(PointerType.Offset, loadedB.ImagePointer.Type);
            Assert.Equal(XPointerResolutionMode.AliasCell, loadedB.ImagePointer.ResolutionMode);
        }
        finally
        {
            DeleteTemporaryDirectory(temporaryDirectory);
        }
    }

    [Fact]
    public void XAnim_script_strings_rebuild_null_empty_and_shared_indices()
    {
        string temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            string destinationPath = Path.Combine(temporaryDirectory, "script-strings.ff");
            EnsureOutputPathIsSafe(destinationPath, temporaryDirectory);
            XBlockAddress semanticCell = new(XFileBlockType.LARGE, 0);
            var animation = new XAnimPartsAsset
            {
                Name = "qual/anim",
                BoneCounts = new byte[10],
                BoneNameCount = 4,
                Names = [
                    new ScriptStringReference(41, "", new ScriptStringHandle(41), semanticCell),
                    new ScriptStringReference(99, "tag_shared", new ScriptStringHandle(99), semanticCell),
                    new ScriptStringReference(7, "tag_shared", new ScriptStringHandle(7), semanticCell),
                    new ScriptStringReference(0, null, ScriptStringHandle.Null, semanticCell)
                ]
            };

            WriteCanonicalFastFile(
                new ZoneLinkRequest(
                    new LinkAssetPool([new LinkAssetProviderSource(animation)]),
                    [CreateOwnedRoot("animation", animation)],
                    languageMask: 1,
                    selectedLanguageMask: 1),
                destinationPath);

            var documentService = new FastFileDocumentService();
            using FastFileWorkspace loaded = documentService.Open(
                new FastFileDocumentOpenRequest(destinationPath, Isolated.Instance));
            Assert.Equal(3, loaded.LoadedZone.XAssetList.ScriptStringCount);
            Assert.Equal(
                new string?[] { null, "", "tag_shared" },
                loaded.LoadedZone.XAssetList.ScriptStrings
                    .Select(entry => entry.Value)
                    .ToArray());

            XAnimPartsAsset loadedAnimation = Assert.IsType<XAnimPartsAsset>(
                Assert.Single(loaded.LoadedZone.LoadedAssets).Asset);
            Assert.Equal(new ushort[] { 1, 2, 2, 0 },
                loadedAnimation.Names.Select(value => value.RawLocalIndex).ToArray());
            Assert.Equal(new string?[] { "", "tag_shared", "tag_shared", null },
                loadedAnimation.Names.Select(value => value.Text).ToArray());
            Assert.False(loadedAnimation.Names[0].RuntimeHandle.IsNull);
            Assert.Equal(
                loadedAnimation.Names[1].RuntimeHandle,
                loadedAnimation.Names[2].RuntimeHandle);
            Assert.True(loadedAnimation.Names[3].RuntimeHandle.IsNull);
        }
        finally
        {
            DeleteTemporaryDirectory(temporaryDirectory);
        }
    }

    [Fact]
    public void Streamed_image_preserves_read_only_language_references_without_creating_a_package()
    {
        string temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            string destinationPath = Path.Combine(temporaryDirectory, "streamed-image.ff");
            EnsureOutputPathIsSafe(destinationPath, temporaryDirectory);

            var languageOneEntry = new DbHeaderImageStreamEntry(
                FileIndex: 3,
                SourceStart: 0x1000,
                SourceEnd: 0x1100,
                BlockOffset: 0x0020,
                StreamOffset: 0x00020020,
                SerializedOffset: -1);
            var languageTwoEntry = new DbHeaderImageStreamEntry(
                FileIndex: 9,
                SourceStart: 0x3000,
                SourceEnd: 0x3200,
                BlockOffset: 0x0040,
                StreamOffset: 0x00050040,
                SerializedOffset: -1);
            ImageFileStreamLanguageReferences[] imageStreamReferences = [
                new(1, [
                    new ImageFileStreamReference(languageOneEntry, byteLength: 4),
                    EmptyImageFileStreamReference(),
                    EmptyImageFileStreamReference(),
                    EmptyImageFileStreamReference()
                ]),
                new(2, [
                    new ImageFileStreamReference(languageTwoEntry, byteLength: 4),
                    EmptyImageFileStreamReference(),
                    EmptyImageFileStreamReference(),
                    EmptyImageFileStreamReference()
                ])
            ];
            var image = new GfxImageAsset
            {
                Name = "qual/streamed",
                StreamData = [
                    new GfxImageStreamData(1, 1, 4),
                    new GfxImageStreamData(0, 0, 0),
                    new GfxImageStreamData(0, 0, 0),
                    new GfxImageStreamData(0, 0, 0)
                ]
            };

            WriteCanonicalFastFile(
                new ZoneLinkRequest(
                    new LinkAssetPool([
                        new LinkAssetProviderSource(
                            image,
                            imageStreamReferences: imageStreamReferences)
                    ]),
                    [CreateOwnedRoot("streamed-image", image)],
                    languageMask: 3,
                    selectedLanguageMask: 2),
                destinationPath);

            Assert.True(File.Exists(destinationPath));
            Assert.Equal(
                destinationPath,
                Assert.Single(Directory.EnumerateFiles(temporaryDirectory)));
            Assert.Empty(Directory.EnumerateDirectories(temporaryDirectory));
            Assert.DoesNotContain(
                Directory.EnumerateFiles(temporaryDirectory),
                path => string.Equals(
                    Path.GetExtension(path),
                    ".pak",
                    StringComparison.OrdinalIgnoreCase));
            AssertStreamedImageReload(
                destinationPath,
                selectedLanguageMask: 1,
                imageStreamReferences);
            AssertStreamedImageReload(
                destinationPath,
                selectedLanguageMask: 2,
                imageStreamReferences);
        }
        finally
        {
            DeleteTemporaryDirectory(temporaryDirectory);
        }
    }

    private static IEnumerable<string> DiscoverStockFastFiles()
    {
        string[] paths =
        [
            .. Directory.EnumerateFiles(
                StockFastFilesDirectory,
                "*.ff",
                SearchOption.TopDirectoryOnly),
            .. Directory.EnumerateFiles(
                MapPack1Directory,
                "*.ff",
                SearchOption.TopDirectoryOnly),
            .. Directory.EnumerateFiles(
                MapPack2Directory,
                "*.ff",
                SearchOption.TopDirectoryOnly)
        ];

        string[] discoveredPaths = paths
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(
                StockFastFileAllowlistEnvironmentVariable)) &&
            discoveredPaths.Length != 105)
        {
            throw new InvalidOperationException(
                $"The stock-oracle suite requires exactly 105 fastfiles, but discovered {discoveredPaths.Length}.");
        }

        return SelectStockFastFiles(discoveredPaths);
    }

    private static IEnumerable<string> SelectStockFastFiles(IReadOnlyList<string> discoveredPaths)
    {
        string? selector = Environment.GetEnvironmentVariable(StockFastFileAllowlistEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(selector))
            return discoveredPaths;

        string[] requestedNames = selector.Split(';', StringSplitOptions.TrimEntries);
        var selectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var selectedPaths = new List<string>(requestedNames.Length);
        foreach (string requestedName in requestedNames)
        {
            if (requestedName.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Environment variable '{StockFastFileAllowlistEnvironmentVariable}' contains an empty fastfile name.");
            }

            if (Path.GetFileName(requestedName) != requestedName ||
                requestedName.Contains('/') ||
                requestedName.Contains('\\') ||
                !string.Equals(Path.GetExtension(requestedName), ".ff", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Environment variable '{StockFastFileAllowlistEnvironmentVariable}' entry '{requestedName}' " +
                    "must be an exact .ff basename without path components.");
            }

            if (!selectedNames.Add(requestedName))
            {
                throw new InvalidOperationException(
                    $"Environment variable '{StockFastFileAllowlistEnvironmentVariable}' requests '{requestedName}' more than once.");
            }

            string[] matches = discoveredPaths
                .Where(path => string.Equals(
                    Path.GetFileName(path),
                    requestedName,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matches.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Environment variable '{StockFastFileAllowlistEnvironmentVariable}' requests unknown stock fastfile '{requestedName}'.");
            }

            if (matches.Length != 1)
            {
                throw new InvalidOperationException(
                    $"Environment variable '{StockFastFileAllowlistEnvironmentVariable}' entry '{requestedName}' " +
                    "matches more than one discovered stock fastfile.");
            }

            selectedPaths.Add(matches[0]);
        }

        if (selectedPaths.Count == 0)
        {
            throw new InvalidOperationException(
                $"Environment variable '{StockFastFileAllowlistEnvironmentVariable}' produced an empty fastfile selection.");
        }

        return selectedPaths.OrderBy(path => path, StringComparer.Ordinal);
    }

    private static string CreateTemporaryDirectory()
    {
        string temporaryRoot = Path.GetTempPath();
        string temporaryDirectory = Path.Combine(
            temporaryRoot,
            $"{TemporaryDirectoryNamePrefix}{Guid.NewGuid():N}");
        ValidateOperationOwnedTemporaryDirectory(temporaryDirectory, temporaryRoot);
        string resolvedTemporaryRoot = ResolveExistingDirectory(temporaryRoot);
        string proposedPhysicalDirectory = Path.Combine(
            resolvedTemporaryRoot,
            Path.GetFileName(temporaryDirectory));
        RejectProtectedPhysicalPath(proposedPhysicalDirectory);
        if (FilesystemEntryExists(temporaryDirectory))
        {
            throw new IOException(
                $"Operation-owned temporary directory path '{temporaryDirectory}' is already occupied.");
        }

        Directory.CreateDirectory(temporaryDirectory);
        ValidateExistingOperationOwnedTemporaryDirectory(temporaryDirectory, temporaryRoot);
        return temporaryDirectory;
    }

    private static void DeleteTemporaryDirectory(string temporaryDirectory)
    {
        string temporaryRoot = Path.GetTempPath();
        ValidateOperationOwnedTemporaryDirectory(temporaryDirectory, temporaryRoot);
        if (!FilesystemEntryExists(temporaryDirectory))
            return;

        ValidateExistingOperationOwnedTemporaryDirectory(temporaryDirectory, temporaryRoot);
        Directory.Delete(temporaryDirectory, recursive: true);
    }

    private static void ValidateOperationOwnedTemporaryDirectory(
        string temporaryDirectory,
        string temporaryRoot)
    {
        string fullTemporaryDirectory =
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(temporaryDirectory));
        string fullTemporaryRoot =
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(temporaryRoot));
        string? parentDirectory = Path.GetDirectoryName(fullTemporaryDirectory);
        if (!string.Equals(
                Path.TrimEndingDirectorySeparator(parentDirectory ?? string.Empty),
                fullTemporaryRoot,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Temporary directory '{fullTemporaryDirectory}' is not an immediate child of " +
                $"the current OS temp root '{fullTemporaryRoot}'.");
        }

        string leafName = Path.GetFileName(fullTemporaryDirectory);
        string guidPayload = leafName.StartsWith(
            TemporaryDirectoryNamePrefix,
            StringComparison.Ordinal)
            ? leafName[TemporaryDirectoryNamePrefix.Length..]
            : string.Empty;
        if (!Guid.TryParseExact(guidPayload, "N", out _))
        {
            throw new InvalidOperationException(
                $"Temporary directory '{fullTemporaryDirectory}' is not owned by this test case.");
        }

        if (IsPathWithinOrEqual(fullTemporaryDirectory, RepositoryDirectory) ||
            IsPathWithinOrEqual(fullTemporaryDirectory, StockFastFilesDirectory) ||
            IsPathWithinOrEqual(fullTemporaryDirectory, MapPack1Directory) ||
            IsPathWithinOrEqual(fullTemporaryDirectory, MapPack2Directory))
        {
            throw new InvalidOperationException(
                $"Temporary directory '{fullTemporaryDirectory}' overlaps a protected repository or stock directory.");
        }
    }

    private static void EnsureOutputPathIsSafe(string destinationPath, string temporaryDirectory)
    {
        string fullDestinationPath = Path.GetFullPath(destinationPath);
        string fullTemporaryDirectory =
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(temporaryDirectory));
        string resolvedTemporaryDirectory =
            ValidateExistingOperationOwnedTemporaryDirectory(
                temporaryDirectory,
                Path.GetTempPath());
        if (!IsImmediateChild(fullDestinationPath, fullTemporaryDirectory))
        {
            throw new InvalidOperationException(
                $"The Save As destination '{fullDestinationPath}' is not an immediate child of " +
                $"its operation-owned temporary directory '{fullTemporaryDirectory}'.");
        }

        string physicalDestinationPath = Path.Combine(
            resolvedTemporaryDirectory,
            Path.GetFileName(fullDestinationPath));
        RejectProtectedPhysicalPath(physicalDestinationPath);
    }

    private static string ValidateExistingOperationOwnedTemporaryDirectory(
        string temporaryDirectory,
        string temporaryRoot)
    {
        ValidateOperationOwnedTemporaryDirectory(temporaryDirectory, temporaryRoot);

        string fullTemporaryDirectory =
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(temporaryDirectory));
        var directory = new DirectoryInfo(fullTemporaryDirectory);
        if (!directory.Exists)
        {
            throw new DirectoryNotFoundException(
                $"Operation-owned temporary directory '{fullTemporaryDirectory}' does not exist.");
        }

        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException(
                $"Operation-owned temporary directory '{fullTemporaryDirectory}' must not be a symlink or reparse point.");
        }

        string resolvedTemporaryRoot = ResolveExistingDirectory(temporaryRoot);
        string resolvedTemporaryDirectory = ResolveExistingDirectory(fullTemporaryDirectory);
        if (!IsImmediateChild(resolvedTemporaryDirectory, resolvedTemporaryRoot))
        {
            throw new InvalidOperationException(
                $"Operation-owned temporary directory '{resolvedTemporaryDirectory}' is not an immediate " +
                $"physical child of the resolved OS temp root '{resolvedTemporaryRoot}'.");
        }

        RejectProtectedPhysicalPath(resolvedTemporaryDirectory);
        return resolvedTemporaryDirectory;
    }

    private static string ResolveExistingDirectory(string directoryPath)
    {
        string fullDirectoryPath =
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(directoryPath));
        if (!Directory.Exists(fullDirectoryPath))
        {
            throw new DirectoryNotFoundException(
                $"Directory '{fullDirectoryPath}' must exist before its physical path can be resolved.");
        }

        string root = Path.GetPathRoot(fullDirectoryPath)
            ?? throw new InvalidOperationException(
                $"Directory '{fullDirectoryPath}' has no filesystem root.");
        string currentDirectory = Path.TrimEndingDirectorySeparator(root);
        string relativePath = Path.GetRelativePath(root, fullDirectoryPath);
        if (string.Equals(relativePath, ".", StringComparison.Ordinal))
            return currentDirectory;

        foreach (string component in relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries))
        {
            var componentDirectory = new DirectoryInfo(
                Path.Combine(currentDirectory, component));
            if (!componentDirectory.Exists)
            {
                throw new DirectoryNotFoundException(
                    $"Directory component '{componentDirectory.FullName}' no longer exists while resolving " +
                    $"'{fullDirectoryPath}'.");
            }

            if ((componentDirectory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                FileSystemInfo? target = componentDirectory.ResolveLinkTarget(returnFinalTarget: true);
                if (target is not DirectoryInfo targetDirectory || !targetDirectory.Exists)
                {
                    throw new InvalidOperationException(
                        $"Directory link '{componentDirectory.FullName}' does not resolve to an existing directory.");
                }

                currentDirectory = Path.TrimEndingDirectorySeparator(targetDirectory.FullName);
            }
            else
            {
                currentDirectory = Path.TrimEndingDirectorySeparator(componentDirectory.FullName);
            }
        }

        return currentDirectory;
    }

    private static void RejectProtectedPhysicalPath(string physicalPath)
    {
        foreach (string protectedRoot in ResolveProtectedDirectories())
        {
            if (IsPathWithinOrEqual(physicalPath, protectedRoot) ||
                IsPathWithinOrEqual(protectedRoot, physicalPath))
            {
                throw new InvalidOperationException(
                    $"Path '{physicalPath}' overlaps protected physical directory '{protectedRoot}'.");
            }
        }
    }

    private static IEnumerable<string> ResolveProtectedDirectories()
    {
        yield return ResolveExistingDirectory(RepositoryDirectory);
        yield return ResolveExistingDirectory(StockFastFilesDirectory);
        yield return ResolveExistingDirectory(MapPack1Directory);
        yield return ResolveExistingDirectory(MapPack2Directory);
    }

    private static bool IsImmediateChild(string path, string directory) =>
        string.Equals(
            GetNormalizedFullPath(Path.GetDirectoryName(Path.GetFullPath(path)) ?? string.Empty),
            GetNormalizedFullPath(directory),
            StringComparison.OrdinalIgnoreCase);

    private static bool IsPathWithin(string path, string directory)
    {
        string fullPath = GetNormalizedFullPath(path);
        string fullDirectory = GetNormalizedFullPath(directory);
        string directoryPrefix = fullDirectory.EndsWith(Path.DirectorySeparatorChar) ||
            fullDirectory.EndsWith(Path.AltDirectorySeparatorChar)
            ? fullDirectory
            : fullDirectory + Path.DirectorySeparatorChar;
        return !string.Equals(fullPath, fullDirectory, StringComparison.OrdinalIgnoreCase) &&
            fullPath.StartsWith(directoryPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPathWithinOrEqual(string path, string directory) =>
        string.Equals(
            GetNormalizedFullPath(path),
            GetNormalizedFullPath(directory),
            StringComparison.OrdinalIgnoreCase) ||
        IsPathWithin(path, directory);

    private static string GetNormalizedFullPath(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool FilesystemEntryExists(string path)
    {
        var file = new FileInfo(path);
        var directory = new DirectoryInfo(path);
        return file.Exists ||
            directory.Exists ||
            file.LinkTarget is not null ||
            directory.LinkTarget is not null;
    }

    private static FileFingerprint CaptureFingerprint(string path)
    {
        var file = new FileInfo(path);
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: ComparisonBufferSize,
            FileOptions.SequentialScan);
        string sha256 = Convert.ToHexString(SHA256.HashData(stream));
        file.Refresh();
        return new FileFingerprint(file.Length, file.LastWriteTimeUtc, sha256);
    }

    private static void AssertSourceFingerprintUnchanged(
        string sourcePath,
        FileFingerprint? sourceBefore)
    {
        if (sourceBefore is null)
            return;

        FileFingerprint sourceAfter = CaptureFingerprint(sourcePath);
        Assert.True(
            sourceBefore == sourceAfter,
            $"Stock source '{sourcePath}' changed during qualification. " +
            $"Before: {sourceBefore}. After: {sourceAfter}.");
    }

    private static TResult WithLoadedZone<TResult>(
        string path,
        uint selectedLanguageMask,
        Func<DbLoadSession, LoadedXZone, TResult> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        using var loadSession = new DbLoadSession(
            selectedLanguageMask: selectedLanguageMask);
        LoadedXZone loaded = loadSession.DB_LoadXZone(
            path,
            XZoneFlags.DB_ZONE_DEV);
        return operation(loadSession, loaded);
    }

    private static HeaderEnvelope CaptureHeaderEnvelope(LoadedXZone loaded) =>
        new(
            loaded.Header.PackedStreamOffset,
            loaded.Header.SerializedHeaderLength,
            loaded.Header.SerializedHeaderBytes.ToArray());

    private static void AssertHeaderEnvelopeMatches(
        HeaderEnvelope source,
        HeaderEnvelope candidate,
        string sourcePath,
        string candidatePath)
    {
        Assert.Equal(source.PackedStreamOffset, candidate.PackedStreamOffset);
        Assert.Equal(source.SerializedHeaderLength, candidate.SerializedHeaderLength);
        Assert.True(
            source.SerializedBytes.Length >= 8 && candidate.SerializedBytes.Length >= 8,
            "DB headers must contain FileSize and MaxFileSize dwords.");
        AssertByteSequencesMatch(
            source.SerializedBytes.AsSpan()[..^8],
            candidate.SerializedBytes.AsSpan()[..^8],
            $"DB-header envelope mismatch: source '{sourcePath}', candidate '{candidatePath}'");
    }

    private static void AssertSaveSucceeded(
        FastFileEditingSession editingSession,
        string destinationPath)
    {
        SaveAsResult result = new TransactionalSaveAsService().SaveAs(
            editingSession,
            new SaveAsRequest(destinationPath, AllowOverwrite: false));
        Assert.True(
            result.Succeeded,
            $"Save As failed for '{destinationPath}'.{Environment.NewLine}" +
            string.Join(Environment.NewLine, result.Diagnostics));
        Assert.True(File.Exists(destinationPath));
    }

    private static void WriteCanonicalFastFile(
        ZoneLinkRequest request,
        string destinationPath)
    {
        ZoneLinkResult link = new ZoneLinker().Link(request);
        AssertLinkSucceeded(
            link,
            $"Canonical fixture link failed for '{destinationPath}'");
        FastFilePackagingResult package = new FastFilePackager().PackageGreenfield(
            GetDecodedBytes(link),
            link.LanguageMask,
            link.SelectedLanguageMask,
            link.ImageStreamLanguageTables);
        Assert.True(
            package.Succeeded,
            $"Canonical fixture packaging failed for '{destinationPath}'.{Environment.NewLine}" +
            string.Join(
                Environment.NewLine,
                package.Errors.Select(error => $"{error.Code}: {error.Message}")));
        Assert.True(package.Bytes.HasValue);
        File.WriteAllBytes(destinationPath, package.Bytes.Value.Span);
        Assert.True(File.Exists(destinationPath));
    }

    private static LinkRoot CreateOwnedRoot(string entryId, BaseAsset asset) =>
        new(
            entryId,
            asset.SerializedAssetType,
            LinkRootIntent.Owned,
            AssetKey.FromDefinition(asset),
            asset.SerializedAssetName,
            opaqueHeader: null);

    private static void AssertLinkSucceeded(
        ZoneLinkResult result,
        string description)
    {
        Assert.True(
            result.Succeeded,
            $"{description}.{Environment.NewLine}" +
            string.Join(Environment.NewLine, result.Errors));
        Assert.True(result.DecodedBytes.HasValue);
        Assert.NotNull(result.XFile);
    }

    private static ReadOnlyMemory<byte> GetDecodedBytes(ZoneLinkResult result)
    {
        ReadOnlyMemory<byte>? decoded = result.DecodedBytes;
        Assert.True(decoded.HasValue);
        return decoded.Value;
    }

    private static void AssertXFileLayoutMatches(
        XFile? expected,
        XFile? actual,
        string description)
    {
        if (expected is null || actual is null)
        {
            throw new Xunit.Sdk.XunitException(
                $"Canonical link for '{description}' did not produce an XFile layout.");
        }

        Assert.Equal(expected.Size, actual.Size);
        Assert.Equal(expected.ExternalSize, actual.ExternalSize);
        Assert.Equal(XFile.BlockCount, expected.BlockSizes.Count);
        Assert.Equal(XFile.BlockCount, actual.BlockSizes.Count);
        Assert.Equal(expected.BlockSizes, actual.BlockSizes);
    }

    private static void AssertImageStreamLanguageTablesMatch(
        IReadOnlyList<DbHeaderImageStreamLanguageTable> expected,
        IReadOnlyList<DbHeaderImageStreamLanguageTable> actual,
        string description)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (int languageIndex = 0; languageIndex < expected.Count; languageIndex++)
        {
            DbHeaderImageStreamLanguageTable expectedLanguage = expected[languageIndex];
            DbHeaderImageStreamLanguageTable actualLanguage = actual[languageIndex];
            Assert.Equal(expectedLanguage.SerializedIndex, actualLanguage.SerializedIndex);
            Assert.Equal(expectedLanguage.LanguageMask, actualLanguage.LanguageMask);
            Assert.Equal(
                expectedLanguage.ImageStreamEntries.Length,
                actualLanguage.ImageStreamEntries.Length);
            for (int entryIndex = 0;
                 entryIndex < expectedLanguage.ImageStreamEntries.Length;
                 entryIndex++)
            {
                AssertImageStreamEntryMatches(
                    expectedLanguage.ImageStreamEntries[entryIndex],
                    actualLanguage.ImageStreamEntries[entryIndex],
                    $"Image stream mismatch for '{description}', language " +
                    $"0x{expectedLanguage.LanguageMask:X}, entry {entryIndex}");
            }
        }
    }

    private static void AssertImageStreamEntryMatches(
        DbHeaderImageStreamEntry expected,
        DbHeaderImageStreamEntry actual,
        string description)
    {
        Assert.True(
            expected.FileIndex == actual.FileIndex &&
            expected.SourceStart == actual.SourceStart &&
            expected.SourceEnd == actual.SourceEnd &&
            expected.BlockOffset == actual.BlockOffset &&
            expected.StreamOffset == actual.StreamOffset,
            $"{description}: expected wire fields {expected}, actual {actual}.");
    }

    private static void AssertOrderedRootSubsequence(
        IReadOnlyList<LinkRoot> expectedRoots,
        IReadOnlyList<LinkRoot> candidateRoots,
        string sourcePath)
    {
        LinkRootDescriptor[] expected = expectedRoots
            .Select(CreateRootDescriptor)
            .ToArray();
        LinkRootDescriptor[] candidate = candidateRoots
            .Select(CreateRootDescriptor)
            .ToArray();
        int candidateIndex = 0;
        for (int expectedIndex = 0; expectedIndex < expected.Length; expectedIndex++)
        {
            while (candidateIndex < candidate.Length &&
                candidate[candidateIndex] != expected[expectedIndex])
            {
                candidateIndex++;
            }

            Assert.True(
                candidateIndex < candidate.Length,
                $"Canonical candidate for '{sourcePath}' does not retain source root " +
                $"{expectedIndex} ({expected[expectedIndex]}) as an ordered subsequence.");
            candidateIndex++;
        }
    }

    private static LinkRootDescriptor CreateRootDescriptor(LinkRoot root) =>
        new(
            root.SerializedType,
            root.Intent,
            root.Asset,
            root.OriginalSerializedName,
            root.OpaqueHeader);

    private static void AssertCandidateRootRows(
        LoadedXZone loaded,
        IReadOnlyList<LinkRoot> roots,
        string sourcePath)
    {
        Assert.Equal(roots.Count, loaded.XAssetList.Assets.Count);
        Assert.Equal(roots.Count, loaded.LoadedAssets.Count);
        for (int index = 0; index < roots.Count; index++)
        {
            LinkRoot root = roots[index];
            XAssetListEntrySnapshot row = loaded.XAssetList.Assets[index];
            Assert.Equal(root.SerializedType, row.Type);
            switch (root.Intent)
            {
                case LinkRootIntent.Owned:
                case LinkRootIntent.External:
                    Assert.False(row.IsOpaqueHeader);
                    Assert.Equal(PointerType.Inline, row.AssetPointer.Type);
                    break;
                case LinkRootIntent.Null:
                    Assert.False(row.IsOpaqueHeader);
                    Assert.Equal(PointerType.Null, row.AssetPointer.Type);
                    break;
                case LinkRootIntent.OpaqueNative:
                    Assert.True(row.IsOpaqueHeader);
                    Assert.Equal(root.OpaqueHeader, row.RawHeader);
                    break;
                default:
                    throw new Xunit.Sdk.XunitException(
                        $"Candidate '{sourcePath}' has unknown root intent {root.Intent} at row {index}.");
            }
        }
    }

    private static ImageFileStreamReference EmptyImageFileStreamReference() =>
        new(
            new DbHeaderImageStreamEntry(
                FileIndex: 0,
                SourceStart: 0,
                SourceEnd: 0,
                BlockOffset: 0,
                StreamOffset: 0,
                SerializedOffset: -1),
            byteLength: 0);

    private static void AssertStreamedImageReload(
        string fastFilePath,
        uint selectedLanguageMask,
        IReadOnlyList<ImageFileStreamLanguageReferences> expectedReferences)
    {
        DbHeaderImageStreamLanguageTable[] expectedTables = expectedReferences
            .Select((language, index) => new DbHeaderImageStreamLanguageTable(
                index,
                language.LanguageMask,
                language.References.Select(reference => reference.Entry)))
            .ToArray();
        _ = WithLoadedZone(
            fastFilePath,
            selectedLanguageMask,
            (loadSession, loaded) =>
            {
                Assert.Equal(3u, loaded.Header.LanguageMask);
                Assert.Equal(selectedLanguageMask, loaded.Header.SelectedLanguageMask);
                Assert.Equal(2, loaded.Header.LanguageTables.Length);
                Assert.Equal(4u, loaded.Header.EntryCount);
                AssertImageStreamLanguageTablesMatch(
                    expectedTables,
                    loaded.Header.LanguageTables,
                    $"Reloaded header for selected language 0x{selectedLanguageMask:X}");

                GfxImageAsset loadedImage = Assert.IsType<GfxImageAsset>(
                    Assert.Single(loaded.LoadedAssets).Asset);
                int[] reloadedByteLengths =
                    GfxImageStreamData.ValidateProfileAndComputePartByteCounts(
                        loadedImage.StreamData);
                foreach (ImageFileStreamLanguageReferences language in expectedReferences)
                {
                    Assert.Equal(
                        language.References.Select(reference => reference.ByteLength),
                        reloadedByteLengths);
                }

                LinkAssetPool pool = loadSession.FreezeLinkAssetPool();
                IReadOnlyList<LinkRoot> roots = loaded.FreezeLinkRoots();
                Assert.Single(pool.Providers);
                Assert.Single(roots);
                var request = new ZoneLinkRequest(
                    pool,
                    roots,
                    loaded.Header.LanguageMask,
                    loaded.Header.SelectedLanguageMask);
                ZoneLinkResult link = new ZoneLinker().Link(request);
                AssertLinkSucceeded(
                    link,
                    $"Streamed image relink failed for selected language 0x{selectedLanguageMask:X}");
                AssertImageStreamLanguageTablesMatch(
                    expectedTables,
                    link.ImageStreamLanguageTables,
                    $"Reloaded link for selected language 0x{selectedLanguageMask:X}");
                return true;
            });
    }

    private static void AssertByteSequencesMatch(
        ReadOnlySpan<byte> source,
        ReadOnlySpan<byte> candidate,
        string description)
    {
        Assert.True(
            source.Length == candidate.Length,
            $"{description}: source is {source.Length} bytes, candidate is {candidate.Length} bytes.");

        for (int index = 0; index < source.Length; index++)
        {
            if (source[index] != candidate[index])
            {
                throw new Xunit.Sdk.XunitException(
                    $"{description} at offset {index}: source is 0x{source[index]:X2}, " +
                    $"candidate is 0x{candidate[index]:X2}.");
            }
        }
    }

    private sealed class CallbackCandidateValidator(
        Func<string, CancellationToken, IReadOnlyList<string>> validate)
        : ITransactionalSaveCandidateValidator
    {
        public IReadOnlyList<string> Validate(
            string candidatePath,
            CancellationToken cancellationToken = default) =>
            validate(candidatePath, cancellationToken);
    }

    private sealed record FileFingerprint(
        long Length,
        DateTime LastWriteTimeUtc,
        string Sha256);

    private sealed record SourceLayoutReplay(
        byte[] DecodedZone,
        HeaderEnvelope Header,
        byte[] PackagedFastFile);

    private sealed record HeaderEnvelope(
        int PackedStreamOffset,
        int SerializedHeaderLength,
        byte[] SerializedBytes);

    private readonly record struct LinkRootDescriptor(
        XAssetType SerializedType,
        LinkRootIntent Intent,
        AssetKey? Asset,
        string? OriginalSerializedName,
        int? OpaqueHeader);
}

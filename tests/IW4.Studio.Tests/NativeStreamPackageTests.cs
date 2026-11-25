using IW4.Game.Assets.Image;
using IW4.Game.Database;
using IW4.Game.Database.Streaming;
using IW4.Game.Zone;
using IW4.Linker.Contracts;
using IW4.Linker.Packaging;
using IW4.Runtime.Assets.Images;
using IW4.Studio.Documents;
using IW4.Streaming.Images;
using Xunit;
using System.Collections.Concurrent;

namespace IW4.Studio.Tests;

public sealed class NativeStreamPackageTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("IW4.NativeStreams.");

    [Fact]
    public void Repackage_appends_frames_and_preserves_each_language_payload()
    {
        Fixture fixture = CreateFixture("source");
        using var source = Open(fixture.FastFilePath);
        GfxImageAsset image = Assert.IsType<GfxImageAsset>(source.LoadedZone.LoadedAssets
            .Single(result => result.Asset is GfxImageAsset).Asset);
        LinkAssetProviderSource captured = new(image, imageStreamReferences: fixture.References);
        var result = NativeStreamPackageBuilder.Repackage(
            [new(AssetKey.FromDefinition(image), image, source.LoadedZone, captured)],
            languageMask: 3,
            existingImages: fixture.PackageBytes);

        Assert.NotNull(result.ImagePackage);
        Assert.True(result.ImagePackage!.AsSpan(0, fixture.PackageBytes.Length).SequenceEqual(fixture.PackageBytes));
        LinkAssetProviderSource rebased = Assert.Single(result.Sources);
        string candidatePath = Path.Combine(_directory.FullName, "candidate.ff");
        File.WriteAllBytes(Path.ChangeExtension(candidatePath, ".pak"), result.ImagePackage!);
        DbHeaderImageStreamLanguageTable[] tables = rebased.ImageStreamReferences
            .Select((language, index) => new DbHeaderImageStreamLanguageTable(index,
                language.LanguageMask, language.References.Select(reference => reference.Entry)))
            .ToArray();
        var header = Reheader(source.LoadedZone.Header, tables);
        using var resolver = new GfxImageStreamResolver(header, candidatePath);
        Assert.True(resolver.TryReadStreamParts(image, 1, out var english, out string reason1), reason1);
        Assert.True(resolver.TryReadStreamParts(image, 2, out var french, out string reason2), reason2);
        Assert.Equal(fixture.LanguageOneParts.SelectMany(part => part), english.SelectMany(part => part));
        Assert.Equal(fixture.LanguageTwoParts.SelectMany(part => part), french.SelectMany(part => part));
    }

    [Fact]
    public void Save_failure_after_sidecar_commit_restores_previous_destination_package()
    {
        Fixture fixture = CreateFixture("save-source");
        FastFileWorkspace source = Open(fixture.FastFilePath);
        using var session = new FastFileEditingSession(source);
        string destination = Path.Combine(_directory.FullName, "save-copy.ff");
        string sidecar = Path.ChangeExtension(destination, ".pak");
        byte[] sentinel = [0x1, 0x22, 0xFE, 0x7A];
        File.WriteAllBytes(sidecar, sentinel);

        SaveAsResult saved = new TransactionalSaveAsService(fileSystem: new FailFastFileCommitFileSystem())
            .SaveAs(session, new SaveAsRequest(destination, AllowOverwrite: true));

        Assert.False(saved.Succeeded);
        Assert.Equal(sentinel, File.ReadAllBytes(sidecar));
        Assert.False(File.Exists(destination));
    }

    [Fact]
    public void Save_without_named_streams_does_not_create_sidecars()
    {
        using var session = new FastFileEditingSession(
            new FastFileDocumentService().CreateBlank(languageMask: 1, selectedLanguageMask: 1));
        string destination = Path.Combine(_directory.FullName, "blank.ff");
        SaveAsResult saved = new TransactionalSaveAsService().SaveAs(
            session, new SaveAsRequest(destination, AllowOverwrite: false));
        Assert.True(saved.Succeeded, string.Join(Environment.NewLine, saved.Diagnostics));
        Assert.False(File.Exists(Path.ChangeExtension(destination, ".pak")));
        Assert.False(File.Exists(Path.Combine(_directory.FullName, "blank_snd.pak")));
    }

    [Fact]
    public async Task Concurrent_saves_to_the_same_destination_are_serialized()
    {
        var first = new FastFileEditingSession(
            new FastFileDocumentService().CreateBlank(languageMask: 1, selectedLanguageMask: 1));
        var second = new FastFileEditingSession(
            new FastFileDocumentService().CreateBlank(languageMask: 1, selectedLanguageMask: 1));
        using (first)
        using (second)
        using (var fileSystem = new BlockingFastFileCommitFileSystem())
        {
            string destination = Path.Combine(_directory.FullName, "shared.ff");
            var service = new TransactionalSaveAsService(fileSystem: fileSystem);
            Task<SaveAsResult> firstSave = Task.Run(() => service.SaveAs(first,
                new SaveAsRequest(destination, AllowOverwrite: true)));
            Assert.True(fileSystem.FirstCommitEntered.Wait(TimeSpan.FromSeconds(5)));
            Task<SaveAsResult> secondSave = Task.Run(() => service.SaveAs(second,
                new SaveAsRequest(destination, AllowOverwrite: true)));
            await Task.Delay(150);
            Assert.False(fileSystem.SecondCommitEntered.IsSet);
            fileSystem.ReleaseFirstCommit.Set();
            Assert.True((await firstSave).Succeeded);
            Assert.True((await secondSave).Succeeded);
            Assert.Equal(2, fileSystem.FastFileCommitCount);
        }
    }

    private Fixture CreateFixture(string stem)
    {
        byte[][] languageOne = Enumerable.Range(0, GfxImageStreamData.EntryCount)
            .Select(index => Enumerable.Repeat((byte)(0x10 + index), 128).ToArray()).ToArray();
        byte[][] languageTwo = Enumerable.Range(0, GfxImageStreamData.EntryCount)
            .Select(index => Enumerable.Repeat((byte)(0x80 + index), 128).ToArray()).ToArray();
        ReadOnlyMemory<byte>[] payloads = languageOne.Concat(languageTwo).Select(bytes => (ReadOnlyMemory<byte>)bytes).ToArray();
        ImageFilePackage package = new ImageFilePackager().Package(
            DbHeaderImageStreamEntry.NamedFileIndex,
            payloads);
        ImageFileStreamLanguageReferences[] references =
        [
            new(1, package.References.Take(4)),
            new(2, package.References.Skip(4).Take(4))
        ];
        var image = new GfxImageAsset
        {
            Name = "streams/test",
            Format = 0x85,
            TextureControl1 = 0x1aae4,
            LevelCount = 4,
            DimensionCount = GfxImageDimension.TwoDimensional,
            MapType = MapType.TwoDimensional,
            Width = 8,
            Height = 8,
            Depth = 1,
            BaseWidth = 8,
            BaseHeight = 8,
            BaseDepth = 1,
            BaseLevelCount = 4,
            CardMemory = 512,
            StreamImageIndex = 0,
            StreamData = Enumerable.Range(1, 4)
                .Select(index => new GfxImageStreamData((ushort)Math.Max(1, 8 >> (index - 1)),
                    (ushort)Math.Max(1, 8 >> (index - 1)), (uint)(index * 128)))
                .ToArray()
        };
        var source = new LinkAssetProviderSource(image, imageStreamReferences: references);
        var request = new ZoneLinkRequest(
            new LinkAssetPool([source]),
            [new LinkRoot("image", image.SerializedAssetType, LinkRootIntent.Owned,
                AssetKey.FromDefinition(image), image.Name, null)],
            languageMask: 3,
            selectedLanguageMask: 1,
            scriptStrings: []);
        byte[] fastFile = AssetPackageFixture.FastFile(request);
        string fastFilePath = Path.Combine(_directory.FullName, stem + ".ff");
        File.WriteAllBytes(fastFilePath, fastFile);
        File.WriteAllBytes(Path.ChangeExtension(fastFilePath, ".pak"), package.Bytes.ToArray());
        return new Fixture(fastFilePath, package.Bytes.ToArray(), references, languageOne, languageTwo);
    }

    private static FastFileWorkspace Open(string path) =>
        new FastFileDocumentService().Open(new(path, Isolated.Instance));

    private static IW4.Game.Database.DbHeader Reheader(
        IW4.Game.Database.DbHeader source,
        IReadOnlyList<DbHeaderImageStreamLanguageTable> tables) =>
        new(source.Magic, source.Version, source.AllowOnlineUpdate,
            source.FileCreationTimeRaw, source.LanguageMask, source.SelectedLanguageMask,
            checked((uint)tables.Count), source.SelectedLanguageIndex, source.EntryCount,
            tables, source.FileSize, source.MaxFileSize, source.SerializedHeaderOffset,
            source.SerializedHeaderBytes.AsSpan(), source.PackedStreamOffset, source.SourceFileLength);

    public void Dispose() => _directory.Delete(recursive: true);

    private sealed record Fixture(
        string FastFilePath,
        byte[] PackageBytes,
        ImageFileStreamLanguageReferences[] References,
        byte[][] LanguageOneParts,
        byte[][] LanguageTwoParts);

    private sealed class FailFastFileCommitFileSystem : ITransactionalSaveFileSystem
    {
        private readonly TransactionalSaveFileSystem _inner = new();
        public string GetFullPath(string path) => _inner.GetFullPath(path);
        public string ResolveExistingDirectoryPath(string path) => _inner.ResolveExistingDirectoryPath(path);
        public string ResolveExistingFilePath(string path) => _inner.ResolveExistingFilePath(path);
        public bool DirectoryExists(string path) => _inner.DirectoryExists(path);
        public bool FileExists(string path) => _inner.FileExists(path);
        public TransactionalSaveDirectory CreateTemporarySiblingDirectory(string destinationPath) => _inner.CreateTemporarySiblingDirectory(destinationPath);
        public TransactionalSaveFile WriteTemporaryFile(TransactionalSaveDirectory directory, string fileName, ReadOnlySpan<byte> bytes) =>
            _inner.WriteTemporaryFile(directory, fileName, bytes);
        public TransactionalSaveFile WriteTemporaryCopy(TransactionalSaveDirectory directory, string fileName, string sourcePath) =>
            _inner.WriteTemporaryCopy(directory, fileName, sourcePath);
        public void CommitTemporaryFile(TransactionalSaveFile temporaryFile, string destinationPath, bool overwrite)
        {
            if (destinationPath.EndsWith(".ff", StringComparison.OrdinalIgnoreCase))
                throw new IOException("Injected fastfile commit failure.");
            _inner.CommitTemporaryFile(temporaryFile, destinationPath, overwrite);
        }
        public void RollbackFile(TransactionalSaveFile file) => _inner.RollbackFile(file);
        public void DeleteTemporaryDirectory(TransactionalSaveDirectory directory) => _inner.DeleteTemporaryDirectory(directory);
    }

    private sealed class BlockingFastFileCommitFileSystem : ITransactionalSaveFileSystem, IDisposable
    {
        private readonly TransactionalSaveFileSystem _inner = new();
        private int _fastFileCommitCount;
        public ManualResetEventSlim FirstCommitEntered { get; } = new();
        public ManualResetEventSlim SecondCommitEntered { get; } = new();
        public ManualResetEventSlim ReleaseFirstCommit { get; } = new();
        public int FastFileCommitCount => Volatile.Read(ref _fastFileCommitCount);
        public string GetFullPath(string path) => _inner.GetFullPath(path);
        public string ResolveExistingDirectoryPath(string path) => _inner.ResolveExistingDirectoryPath(path);
        public string ResolveExistingFilePath(string path) => _inner.ResolveExistingFilePath(path);
        public bool DirectoryExists(string path) => _inner.DirectoryExists(path);
        public bool FileExists(string path) => _inner.FileExists(path);
        public TransactionalSaveDirectory CreateTemporarySiblingDirectory(string destinationPath) => _inner.CreateTemporarySiblingDirectory(destinationPath);
        public TransactionalSaveFile WriteTemporaryFile(TransactionalSaveDirectory directory, string fileName, ReadOnlySpan<byte> bytes) =>
            _inner.WriteTemporaryFile(directory, fileName, bytes);
        public TransactionalSaveFile WriteTemporaryCopy(TransactionalSaveDirectory directory, string fileName, string sourcePath) =>
            _inner.WriteTemporaryCopy(directory, fileName, sourcePath);
        public void CommitTemporaryFile(TransactionalSaveFile temporaryFile, string destinationPath, bool overwrite)
        {
            if (destinationPath.EndsWith(".ff", StringComparison.OrdinalIgnoreCase))
            {
                int commit = Interlocked.Increment(ref _fastFileCommitCount);
                if (commit == 1)
                {
                    FirstCommitEntered.Set();
                    if (!ReleaseFirstCommit.Wait(TimeSpan.FromSeconds(10)))
                        throw new TimeoutException("Second save did not release the first commit.");
                }
                else if (commit == 2)
                {
                    SecondCommitEntered.Set();
                }
            }
            _inner.CommitTemporaryFile(temporaryFile, destinationPath, overwrite);
        }
        public void RollbackFile(TransactionalSaveFile file) => _inner.RollbackFile(file);
        public void DeleteTemporaryDirectory(TransactionalSaveDirectory directory) => _inner.DeleteTemporaryDirectory(directory);
        public void Dispose()
        {
            FirstCommitEntered.Dispose();
            SecondCommitEntered.Dispose();
            ReleaseFirstCommit.Dispose();
        }
    }
}

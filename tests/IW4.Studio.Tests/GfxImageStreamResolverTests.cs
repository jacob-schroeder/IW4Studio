using IW4.Assets.Assets.Image;
using IW4.FastFiles.Database;
using IW4.FastFiles.Streaming.Images;
using IW4.Linker.Packaging;
using Xunit;

namespace IW4.Studio.Tests;

public sealed class GfxImageStreamResolverTests
{
    [Fact]
    public void TryReadBestPayload_orders_ushort_dimensions_by_non_overflowing_area()
    {
        using var fixture = new StreamPackageFixture();
        using GfxImageStreamResolver resolver = fixture.CreateResolver();

        bool resolved = resolver.TryReadBestPayload(
            fixture.Image,
            out byte[] payload,
            out int width,
            out int height,
            out string reason);

        Assert.True(resolved, reason);
        Assert.Equal(ushort.MaxValue, width);
        Assert.Equal(ushort.MaxValue, height);
        Assert.Equal(fixture.LargestPayload, payload);
    }

    [Fact]
    public void TryReadMipPayloads_orders_ushort_dimensions_by_non_overflowing_area()
    {
        using var fixture = new StreamPackageFixture();
        using GfxImageStreamResolver resolver = fixture.CreateResolver();

        bool resolved = resolver.TryReadMipPayloads(
            fixture.Image,
            out IReadOnlyList<GfxImageStreamMipPayload> mips,
            out string reason);

        Assert.True(resolved, reason);
        Assert.Collection(
            mips,
            mip =>
            {
                Assert.Equal(ushort.MaxValue, mip.Width);
                Assert.Equal(ushort.MaxValue, mip.Height);
                Assert.Equal(fixture.LargestPayload, mip.Payload);
            },
            mip =>
            {
                Assert.Equal(ushort.MaxValue / 2, mip.Width);
                Assert.Equal(ushort.MaxValue / 2, mip.Height);
                Assert.Equal(fixture.SmallerPayload, mip.Payload);
            });
    }

    private sealed class StreamPackageFixture : IDisposable
    {
        private const int StreamPartByteCount = 0x80;
        private readonly string _temporaryDirectory;

        public StreamPackageFixture()
        {
            _temporaryDirectory = Directory.CreateTempSubdirectory(
                "IW4.Studio.Tests.GfxImageStreamResolver.").FullName;
            FastFilePath = Path.Combine(_temporaryDirectory, "resolver-test.ff");

            SmallerPayload = Enumerable.Repeat((byte)0x22, StreamPartByteCount).ToArray();
            LargestPayload = Enumerable.Repeat((byte)0x77, StreamPartByteCount).ToArray();
            ImageFilePackage package = new ImageFilePackager().Package(
                fileIndex: 1,
                [
                    SmallerPayload,
                    LargestPayload,
                    ReadOnlyMemory<byte>.Empty,
                    ReadOnlyMemory<byte>.Empty
                ]);
            File.WriteAllBytes(
                Path.Combine(_temporaryDirectory, "imagefile1.pak"),
                package.Bytes.ToArray());

            Image = new GfxImageAsset
            {
                StreamImageIndex = 0,
                StreamData =
                [
                    new GfxImageStreamData(
                        ushort.MaxValue / 2,
                        ushort.MaxValue / 2,
                        StreamPartByteCount),
                    new GfxImageStreamData(
                        ushort.MaxValue,
                        ushort.MaxValue,
                        StreamPartByteCount * 2),
                    new GfxImageStreamData(0, 0, 0),
                    new GfxImageStreamData(0, 0, 0)
                ],
                StreamEntries = package.References
                    .Select(reference => reference.Entry)
                    .ToArray()
            };
        }

        public string FastFilePath { get; }
        public GfxImageAsset Image { get; }
        public byte[] SmallerPayload { get; }
        public byte[] LargestPayload { get; }

        public GfxImageStreamResolver CreateResolver() =>
            new(CreateHeader(), FastFilePath);

        public void Dispose() =>
            Directory.Delete(_temporaryDirectory, recursive: true);

        private static DbHeader CreateHeader()
        {
            const uint languageMask = 1;
            return new DbHeader(
                magic: DbHeader.UnsignedMagic,
                version: XFileVersion.ModernWarfare2,
                allowOnlineUpdate: false,
                fileCreationTimeRaw: 0,
                languageMask,
                selectedLanguageMask: languageMask,
                languageCount: 1,
                selectedLanguageIndex: 0,
                entryCount: 0,
                languageTables:
                [
                    new DbHeaderImageStreamLanguageTable(
                        serializedIndex: 0,
                        languageMask,
                        imageStreamEntries: [])
                ],
                fileSize: 0,
                maxFileSize: 0,
                serializedHeaderOffset: 0,
                serializedHeaderBytes: [],
                packedStreamOffset: 0,
                sourceFileLength: 0);
        }
    }
}

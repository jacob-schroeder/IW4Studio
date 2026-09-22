using IW4.Assets.Assets.Sound;
using IW4.FastFiles.Loaders.Streaming.Sound;
using IW4.FastFiles.Streaming.Sound;
using Xunit;

namespace IW4.Studio.Tests;

public sealed class StreamedSoundResolverTests
{
    private const int MaximumPayloadByteCount = 16 * 1024 * 1024;

    [Fact]
    public void TryReadPayload_rejects_length_above_product_limit_before_package_lookup()
    {
        using var fixture = new StreamPackageFixture();
        using StreamedSoundResolver resolver = fixture.CreateResolver();
        StreamedSound sound = fixture.CreateSound(
            streamOffset: 0,
            streamLength: MaximumPayloadByteCount + 1);

        bool resolved = resolver.TryReadPayload(
            sound,
            out byte[] payload,
            out string reason);

        Assert.False(resolved);
        Assert.Empty(payload);
        Assert.Equal(
            $"sound stream length {MaximumPayloadByteCount + 1} exceeds the {MaximumPayloadByteCount}-byte payload limit",
            reason);
    }

    [Fact]
    public void TryReadPayload_accepts_maximum_product_length_before_package_lookup()
    {
        using var fixture = new StreamPackageFixture();
        using StreamedSoundResolver resolver = fixture.CreateResolver();
        StreamedSound sound = fixture.CreateSound(
            streamOffset: 0,
            streamLength: MaximumPayloadByteCount);

        bool resolved = resolver.TryReadPayload(
            sound,
            out byte[] payload,
            out string reason);

        Assert.False(resolved);
        Assert.Empty(payload);
        Assert.StartsWith("missing sound stream package", reason);
    }

    [Theory]
    [InlineData(-1, 1, "sound stream offset -1 is negative")]
    [InlineData(0, -1, "sound stream length -1 is negative")]
    [InlineData(0, 0, "sound stream length is zero")]
    public void TryReadPayload_preserves_invalid_metadata_diagnostics(
        int streamOffset,
        int streamLength,
        string expectedReason)
    {
        using var fixture = new StreamPackageFixture();
        using StreamedSoundResolver resolver = fixture.CreateResolver();

        bool resolved = resolver.TryReadPayload(
            fixture.CreateSound(streamOffset, streamLength),
            out byte[] payload,
            out string reason);

        Assert.False(resolved);
        Assert.Empty(payload);
        Assert.Equal(expectedReason, reason);
    }

    [Theory]
    [InlineData(4, 1, "sound stream offset 0x4 is outside packfile1.pak")]
    [InlineData(2, 3, "sound stream range 0x2-0x5 extends past end of packfile1.pak")]
    public void TryReadPayload_preserves_invalid_package_range_diagnostics(
        int streamOffset,
        int streamLength,
        string expectedReason)
    {
        using var fixture = new StreamPackageFixture();
        fixture.WritePackage([0x10, 0x20, 0x30, 0x40]);
        using StreamedSoundResolver resolver = fixture.CreateResolver();

        bool resolved = resolver.TryReadPayload(
            fixture.CreateSound(streamOffset, streamLength),
            out byte[] payload,
            out string reason);

        Assert.False(resolved);
        Assert.Empty(payload);
        Assert.Equal(expectedReason, reason);
    }

    [Fact]
    public void StreamedSoundPayloadResolver_preserves_supported_payload_bytes()
    {
        using var fixture = new StreamPackageFixture();
        fixture.WritePackage([0x10, 0x20, 0x30, 0x40, 0x50]);
        using StreamedSoundResolver streams = fixture.CreateResolver();
        var resolver = new StreamedSoundPayloadResolver(streams);

        bool resolved = resolver.TryResolvePayload(
            fixture.CreateSound(streamOffset: 1, streamLength: 3),
            out byte[] payload,
            out string reason);

        Assert.True(resolved, reason);
        Assert.Equal([0x20, 0x30, 0x40], payload);
        Assert.Empty(reason);
    }

    private sealed class StreamPackageFixture : IDisposable
    {
        private readonly string _temporaryDirectory;

        public StreamPackageFixture()
        {
            _temporaryDirectory = Directory.CreateTempSubdirectory(
                "IW4.Studio.Tests.StreamedSoundResolver.").FullName;
            FastFilePath = Path.Combine(_temporaryDirectory, "resolver-test.ff");
        }

        public string FastFilePath { get; }

        public StreamedSoundResolver CreateResolver() => new(FastFilePath);

        public StreamedSound CreateSound(int streamOffset, int streamLength) =>
            new()
            {
                FileIndex = 1,
                Source = new StreamedSoundFileSource
                {
                    StreamFileOffset = streamOffset,
                    StreamFileLength = streamLength
                }
            };

        public void WritePackage(byte[] bytes) =>
            File.WriteAllBytes(
                Path.Combine(_temporaryDirectory, "packfile1.pak"),
                bytes);

        public void Dispose() =>
            Directory.Delete(_temporaryDirectory, recursive: true);
    }
}

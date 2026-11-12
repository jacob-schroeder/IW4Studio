using System.Numerics;
using IW4.Formats.SourceFormat.XAnim;
using IW4.Game.Assets.XAnim;
using IW4.Game.ScriptStrings;
using Xunit;

namespace IW4.Studio.Tests;

public sealed class XAnimExchangeTests
{
    private static readonly XAnimExchange Exchange = new();

    [Fact]
    public void Decode_reconstructs_native_bucket_order_and_clip_metadata()
    {
        XAnimPlaybackClip clip = Exchange.Decode(CreateNativeAsset());

        Assert.Equal(4, clip.NumFrames);
        Assert.Equal(30f, clip.Framerate);
        Assert.True(clip.Looped);
        Assert.Equal(7, clip.BoneCount);
        Assert.Equal(["none", "simple", "normal", "precision", "simple_const", "normal_const", "precision_const"], clip.BoneNames);
        Assert.Equal(4.0 / 30.0, clip.DurationSeconds, 6);
    }

    [Fact]
    public void Decode_samples_native_simple_normal_and_precision_quaternions()
    {
        XAnimPlaybackClip clip = Exchange.Decode(CreateNativeAsset());
        XAnimLocalBoneTransform[] first = Sample(clip, 0);
        XAnimLocalBoneTransform[] middle = Sample(clip, 2);
        XAnimLocalBoneTransform[] last = Sample(clip, 4);

        AssertRotation(first[0], 0, 0, 0, 1);
        AssertRotation(first[1], 0, 0, 0, 1);
        AssertRotation(first[2], 0, 0, 0, 1);
        AssertRotation(first[3], 0, 0, 0, 1);
        const float half = 0.70710677f;
        AssertRotation(middle[1], 0, 0, half, half);
        AssertRotation(middle[2], half, 0, 0, half);
        AssertRotation(middle[3], 0, half, 0, half);
        AssertRotation(last[1], 0, 0, 1, 0);
        AssertRotation(last[2], 1, 0, 0, 0);
        AssertRotation(last[3], 0, 1, 0, 0);
        AssertRotation(last[4], 0, 0, 1, 0);
        AssertRotation(last[5], 1, 0, 0, 0);
        AssertRotation(last[6], 0, 1, 0, 0);
    }

    [Fact]
    public void Decode_samples_native_small_large_constant_and_absent_translations()
    {
        XAnimPlaybackClip clip = Exchange.Decode(CreateNativeAsset());
        XAnimLocalBoneTransform[] first = Sample(clip, 0);
        XAnimLocalBoneTransform[] middle = Sample(clip, 2);
        XAnimLocalBoneTransform[] last = Sample(clip, 4);

        AssertVector(first[0].Translation, 0, 0, 0);
        AssertVector(first[1].Translation, 1, 2, 3);
        AssertVector(middle[1].Translation, 2, 5, 11);
        AssertVector(last[1].Translation, 3, 8, 19);
        AssertVector(first[2].Translation, 11, 21, 36);
        AssertVector(middle[2].Translation, 10.5f, 21.5f, 38);
        AssertVector(last[2].Translation, 10, 22, 40);
        AssertVector(middle[3].Translation, -2, 3, 7);
        AssertVector(middle[4].Translation, 0, 0, 0);
    }

    [Theory]
    [InlineData(-100f, 0f)]
    [InlineData(100f, 4f)]
    public void Sample_clamps_outside_native_frame_range(float requested, float expected)
    {
        XAnimPlaybackClip clip = Exchange.Decode(CreateNativeAsset());
        XAnimLocalBoneTransform[] actual = Sample(clip, requested);
        XAnimLocalBoneTransform[] endpoint = Sample(clip, expected);

        Assert.Equal(endpoint[1], actual[1]);
        Assert.Equal(endpoint[2], actual[2]);
    }

    [Fact]
    public void Sample_rejects_nonfinite_frames_and_short_destination()
    {
        XAnimPlaybackClip clip = Exchange.Decode(CreateNativeAsset());

        Assert.Throws<ArgumentOutOfRangeException>(() => clip.Sample(float.NaN, new XAnimLocalBoneTransform[7]));
        Assert.Throws<ArgumentOutOfRangeException>(() => clip.Sample(float.PositiveInfinity, new XAnimLocalBoneTransform[7]));
        Assert.Throws<ArgumentException>(() => clip.Sample(0, new XAnimLocalBoneTransform[6]));
    }

    [Fact]
    public void Decode_detaches_sampled_tracks_and_bone_names_from_mutable_source()
    {
        byte[] dataBytes = [0];
        int[] dataInts = VecBits(1, 2, 3);
        byte[] countBuckets = new byte[10];
        countBuckets[8] = 1;
        ScriptStringReference[] names = [Name("before")];
        XAnimPartsAsset asset = new()
        {
            Name = "detached", NumFrames = 1, BoneNameCount = 1, DeltaFlags = 1,
            BoneCounts = countBuckets, Names = names, DataByteCount = 1, DataIntCount = 3,
            PackedDataStreams = new XAnimPackedDataStreams { QuantizedBytes = dataBytes, QuantizedInts = dataInts },
            Indices = new XAnimFrameIndexStream { IsByteEncoded = true }
        };
        XAnimPlaybackClip clip = Exchange.Decode(asset);
        dataBytes[0] = 99;
        dataInts[0] = BitConverter.SingleToInt32Bits(99);
        names[0] = Name("after");
        countBuckets[8] = 0;

        Assert.Equal("before", Assert.Single(clip.BoneNames));
        AssertVector(Assert.Single(Sample(clip, 1)).Translation, 1, 2, 3);
    }

    [Fact]
    public void Unlink_writes_known_minimal_v17_bytes_and_Read_restores_pose()
    {
        using TempSource source = new();
        XAnimPartsAsset asset = CreateMinimalNativeAsset();

        IReadOnlyList<string> paths = Exchange.Unlink(source.Directory, asset);
        byte[] actual = File.ReadAllBytes(Path.Combine(source.Directory, "xanim", "sample"));

        Assert.Single(paths);
        Assert.Equal(new byte[]
        {
            17, 0, 5, 0, 1, 0, 0, 0, 30, 0,
            0, 1, (byte)'r', (byte)'o', (byte)'o', (byte)'t', 0,
            0, 0, 0, 0, 0
        }, actual);
        AssertVector(Assert.Single(Sample(Exchange.Read(source.Directory, "sample"), 2)).Translation, 0, 0, 0);
    }

    [Fact]
    public void Unlink_and_Read_preserve_native_sampled_poses()
    {
        using TempSource source = new();
        XAnimPlaybackClip direct = Exchange.Decode(CreateNativeAsset());
        Exchange.Unlink(source.Directory, CreateNativeAsset());
        XAnimPlaybackClip compiled = Exchange.Read(source.Directory, "sample");

        Assert.Equal(direct.BoneNames, compiled.BoneNames);
        foreach (float frame in new[] { 0f, 2f, 4f })
        {
            XAnimLocalBoneTransform[] expected = Sample(direct, frame);
            XAnimLocalBoneTransform[] actual = Sample(compiled, frame);
            for (int bone = 0; bone < expected.Length; bone++)
            {
                AssertVectorNear(actual[bone].Translation, expected[bone].Translation, 0.002f);
                AssertRotationNear(actual[bone].Rotation, expected[bone].Rotation, 0.002f);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unlink_selects_v17_2d_or_v18_3d_delta_bytes(bool threeD)
    {
        using TempSource source = new();
        Exchange.Unlink(source.Directory, CreateMinimalNativeAsset(delta3D: threeD));

        byte[] expected = threeD
            ? new byte[]
            {
                18, 0, 5, 0, 1, 0, 4, 0, 30, 0,
                1, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                0, 1, (byte)'r', (byte)'o', (byte)'o', (byte)'t', 0,
                0, 0, 0, 0, 0
            }
            : new byte[]
            {
                17, 0, 5, 0, 1, 0, 2, 0, 30, 0,
                1, 0, 0, 0, 0, 0,
                0, 1, (byte)'r', (byte)'o', (byte)'o', (byte)'t', 0,
                0, 0, 0, 0, 0
            };
        Assert.Equal(expected, File.ReadAllBytes(Path.Combine(source.Directory, "xanim", "sample")));
    }

    [Fact]
    public void Read_accepts_sparse_byte_indices_and_interpolates_at_known_frame()
    {
        using TempSource source = new();
        source.Write(CompiledBone(4, (writer) =>
        {
            writer.Write((ushort)0); // no rotation
            writer.Write((ushort)2); // two small translation keys
            writer.Write((byte)1);
            writer.Write((byte)3);
            writer.Write((byte)1);
            WriteVec(writer, 10, 0, 0);
            WriteVec(writer, 255, 0, 0); // 255 * (1 / 255) = 1 per key unit
            writer.Write(new byte[] { 0, 0, 0, 4, 0, 0 });
        }));

        XAnimPlaybackClip clip = Exchange.Read(source.Directory, "sample");
        AssertVector(Sample(clip, 0)[0].Translation, 10, 0, 0);
        AssertVector(Sample(clip, 2)[0].Translation, 12, 0, 0);
        AssertVector(Sample(clip, 4)[0].Translation, 14, 0, 0);
    }

    [Fact]
    public void Read_accepts_sparse_ushort_indices_and_unsigned_large_values()
    {
        using TempSource source = new();
        source.Write(CompiledBone(300, writer =>
        {
            writer.Write((ushort)0);
            writer.Write((ushort)2);
            writer.Write((ushort)10);
            writer.Write((ushort)290);
            writer.Write((byte)0);
            WriteVec(writer, 1, 0, 0);
            WriteVec(writer, 65535, 0, 0);
            writer.Write((short)-1); writer.Write((short)0); writer.Write((short)0);
            writer.Write((short)0); writer.Write((short)0); writer.Write((short)0);
        }));

        XAnimPlaybackClip clip = Exchange.Read(source.Directory, "sample");
        AssertVectorNear(Sample(clip, 10)[0].Translation, new Vector3(65536, 0, 0), 0.1f);
        AssertVector(Sample(clip, 290)[0].Translation, 1, 0, 0);
    }

    [Fact]
    public void Read_derives_full_indices_without_index_bytes()
    {
        using TempSource source = new();
        source.Write(CompiledBone(2, writer =>
        {
            writer.Write((ushort)0);
            writer.Write((ushort)3); // all 0,1,2 frames; no indices follow
            writer.Write((byte)1);
            WriteVec(writer, 0, 0, 0);
            WriteVec(writer, 255, 0, 0);
            writer.Write(new byte[] { 0, 0, 0, 2, 0, 0, 4, 0, 0 });
        }));

        AssertVector(Sample(Exchange.Read(source.Directory, "sample"), 1)[0].Translation, 2, 0, 0);
    }

    [Theory]
    [InlineData((ushort)17, (byte)2)]
    [InlineData((ushort)18, (byte)4)]
    public void Read_accepts_v17_2d_and_v18_3d_delta_without_applying_it_to_pose(ushort version, byte deltaFlag)
    {
        using TempSource source = new();
        source.Write(CompiledBone(4, writer =>
        {
            writer.Write((ushort)0);
            writer.Write((ushort)0);
        }, version, deltaFlag, writer =>
        {
            writer.Write((ushort)1);
            writer.Write((short)0);
            if (version == 18)
            {
                writer.Write((short)0);
                writer.Write((short)0);
            }
            writer.Write((ushort)1);
            WriteVec(writer, 100, 200, 300);
        }));

        XAnimPlaybackClip clip = Exchange.Read(source.Directory, "sample");
        AssertRotation(Assert.Single(Sample(clip, 4)), 0, 0, 0, 1);
        AssertVector(Assert.Single(Sample(clip, 4)).Translation, 0, 0, 0);
    }

    [Theory]
    [InlineData((ushort)16, (byte)0)]
    [InlineData((ushort)17, (byte)4)]
    [InlineData((ushort)18, (byte)6)]
    [InlineData((ushort)18, (byte)128)]
    public void Read_rejects_unsupported_version_and_invalid_flag_combinations(ushort version, byte flags)
    {
        using TempSource source = new();
        source.Write(CompiledBone(4, writer => { writer.Write((ushort)0); writer.Write((ushort)0); }, version, flags));

        Assert.Throws<InvalidDataException>(() => Exchange.Read(source.Directory, "sample"));
    }

    [Theory]
    [InlineData(new byte[] { 17, 0, 0 })]
    [InlineData(new byte[] { 17, 0, 5, 0, 1, 0, 0, 0, 30, 0, 0 })]
    public void Read_rejects_truncated_compiled_data(byte[] bytes)
    {
        using TempSource source = new();
        source.Write(bytes);

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => Exchange.Read(source.Directory, "sample"));
        Assert.Contains("truncated", error.Message);
    }

    [Fact]
    public void Read_rejects_trailing_compiled_data()
    {
        using TempSource source = new();
        source.Write([.. CompiledBone(4, writer => { writer.Write((ushort)0); writer.Write((ushort)0); }), 0x7f]);

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => Exchange.Read(source.Directory, "sample"));
        Assert.Contains("trailing", error.Message);
    }

    [Theory]
    [InlineData((byte)2, (byte)2)]
    [InlineData((byte)4, (byte)2)]
    [InlineData((byte)4, (byte)5)]
    public void Read_rejects_duplicate_descending_or_out_of_range_sparse_indices(byte first, byte second)
    {
        using TempSource source = new();
        source.Write(CompiledBone(4, writer =>
        {
            writer.Write((ushort)2);
            writer.Write(first);
            writer.Write(second);
            writer.Write((short)0);
            writer.Write((short)0);
            writer.Write((ushort)0);
        }, simpleMask: 1));

        Assert.Throws<InvalidDataException>(() => Exchange.Read(source.Directory, "sample"));
    }

    [Fact]
    public void Read_rejects_invalid_empty_quaternion_mask()
    {
        using TempSource source = new();
        source.Write(CompiledBone(4, writer => { writer.Write((ushort)0); writer.Write((ushort)0); }, simpleMask: 0));

        Assert.Throws<InvalidDataException>(() => Exchange.Read(source.Directory, "sample"));
    }

    [Fact]
    public void Read_rejects_nonfinite_translation_vector()
    {
        using TempSource source = new();
        source.Write(CompiledBone(4, writer =>
        {
            writer.Write((ushort)0);
            writer.Write((ushort)1);
            WriteVec(writer, float.NaN, 0, 0);
        }));

        Assert.Throws<InvalidDataException>(() => Exchange.Read(source.Directory, "sample"));
    }

    [Fact]
    public void Decode_rejects_declared_stream_length_mismatch()
    {
        XAnimPartsAsset asset = CreateMinimalNativeAsset(dataByteCount: 2);

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => Exchange.Decode(asset));
        Assert.Contains("dataByte", error.Message);
    }

    [Fact]
    public void Decode_rejects_duplicate_translation_bone_assignment()
    {
        XAnimPartsAsset asset = CreateNativeAsset(duplicateTranslationBone: true);

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => Exchange.Decode(asset));
        Assert.Contains("more than once", error.Message);
    }

    [Fact]
    public void Decode_rejects_unknown_console_flags()
    {
        XAnimPartsAsset asset = CreateMinimalNativeAsset(flags: 0x80);

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => Exchange.Decode(asset));
        Assert.Contains("flag", error.Message);
    }

    [Fact]
    public void Decode_rejects_nonmonotone_native_indices()
    {
        XAnimPartsAsset asset = CreateNativeAsset(badIndex: true);

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => Exchange.Decode(asset));
        Assert.Contains("strictly increasing", error.Message);
    }

    private static XAnimPartsAsset CreateMinimalNativeAsset(
        int dataByteCount = 1, byte flags = 0, bool? delta3D = null) => new()
    {
        Name = "sample", NumFrames = 4, Framerate = 30,
        Flags = delta3D is null ? flags : (byte)(delta3D.Value ? 4 : 2),
        DeltaFlags = 1, BoneNameCount = 1,
        BoneCounts = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 1 },
        Names = [Name("root")], DataByteCount = (ushort)dataByteCount,
        PackedDataStreams = new XAnimPackedDataStreams { QuantizedBytes = new byte[] { 0 } },
        Indices = new XAnimFrameIndexStream { IsByteEncoded = true },
        DeltaPart = delta3D is null ? null : delta3D.Value
            ? new XAnimDeltaPart { Quat = new XAnimDeltaPartQuat { Frame0 = new XQuat(0, 0, 0, short.MaxValue) } }
            : new XAnimDeltaPart { Quat2 = new XAnimDeltaPartQuat2 { Frame0 = new XQuat2(0, short.MaxValue) } }
    };

    private static XAnimPartsAsset CreateNativeAsset(bool duplicateTranslationBone = false, bool badIndex = false)
    {
        // Seven rotation buckets in native order. Translation buckets name bones explicitly.
        byte[] dataBytes =
        [
            0, (byte)(badIndex ? 0 : 4), // simple quaternion indices
            0, 4,                         // normal quaternion indices
            0, 4,                         // precision quaternion indices
            1, 0, 4,                      // small translation: bone 1, indices
            (byte)(duplicateTranslationBone ? 1 : 2), 0, 4, // large translation
            3,                            // constant translation
            0, 4, 5, 6                    // absent translations
        ];
        short[] dataShorts =
        [
            1, 1, 1, // three animated quaternion stored sizes
            0x4000,  // constant simple, z = 1
            0x4000, 0, 0, // constant precision, y = 1
            1, 1     // two animated translation stored sizes
        ];
        int[] dataInts =
        [
            0x60000000, // constant normal, x = 1
            .. VecBits(1, 2, 3), .. VecBits(.5f, 1, 2),
            .. VecBits(10, 20, 30), .. VecBits(1f / 65535f, .5f, 2),
            .. VecBits(-2, 3, 7)
        ];
        short[] randomShorts =
        [
            0, 0x4000, // simple animation: identity, z = 1
            0, 0, 0, 0x4000, 0, 0, // precision animation: identity, y = 1
            -1, 2, 3, 0, 4, 5 // large translation
        ];
        int[] randomInts = [0, 0x60000000]; // normal animation: identity, x = 1
        byte[] randomBytes = [0, 0, 0, 4, 6, 8];
        return new XAnimPartsAsset
        {
            Name = "sample", NumFrames = 4, Framerate = 30, Flags = 1,
            DeltaFlags = 1, BoneCounts = new byte[] { 1, 1, 1, 1, 1, 1, 1, 1, 1, 4 },
            BoneNameCount = 7,
            Names = [Name("none"), Name("simple"), Name("normal"), Name("precision"),
                     Name("simple_const"), Name("normal_const"), Name("precision_const")],
            DataByteCount = (ushort)dataBytes.Length,
            DataShortCount = (ushort)dataShorts.Length,
            DataIntCount = (ushort)dataInts.Length,
            RandomDataShortCount = randomShorts.Length,
            RandomDataByteCount = (ushort)randomBytes.Length,
            RandomDataIntCount = (ushort)randomInts.Length,
            PackedDataStreams = new XAnimPackedDataStreams
            {
                QuantizedBytes = dataBytes, QuantizedShorts = dataShorts, QuantizedInts = dataInts,
                RandomizedQuantizedShorts = randomShorts, RandomizedQuantizedBytes = randomBytes,
                RandomizedQuantizedInts = randomInts
            },
            Indices = new XAnimFrameIndexStream { IsByteEncoded = true }
        };
    }

    private static ScriptStringReference Name(string text) => new(0, text, ScriptStringHandle.Null, default);

    private static int[] VecBits(float x, float y, float z) =>
        [BitConverter.SingleToInt32Bits(x), BitConverter.SingleToInt32Bits(y), BitConverter.SingleToInt32Bits(z)];

    private static XAnimLocalBoneTransform[] Sample(XAnimPlaybackClip clip, float frame)
    {
        XAnimLocalBoneTransform[] result = new XAnimLocalBoneTransform[clip.BoneCount];
        clip.Sample(frame, result);
        return result;
    }

    private static void AssertVector(Vector3 actual, float x, float y, float z) =>
        AssertVectorNear(actual, new Vector3(x, y, z), 0.0001f);

    private static void AssertVectorNear(Vector3 actual, Vector3 expected, float tolerance)
    {
        Assert.InRange(actual.X, expected.X - tolerance, expected.X + tolerance);
        Assert.InRange(actual.Y, expected.Y - tolerance, expected.Y + tolerance);
        Assert.InRange(actual.Z, expected.Z - tolerance, expected.Z + tolerance);
    }

    private static void AssertRotation(XAnimLocalBoneTransform actual, float x, float y, float z, float w) =>
        AssertRotationNear(actual.Rotation, new Quaternion(x, y, z, w), 0.0001f);

    private static void AssertRotationNear(Quaternion actual, Quaternion expected, float tolerance)
    {
        Assert.InRange(actual.X, expected.X - tolerance, expected.X + tolerance);
        Assert.InRange(actual.Y, expected.Y - tolerance, expected.Y + tolerance);
        Assert.InRange(actual.Z, expected.Z - tolerance, expected.Z + tolerance);
        Assert.InRange(actual.W, expected.W - tolerance, expected.W + tolerance);
    }

    private static byte[] CompiledBone(
        ushort numFrames, Action<BinaryWriter> writeBone, ushort version = 17,
        byte flags = 0, Action<BinaryWriter>? writeDelta = null, byte simpleMask = 1)
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);
        writer.Write(version);
        writer.Write((ushort)(numFrames + 1)); // nonlooping source stores loop-frame count
        writer.Write((ushort)1);
        writer.Write(flags);
        writer.Write((byte)0);
        writer.Write((ushort)30);
        writeDelta?.Invoke(writer);
        writer.Write((byte)0); // flip mask
        writer.Write(simpleMask);
        writer.Write(new byte[] { (byte)'r', (byte)'o', (byte)'o', (byte)'t', 0 });
        writeBone(writer);
        writer.Write((byte)0); // no notifications
        return stream.ToArray();
    }

    private static void WriteVec(BinaryWriter writer, float x, float y, float z)
    {
        writer.Write(x); writer.Write(y); writer.Write(z);
    }

    private sealed class TempSource : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "IW4-XAnim-tests-" + Guid.NewGuid().ToString("N"));

        public TempSource() => System.IO.Directory.CreateDirectory(Path.Combine(Directory, "xanim"));

        public void Write(byte[] bytes) => File.WriteAllBytes(Path.Combine(Directory, "xanim", "sample"), bytes);

        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}

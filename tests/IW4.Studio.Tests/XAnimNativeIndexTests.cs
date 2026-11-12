using System.Numerics;
using IW4.Formats.SourceFormat.XAnim;
using IW4.Game.Assets.XAnim;
using IW4.Game.ScriptStrings;
using Xunit;

namespace IW4.Studio.Tests;

public sealed class XAnimNativeIndexTests
{
    [Theory]
    [InlineData(2)]   // Sparse indices in the short stream, without checkpoints.
    [InlineData(65)]  // Sparse root indices, with two checkpoint words.
    [InlineData(301)] // Full root indices, with three checkpoints; compiled indices are implicit.
    public void Ushort_native_indices_preserve_keyframes_and_interpolation_through_export(int keyCount)
    {
        ushort[] indices = keyCount == 2
            ? [10, 290]
            : Enumerable.Range(0, keyCount).Select(index => (ushort)index).ToArray();
        short[] packedRotations = new short[keyCount];
        packedRotations[^1] = 0x4000; // z=1,w=0; preceding keys have z=0,w=1.
        ushort storedSize = (ushort)(keyCount - 1);
        bool rootIndices = storedSize >= 64;
        int checkpointCount = rootIndices ? ((keyCount - 2) / 256) + 2 : 0;
        short[] shorts = rootIndices
            ? [(short)storedSize, .. new short[checkpointCount]]
            : [(short)storedSize, .. indices.Select(index => (short)index)];
        XAnimPartsAsset asset = new()
        {
            Name = "word_indices",
            NumFrames = 300,
            Framerate = 30,
            BoneNameCount = 1,
            BoneCounts = new byte[] { 1, 0, 0, 0, 0, 0, 0, 0, 0, 1 },
            Names = [new ScriptStringReference(0, "root", ScriptStringHandle.Null, default)],
            DataByteCount = 1,
            DataShortCount = (ushort)shorts.Length,
            RandomDataShortCount = packedRotations.Length,
            IndexCount = rootIndices ? keyCount : 0,
            PackedDataStreams = new XAnimPackedDataStreams
            {
                QuantizedBytes = [0], // NoTrans bucket assigns bone zero.
                QuantizedShorts = shorts,
                RandomizedQuantizedShorts = packedRotations
            },
            Indices = new XAnimFrameIndexStream
            {
                IsByteEncoded = false,
                EncodedByteCount = rootIndices ? keyCount * sizeof(ushort) : 0,
                FrameIndices = rootIndices ? indices : []
            }
        };

        var exchange = new XAnimExchange();
        XAnimPlaybackClip native = exchange.Decode(asset);
        string directory = Path.Combine(Path.GetTempPath(), "IW4-XAnim-indices-" + Guid.NewGuid().ToString("N"));
        try
        {
            exchange.Unlink(directory, asset);
            XAnimPlaybackClip compiled = exchange.Read(directory, asset.Name);
            float midpoint = (indices[^2] + indices[^1]) / 2.0f;
            foreach (XAnimPlaybackClip clip in new[] { native, compiled })
            {
                AssertPose(clip, indices[^2], Quaternion.Identity);
                AssertPose(clip, midpoint, new Quaternion(0, 0, MathF.Sqrt(0.5f), MathF.Sqrt(0.5f)));
                AssertPose(clip, indices[^1], new Quaternion(0, 0, 1, 0));
            }
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static void AssertPose(XAnimPlaybackClip clip, float frame, Quaternion expected)
    {
        XAnimLocalBoneTransform[] pose = new XAnimLocalBoneTransform[1];
        clip.Sample(frame, pose);
        Assert.Equal(Vector3.Zero, pose[0].Translation);
        Assert.InRange(MathF.Abs(pose[0].Rotation.X - expected.X), 0, 0.0001f);
        Assert.InRange(MathF.Abs(pose[0].Rotation.Y - expected.Y), 0, 0.0001f);
        Assert.InRange(MathF.Abs(pose[0].Rotation.Z - expected.Z), 0, 0.0001f);
        Assert.InRange(MathF.Abs(pose[0].Rotation.W - expected.W), 0, 0.0001f);
    }
}

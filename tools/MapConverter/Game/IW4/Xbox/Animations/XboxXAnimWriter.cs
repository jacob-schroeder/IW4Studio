using System.Buffers.Binary;
using IW4.Game.Assets.XAnim;
using IW4.Game.ScriptStrings;
using MapConverter.Game.IW4.Xbox.FastFiles;

namespace MapConverter.Game.IW4.Xbox.Animations;

/// <summary>Serializes PS3 IW4 animation data in Xenon XAnimParts load order.</summary>
internal static class XboxXAnimWriter
{
    private const uint Inline = uint.MaxValue;

    internal static void Write(XboxZoneStream zone, XAnimPartsAsset anim)
    {
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentNullException.ThrowIfNull(anim);
        if (string.IsNullOrEmpty(anim.Name)) throw new InvalidDataException("XAnimParts has no name.");
        Require(anim.BoneCounts.Count, 10, "boneCount buckets");
        Require(anim.Names.Count, anim.BoneNameCount, "bone names");
        Require(anim.Notify.Count, anim.NotifyCount, "notifies");
        XAnimPackedDataStreams packed = anim.PackedDataStreams;
        Require(packed.QuantizedBytes.Count, anim.DataByteCount, "dataByte");
        Require(packed.QuantizedShorts.Count, anim.DataShortCount, "dataShort");
        Require(packed.QuantizedInts.Count, anim.DataIntCount, "dataInt");
        Require(packed.RandomizedQuantizedShorts.Count, anim.RandomDataShortCount, "randomDataShort");
        Require(packed.RandomizedQuantizedBytes.Count, anim.RandomDataByteCount, "randomDataByte");
        Require(packed.RandomizedQuantizedInts.Count, anim.RandomDataIntCount, "randomDataInt");
        Require(anim.Indices.FrameIndices.Count, anim.IndexCount, "indices");

        byte[] root = new byte[0x58];
        U(root, 0, Inline);
        H(root, 4, anim.DataByteCount);
        H(root, 6, anim.DataShortCount);
        H(root, 8, anim.DataIntCount);
        H(root, 10, anim.RandomDataByteCount);
        H(root, 12, anim.RandomDataIntCount);
        H(root, 14, anim.NumFrames);
        root[16] = anim.Flags;
        root[17] = anim.DeltaFlags;
        for (int i = 0; i < 10; i++) root[18 + i] = anim.BoneCounts[i];
        root[28] = anim.BoneNameCount;
        root[29] = anim.NotifyCount;
        root[30] = anim.AssetType;
        root[31] = anim.Pad1F;
        I(root, 32, anim.RandomDataShortCount);
        I(root, 36, anim.IndexCount);
        F(root, 40, anim.Framerate);
        F(root, 44, anim.Frequency);
        P(root, 48, anim.Names.Count);
        P(root, 52, packed.QuantizedBytes.Count);
        P(root, 56, packed.QuantizedShorts.Count);
        P(root, 60, packed.QuantizedInts.Count);
        P(root, 64, packed.RandomizedQuantizedShorts.Count);
        P(root, 68, packed.RandomizedQuantizedBytes.Count);
        P(root, 72, packed.RandomizedQuantizedInts.Count);
        P(root, 76, anim.Indices.FrameIndices.Count);
        P(root, 80, anim.Notify.Count);
        P(root, 84, anim.DeltaPart is null ? 0 : 1);
        zone.Write(root, 4, XboxBlock.Temp);

        zone.String(anim.Name);
        if (anim.Names.Count != 0)
        {
            byte[] names = new byte[checked(anim.Names.Count * 2)];
            for (int i = 0; i < anim.Names.Count; i++)
                H(names, i * 2, ScriptString(zone, anim.Names[i]));
            zone.Write(names, 2);
        }
        if (anim.Notify.Count != 0)
        {
            byte[] notify = new byte[checked(anim.Notify.Count * 8)];
            for (int i = 0; i < anim.Notify.Count; i++)
            {
                H(notify, i * 8, ScriptString(zone, anim.Notify[i].Name));
                F(notify, i * 8 + 4, anim.Notify[i].Time);
            }
            zone.Write(notify, 4);
        }
        if (anim.DeltaPart is { } delta) WriteDelta(zone, delta, anim.NumFrames);
        WriteBytes(zone, packed.QuantizedBytes);
        WriteShorts(zone, packed.QuantizedShorts, 2);
        WriteInts(zone, packed.QuantizedInts);
        WriteShorts(zone, packed.RandomizedQuantizedShorts, 2);
        WriteBytes(zone, packed.RandomizedQuantizedBytes);
        WriteInts(zone, packed.RandomizedQuantizedInts);
        WriteFrameIndices(zone, anim.Indices.FrameIndices, anim.NumFrames);
    }

    private static void WriteDelta(XboxZoneStream zone, XAnimDeltaPart delta, ushort numFrames)
    {
        byte[] row = new byte[12];
        P(row, 0, delta.Trans is null ? 0 : 1);
        P(row, 4, delta.Quat2 is null ? 0 : 1);
        P(row, 8, delta.Quat is null ? 0 : 1);
        zone.Write(row, 4);
        if (delta.Trans is { } trans) WriteTrans(zone, trans, numFrames);
        if (delta.Quat2 is { } quat2) WriteQuat2(zone, quat2, numFrames);
        if (delta.Quat is { } quat) WriteQuat(zone, quat, numFrames);
    }

    private static void WriteTrans(XboxZoneStream zone, XAnimPartTrans trans, ushort numFrames)
    {
        byte[] row = new byte[4];
        H(row, 0, trans.Size);
        row[2] = trans.SmallTrans;
        row[3] = trans.Pad3;
        zone.Write(row, 4);
        if (trans.Size == 0)
        {
            XAnimPartTransFrame0 frame = trans.Frame0 ??
                throw new InvalidDataException("Constant XAnim translation has no frame.");
            byte[] value = new byte[12];
            F(value, 0, frame.X); F(value, 4, frame.Y); F(value, 8, frame.Z);
            zone.Write(value, 4);
            return;
        }
        XAnimPartTransFrames frames = trans.Frames ??
            throw new InvalidDataException("Animated XAnim translation has no frames.");
        int count = trans.Size + 1;
        byte[] frameHeader = new byte[0x1c];
        F3(frameHeader, 0, frames.Mins);
        F3(frameHeader, 12, frames.Size);
        U(frameHeader, 24, Inline);
        zone.Write(frameHeader, 4);
        WriteDynamicIndices(zone, frames.DynamicFrames, numFrames, count);
        if (trans.SmallTrans != 0)
        {
            if (frames.FramePayload is not SmallXAnimTransFramePayload small)
                throw new InvalidDataException("Small XAnim translation has an incompatible payload.");
            Require(small.Frames.Count, count, "small translation frames");
            byte[] payload = new byte[checked(count * 3)];
            for (int i = 0; i < count; i++)
            {
                payload[i * 3] = small.Frames[i].X;
                payload[i * 3 + 1] = small.Frames[i].Y;
                payload[i * 3 + 2] = small.Frames[i].Z;
            }
            zone.Write(payload);
        }
        else
        {
            if (frames.FramePayload is not LargeXAnimTransFramePayload large)
                throw new InvalidDataException("Large XAnim translation has an incompatible payload.");
            Require(large.Frames.Count, count, "large translation frames");
            byte[] payload = new byte[checked(count * 6)];
            for (int i = 0; i < count; i++)
            {
                H(payload, i * 6, unchecked((ushort)large.Frames[i].X));
                H(payload, i * 6 + 2, unchecked((ushort)large.Frames[i].Y));
                H(payload, i * 6 + 4, unchecked((ushort)large.Frames[i].Z));
            }
            zone.Write(payload, 4);
        }
    }

    private static void WriteQuat2(XboxZoneStream zone, XAnimDeltaPartQuat2 quat, ushort numFrames)
    {
        byte[] row = new byte[4];
        H(row, 0, quat.Size);
        row[2] = quat.Pad2; row[3] = quat.Pad3;
        zone.Write(row, 4);
        if (quat.Size == 0)
        {
            XQuat2 frame = quat.Frame0 ?? throw new InvalidDataException("Constant XAnim quat2 has no frame.");
            WriteQuat2Frames(zone, [frame]);
            return;
        }
        XAnimDeltaPartQuatDataFrames2 frames = quat.Frames ??
            throw new InvalidDataException("Animated XAnim quat2 has no frames.");
        byte[] header = new byte[4];
        U(header, 0, Inline);
        zone.Write(header, 4);
        WriteDynamicIndices(zone, frames.DynamicFrames, numFrames, quat.Size + 1);
        Require(frames.Frames.Count, quat.Size + 1, "quat2 frames");
        WriteQuat2Frames(zone, frames.Frames);
    }

    private static void WriteQuat(XboxZoneStream zone, XAnimDeltaPartQuat quat, ushort numFrames)
    {
        byte[] row = new byte[4];
        H(row, 0, quat.Size);
        row[2] = quat.Pad2; row[3] = quat.Pad3;
        zone.Write(row, 4);
        if (quat.Size == 0)
        {
            XQuat frame = quat.Frame0 ?? throw new InvalidDataException("Constant XAnim quat has no frame.");
            WriteQuatFrames(zone, [frame]);
            return;
        }
        XAnimDeltaPartQuatDataFrames frames = quat.Frames ??
            throw new InvalidDataException("Animated XAnim quat has no frames.");
        byte[] header = new byte[4];
        U(header, 0, Inline);
        zone.Write(header, 4);
        WriteDynamicIndices(zone, frames.DynamicFrames, numFrames, quat.Size + 1);
        Require(frames.Frames.Count, quat.Size + 1, "quat frames");
        WriteQuatFrames(zone, frames.Frames);
    }

    private static void WriteDynamicIndices(XboxZoneStream zone, XAnimDynamicFrames frames,
        ushort numFrames, int count)
    {
        Require(frames.FrameIndices.Count, count, "dynamic frame indices");
        WriteFrameIndices(zone, frames.FrameIndices, numFrames);
    }

    private static void WriteFrameIndices(XboxZoneStream zone, IReadOnlyList<ushort> indices, ushort numFrames)
    {
        if (indices.Count == 0) return;
        if (numFrames < 256)
        {
            byte[] values = new byte[indices.Count];
            for (int i = 0; i < values.Length; i++) values[i] = checked((byte)indices[i]);
            zone.Write(values);
        }
        else
            WriteShorts(zone, indices.Select(index => unchecked((short)index)).ToArray(), 2);
    }

    private static void WriteQuat2Frames(XboxZoneStream zone, IReadOnlyList<XQuat2> frames)
    {
        byte[] data = new byte[checked(frames.Count * 4)];
        for (int i = 0; i < frames.Count; i++)
        {
            H(data, i * 4, unchecked((ushort)frames[i].Value0));
            H(data, i * 4 + 2, unchecked((ushort)frames[i].Value1));
        }
        zone.Write(data, 4);
    }

    private static void WriteQuatFrames(XboxZoneStream zone, IReadOnlyList<XQuat> frames)
    {
        byte[] data = new byte[checked(frames.Count * 8)];
        for (int i = 0; i < frames.Count; i++)
        {
            H(data, i * 8, unchecked((ushort)frames[i].Value0));
            H(data, i * 8 + 2, unchecked((ushort)frames[i].Value1));
            H(data, i * 8 + 4, unchecked((ushort)frames[i].Value2));
            H(data, i * 8 + 6, unchecked((ushort)frames[i].Value3));
        }
        zone.Write(data, 4);
    }

    private static void WriteBytes(XboxZoneStream zone, IReadOnlyList<byte> bytes)
    {
        if (bytes.Count != 0) zone.Write(bytes.ToArray());
    }

    private static void WriteShorts(XboxZoneStream zone, IReadOnlyList<short> shorts, int alignment)
    {
        if (shorts.Count == 0) return;
        byte[] data = new byte[checked(shorts.Count * 2)];
        for (int i = 0; i < shorts.Count; i++) H(data, i * 2, unchecked((ushort)shorts[i]));
        zone.Write(data, alignment);
    }

    private static void WriteInts(XboxZoneStream zone, IReadOnlyList<int> ints)
    {
        if (ints.Count == 0) return;
        byte[] data = new byte[checked(ints.Count * 4)];
        for (int i = 0; i < ints.Count; i++) I(data, i * 4, ints[i]);
        zone.Write(data, 4);
    }

    private static ushort ScriptString(XboxZoneStream zone, ScriptStringReference value) =>
        value.Text is null ? (ushort)0 : zone.ScriptString(value.Text);
    private static void Require(int actual, int expected, string field)
    {
        if (actual != expected) throw new InvalidDataException($"XAnim {field}: {actual} rows for count {expected}.");
    }
    private static void P(byte[] bytes, int offset, int count) => U(bytes, offset, count == 0 ? 0 : Inline);
    private static void H(byte[] bytes, int offset, ushort value) => BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(offset, 2), value);
    private static void U(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset, 4), value);
    private static void I(byte[] bytes, int offset, int value) => U(bytes, offset, unchecked((uint)value));
    private static void F(byte[] bytes, int offset, float value) => BinaryPrimitives.WriteSingleBigEndian(bytes.AsSpan(offset, 4), value);
    private static void F3(byte[] bytes, int offset, XAnimVec3 value)
    {
        F(bytes, offset, value.X); F(bytes, offset + 4, value.Y); F(bytes, offset + 8, value.Z);
    }
}

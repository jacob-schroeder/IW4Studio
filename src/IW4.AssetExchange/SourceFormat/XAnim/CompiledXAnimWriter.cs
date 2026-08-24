using System.Text;

namespace IW4.AssetExchange.SourceFormat.XAnim;

/// <summary>Writes the OpenAssetTools compiled XAnim v17/v18 source format.</summary>
internal static class CompiledXAnimWriter
{
    private const ushort Version17 = 17;
    private const ushort Version18 = 18;
    private const byte FlagLooped = 0x01;
    private const byte FlagDelta = 0x02;
    private const byte FlagDelta3D = 0x04;

    // These are the exact literals used by OAT's compiled XAnim writer.
    private const float SmallTransSizeScale = 0.003921568859368563f;
    private const float LargeTransSizeScale = 0.00001525902189314365f;

    private static readonly Encoding Latin1 = Encoding.Latin1;

    internal static void Write(Stream stream, XAnimSourceParts parts)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(parts);

        ushort numLoopFrames = checked((ushort)(parts.NumFrames + 1));
        bool useByteIndices = parts.NumFrames < 256;
        bool hasDelta3D = parts.Delta?.Quat?.Is3D == true;
        ushort version = hasDelta3D ? Version18 : Version17;

        var encodedBoneQuats = new EncodedQuatTrack[parts.Bones.Count];
        for (int index = 0; index < encodedBoneQuats.Length; index++)
        {
            encodedBoneQuats[index] = EncodeQuatTrack(
                parts.Bones[index].Quat,
                allowFlip: true,
                $"bone {index} ('{parts.Bones[index].Name}') quaternion");
        }

        byte flags = 0;
        if (parts.Looped)
            flags |= FlagLooped;
        if (parts.Delta is not null)
            flags |= hasDelta3D ? FlagDelta3D : FlagDelta;

        using var writer = new BinaryWriter(
            stream,
            Latin1,
            leaveOpen: true);
        writer.Write(version);
        writer.Write(parts.Looped ? parts.NumFrames : numLoopFrames);
        writer.Write(checked((ushort)parts.Bones.Count));
        writer.Write(flags);
        writer.Write(parts.AssetType);
        writer.Write(checked((ushort)MathF.Round(
            parts.Framerate,
            MidpointRounding.AwayFromZero)));

        if (parts.Delta is not null)
        {
            WriteDeltaTrack(
                writer,
                parts.Delta,
                numLoopFrames,
                useByteIndices);
        }

        if (parts.Bones.Count != 0)
        {
            int maskSize = (parts.Bones.Count + 7) / 8;
            var flipMask = new byte[maskSize];
            var simpleMask = new byte[maskSize];
            for (int index = 0; index < parts.Bones.Count; index++)
            {
                if (encodedBoneQuats[index].Flip)
                    flipMask[index / 8] |= (byte)(1 << (index % 8));
                if (parts.Bones[index].Quat.Type is
                    XAnimSourceQuatType.None or XAnimSourceQuatType.Simple)
                {
                    simpleMask[index / 8] |= (byte)(1 << (index % 8));
                }
            }

            writer.Write(flipMask);
            writer.Write(simpleMask);
            for (int index = 0; index < parts.Bones.Count; index++)
            {
                WriteCString(
                    writer,
                    parts.Bones[index].Name,
                    $"bone {index} name");
            }

            for (int index = 0; index < parts.Bones.Count; index++)
            {
                XAnimSourceBoneTrack bone = parts.Bones[index];
                WriteQuatTrack(
                    writer,
                    bone.Quat,
                    encodedBoneQuats[index],
                    numLoopFrames,
                    useByteIndices);
                WriteTransTrack(
                    writer,
                    bone.Trans,
                    numLoopFrames,
                    useByteIndices,
                    $"bone {index} ('{bone.Name}') translation");
            }
        }

        WriteNotifies(writer, parts);
    }

    private static void WriteDeltaTrack(
        BinaryWriter writer,
        XAnimSourceDeltaTrack delta,
        ushort numLoopFrames,
        bool useByteIndices)
    {
        if (delta.Quat?.Is3D == true)
        {
            WriteDeltaQuat3D(
                writer,
                delta.Quat,
                numLoopFrames,
                useByteIndices);
        }
        else
        {
            WriteDeltaQuat2D(
                writer,
                delta.Quat,
                numLoopFrames,
                useByteIndices);
        }

        if (delta.Trans is null)
        {
            writer.Write((ushort)0);
        }
        else
        {
            WriteTransTrack(
                writer,
                delta.Trans,
                numLoopFrames,
                useByteIndices,
                "delta translation");
        }
    }

    private static void WriteDeltaQuat3D(
        BinaryWriter writer,
        XAnimSourceDeltaQuatTrack quat,
        ushort numLoopFrames,
        bool useByteIndices)
    {
        int frameCount = quat.Frames3D.Count;
        if (frameCount == 0)
        {
            throw new InvalidDataException(
                "A 3D delta quaternion track has no frames.");
        }

        EncodedQuatTrack encoded = EncodeNormalFrames(
            quat.Frames3D,
            allowFlip: false,
            "3D delta quaternion");
        writer.Write(checked((ushort)frameCount));
        if (frameCount == 1)
        {
            WriteValues(writer, encoded.Values, expectedCount: 3);
            return;
        }

        WriteIndices(
            writer,
            quat.Indices,
            numLoopFrames,
            useByteIndices,
            "3D delta quaternion");
        WriteValues(writer, encoded.Values, checked(frameCount * 3));
    }

    private static void WriteDeltaQuat2D(
        BinaryWriter writer,
        XAnimSourceDeltaQuatTrack? quat,
        ushort numLoopFrames,
        bool useByteIndices)
    {
        if (quat is null)
        {
            writer.Write((ushort)0);
            return;
        }

        int frameCount = quat.Frames2D.Count;
        if (frameCount == 0)
        {
            throw new InvalidDataException(
                "A 2D delta quaternion track has no frames.");
        }

        EncodedQuatTrack encoded = EncodeSimpleFrames(
            quat.Frames2D,
            allowFlip: false,
            "2D delta quaternion");
        writer.Write(checked((ushort)frameCount));
        if (frameCount == 1)
        {
            WriteValues(writer, encoded.Values, expectedCount: 1);
            return;
        }

        WriteIndices(
            writer,
            quat.Indices,
            numLoopFrames,
            useByteIndices,
            "2D delta quaternion");
        WriteValues(writer, encoded.Values, frameCount);
    }

    private static EncodedQuatTrack EncodeQuatTrack(
        XAnimSourceQuatTrack quat,
        bool allowFlip,
        string field)
    {
        return quat.Type switch
        {
            XAnimSourceQuatType.None => new EncodedQuatTrack(false, []),
            XAnimSourceQuatType.Simple => EncodeSimpleFrames(
                quat.SimpleFrames,
                allowFlip,
                field),
            XAnimSourceQuatType.Normal => EncodeNormalFrames(
                quat.NormalFrames,
                allowFlip,
                field),
            _ => throw new InvalidDataException(
                $"{field} has unsupported type {quat.Type}.")
        };
    }

    private static EncodedQuatTrack EncodeSimpleFrames(
        IReadOnlyList<XAnimSourceQuat2> frames,
        bool allowFlip,
        string field)
    {
        if (frames.Count == 0)
            return new EncodedQuatTrack(false, []);

        bool flip = allowFlip && frames[0].Value1 < 0;
        var values = new short[frames.Count];
        for (int index = 0; index < frames.Count; index++)
        {
            XAnimSourceQuat2 frame = frames[index];
            bool omittedNegative = frame.Value1 < 0;
            bool continuityNegated = false;
            if (index > 0 && omittedNegative != flip)
            {
                XAnimSourceQuat2 previous = frames[index - 1];
                long dot =
                    (long)previous.Value0 * frame.Value0 +
                    (long)previous.Value1 * frame.Value1;
                continuityNegated = dot > 0;
            }

            int sign = flip != continuityNegated ? -1 : 1;
            values[index] = MultiplyComponent(
                frame.Value0,
                sign,
                field,
                index,
                component: 0);
        }

        return new EncodedQuatTrack(flip, values);
    }

    private static EncodedQuatTrack EncodeNormalFrames(
        IReadOnlyList<XAnimSourceQuat> frames,
        bool allowFlip,
        string field)
    {
        if (frames.Count == 0)
            return new EncodedQuatTrack(false, []);

        bool flip = allowFlip && frames[0].Value3 < 0;
        var values = new short[checked(frames.Count * 3)];
        for (int index = 0; index < frames.Count; index++)
        {
            XAnimSourceQuat frame = frames[index];
            bool omittedNegative = frame.Value3 < 0;
            bool continuityNegated = false;
            if (index > 0 && omittedNegative != flip)
            {
                XAnimSourceQuat previous = frames[index - 1];
                long dot =
                    (long)previous.Value0 * frame.Value0 +
                    (long)previous.Value1 * frame.Value1 +
                    (long)previous.Value2 * frame.Value2 +
                    (long)previous.Value3 * frame.Value3;
                continuityNegated = dot > 0;
            }

            int sign = flip != continuityNegated ? -1 : 1;
            int output = index * 3;
            values[output] = MultiplyComponent(
                frame.Value0,
                sign,
                field,
                index,
                component: 0);
            values[output + 1] = MultiplyComponent(
                frame.Value1,
                sign,
                field,
                index,
                component: 1);
            values[output + 2] = MultiplyComponent(
                frame.Value2,
                sign,
                field,
                index,
                component: 2);
        }

        return new EncodedQuatTrack(flip, values);
    }

    private static short MultiplyComponent(
        short value,
        int sign,
        string field,
        int frame,
        int component)
    {
        int result = value * sign;
        if (result is < short.MinValue or > short.MaxValue)
        {
            throw new InvalidDataException(
                $"{field} frame {frame} component {component} cannot be negated without overflowing int16.");
        }

        return (short)result;
    }

    private static void WriteQuatTrack(
        BinaryWriter writer,
        XAnimSourceQuatTrack quat,
        EncodedQuatTrack encoded,
        ushort numLoopFrames,
        bool useByteIndices)
    {
        if (quat.Type == XAnimSourceQuatType.None)
        {
            writer.Write((ushort)0);
            return;
        }

        if (quat.IsConstant)
        {
            writer.Write((ushort)1);
            WriteValues(
                writer,
                encoded.Values,
                quat.Type == XAnimSourceQuatType.Simple ? 1 : 3);
            return;
        }

        int frameCount = quat.Indices.Count;
        if (frameCount == 0)
        {
            throw new InvalidDataException(
                "A dynamic bone quaternion track has no frame indices.");
        }
        writer.Write(checked((ushort)frameCount));
        WriteIndices(
            writer,
            quat.Indices,
            numLoopFrames,
            useByteIndices,
            "bone quaternion");
        WriteValues(
            writer,
            encoded.Values,
            checked(frameCount *
                (quat.Type == XAnimSourceQuatType.Simple ? 1 : 3)));
    }

    private static void WriteTransTrack(
        BinaryWriter writer,
        XAnimSourceTransTrack trans,
        ushort numLoopFrames,
        bool useByteIndices,
        string field)
    {
        switch (trans.Type)
        {
            case XAnimSourceTransType.None:
                writer.Write((ushort)0);
                return;

            case XAnimSourceTransType.Constant:
                writer.Write((ushort)1);
                WriteVec3(writer, trans.Constant);
                return;

            case XAnimSourceTransType.Small:
            {
                int frameCount = trans.Indices.Count;
                if (frameCount == 0 || trans.SmallFrames.Count != frameCount)
                {
                    throw new InvalidDataException(
                        $"{field} has {frameCount} indices and {trans.SmallFrames.Count} byte frames.");
                }
                writer.Write(checked((ushort)frameCount));
                WriteIndices(
                    writer,
                    trans.Indices,
                    numLoopFrames,
                    useByteIndices,
                    field);
                writer.Write((byte)1);
                WriteVec3(writer, trans.Mins);
                WriteEncodedSize(writer, trans.Size, smallTrans: true, field);
                foreach (XAnimSourceSmallTrans frame in trans.SmallFrames)
                {
                    writer.Write(frame.X);
                    writer.Write(frame.Y);
                    writer.Write(frame.Z);
                }
                return;
            }

            case XAnimSourceTransType.Large:
            {
                int frameCount = trans.Indices.Count;
                if (frameCount == 0 || trans.LargeFrames.Count != frameCount)
                {
                    throw new InvalidDataException(
                        $"{field} has {frameCount} indices and {trans.LargeFrames.Count} short frames.");
                }
                writer.Write(checked((ushort)frameCount));
                WriteIndices(
                    writer,
                    trans.Indices,
                    numLoopFrames,
                    useByteIndices,
                    field);
                writer.Write((byte)0);
                WriteVec3(writer, trans.Mins);
                WriteEncodedSize(writer, trans.Size, smallTrans: false, field);
                foreach (XAnimSourceLargeTrans frame in trans.LargeFrames)
                {
                    writer.Write(frame.X);
                    writer.Write(frame.Y);
                    writer.Write(frame.Z);
                }
                return;
            }

            default:
                throw new InvalidDataException(
                    $"{field} has unsupported type {trans.Type}.");
        }
    }

    private static void WriteEncodedSize(
        BinaryWriter writer,
        XAnimSourceVec3 size,
        bool smallTrans,
        string field)
    {
        float scale = smallTrans
            ? SmallTransSizeScale
            : LargeTransSizeScale;
        WriteEncodedSizeComponent(writer, size.X, scale, field, "X");
        WriteEncodedSizeComponent(writer, size.Y, scale, field, "Y");
        WriteEncodedSizeComponent(writer, size.Z, scale, field, "Z");
    }

    private static void WriteEncodedSizeComponent(
        BinaryWriter writer,
        float value,
        float scale,
        string field,
        string component)
    {
        float encoded = value / scale;
        if (!float.IsFinite(encoded))
        {
            throw new InvalidDataException(
                $"{field} size component {component} cannot be represented by the compiled source format.");
        }
        writer.Write(encoded);
    }

    private static void WriteIndices(
        BinaryWriter writer,
        IReadOnlyList<ushort> indices,
        ushort numLoopFrames,
        bool useByteIndices,
        string field)
    {
        if (indices.Count >= numLoopFrames)
            return;

        if (useByteIndices)
        {
            foreach (ushort index in indices)
            {
                if (index > byte.MaxValue)
                {
                    throw new InvalidDataException(
                        $"{field} index {index} cannot be represented by the byte index format.");
                }
                writer.Write((byte)index);
            }
            return;
        }

        foreach (ushort index in indices)
            writer.Write(index);
    }

    private static void WriteNotifies(
        BinaryWriter writer,
        XAnimSourceParts parts)
    {
        int notifyCount = parts.Notifies.Count;
        if (notifyCount > 0 &&
            parts.Notifies[^1].Name == "end" &&
            MathF.Abs(parts.Notifies[^1].Time - 1.0f) < 0.0001f)
        {
            notifyCount--;
        }

        writer.Write(checked((byte)notifyCount));
        for (int index = 0; index < notifyCount; index++)
        {
            XAnimSourceNotify notify = parts.Notifies[index];
            WriteCString(writer, notify.Name, $"notify {index} name");
            ushort frame = 0;
            if (parts.NumFrames > 0)
            {
                frame = checked((ushort)MathF.Round(
                    notify.Time * parts.NumFrames,
                    MidpointRounding.AwayFromZero));
            }
            writer.Write(frame);
        }
    }

    private static void WriteCString(
        BinaryWriter writer,
        string value,
        string field)
    {
        foreach (char character in value)
        {
            if (character == '\0' || character > byte.MaxValue)
            {
                throw new InvalidDataException(
                    $"{field} cannot be encoded as a one-byte IW4 string.");
            }
        }

        byte[] bytes = Latin1.GetBytes(value);
        writer.Write(bytes);
        writer.Write((byte)0);
    }

    private static void WriteVec3(
        BinaryWriter writer,
        XAnimSourceVec3 value)
    {
        writer.Write(value.X);
        writer.Write(value.Y);
        writer.Write(value.Z);
    }

    private static void WriteValues(
        BinaryWriter writer,
        IReadOnlyList<short> values,
        int expectedCount)
    {
        if (values.Count != expectedCount)
        {
            throw new InvalidDataException(
                $"A quaternion track requires {expectedCount} encoded values but contains {values.Count}.");
        }

        foreach (short value in values)
            writer.Write(value);
    }

    private sealed record EncodedQuatTrack(
        bool Flip,
        IReadOnlyList<short> Values);
}

using System.Text;

namespace IW4.Formats.SourceFormat.XAnim;

/// <summary>Reads the v17/v18 compiled source written by CompiledXAnimWriter.</summary>
internal static class CompiledXAnimReader
{
    private const float SmallScale = 0.003921568859368563f;
    private const float LargeScale = 0.00001525902189314365f;

    internal static XAnimSourceParts Read(Stream stream, string assetName)
    {
        using var reader = new BinaryReader(stream, Encoding.Latin1, leaveOpen: true);
        try
        {
            ushort version = reader.ReadUInt16();
            if (version is not (17 or 18))
                throw new InvalidDataException($"XAnim '{assetName}' has unsupported compiled version {version}.");
            ushort storedFrames = reader.ReadUInt16();
            ushort boneCount = reader.ReadUInt16();
            byte flags = reader.ReadByte();
            if ((flags & ~0x07) != 0 || (flags & 0x06) == 0x06 ||
                (version == 17 && (flags & 0x04) != 0))
                throw new InvalidDataException($"XAnim '{assetName}' has invalid compiled flags 0x{flags:X2}.");
            byte assetType = reader.ReadByte();
            ushort framerate = reader.ReadUInt16();
            bool looped = (flags & 0x01) != 0;
            if (!looped && storedFrames == 0)
                throw new InvalidDataException($"XAnim '{assetName}' has invalid frame or rate header.");
            ushort numFrames = looped ? storedFrames : checked((ushort)(storedFrames - 1));
            ushort loopFrames = checked((ushort)(numFrames + 1));
            bool byteIndices = numFrames < 256;

            XAnimSourceDeltaTrack? delta = null;
            if ((flags & 0x06) != 0)
            {
                bool is3D = (flags & 0x04) != 0;
                int count = reader.ReadUInt16();
                XAnimSourceDeltaQuatTrack? deltaQuat = count == 0 ? null : new XAnimSourceDeltaQuatTrack
                {
                    Is3D = is3D,
                    Indices = count > 1 ? ReadIndices(reader, count, loopFrames, byteIndices) : [],
                    Frames2D = is3D ? [] : Enumerable.Range(0, count)
                        .Select(_ => RestoreSimple(reader.ReadInt16(), false)).ToArray(),
                    Frames3D = !is3D ? [] : Enumerable.Range(0, count)
                        .Select(_ => RestoreNormal(reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt16(), false)).ToArray()
                };
                delta = new XAnimSourceDeltaTrack
                {
                    Quat = deltaQuat,
                    Trans = ReadTrans(reader, loopFrames, byteIndices, zeroMeansNull: true)
                };
            }

            int maskSize = (boneCount + 7) / 8;
            byte[] flipMask = ReadBytes(reader, maskSize);
            byte[] simpleMask = ReadBytes(reader, maskSize);
            string[] names = Enumerable.Range(0, boneCount).Select(_ => ReadString(reader)).ToArray();
            var bones = new XAnimSourceBoneTrack[boneCount];
            for (int index = 0; index < bones.Length; index++)
            {
                bool simple = (simpleMask[index / 8] & (1 << (index % 8))) != 0;
                bool flip = (flipMask[index / 8] & (1 << (index % 8))) != 0;
                int count = reader.ReadUInt16();
                XAnimSourceQuatTrack quat;
                if (count == 0)
                {
                    if (!simple || flip)
                        throw new InvalidDataException($"XAnim '{assetName}' has invalid empty bone quaternion {index}.");
                    quat = new XAnimSourceQuatTrack { Type = XAnimSourceQuatType.None };
                }
                else
                {
                    ushort[] indices = count > 1 ? ReadIndices(reader, count, loopFrames, byteIndices) : [];
                    quat = simple
                        ? new XAnimSourceQuatTrack
                        {
                            Type = XAnimSourceQuatType.Simple,
                            IsConstant = count == 1,
                            Indices = indices,
                            SimpleFrames = Enumerable.Range(0, count)
                                .Select(_ => RestoreSimple(reader.ReadInt16(), flip)).ToArray()
                        }
                        : new XAnimSourceQuatTrack
                        {
                            Type = XAnimSourceQuatType.Normal,
                            IsConstant = count == 1,
                            Indices = indices,
                            NormalFrames = Enumerable.Range(0, count)
                                .Select(_ => RestoreNormal(reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt16(), flip)).ToArray()
                        };
                }
                bones[index] = new XAnimSourceBoneTrack
                {
                    Name = names[index],
                    Quat = quat,
                    Trans = ReadTrans(reader, loopFrames, byteIndices, zeroMeansNull: false)
                        ?? throw new InvalidDataException($"XAnim '{assetName}' bone {index} has no translation track.")
                };
            }

            int notifyCount = reader.ReadByte();
            var notifies = new XAnimSourceNotify[notifyCount];
            for (int index = 0; index < notifyCount; index++)
            {
                string name = ReadString(reader);
                ushort frame = reader.ReadUInt16();
                if (frame > numFrames)
                    throw new InvalidDataException($"XAnim '{assetName}' notify {index} is outside its frame range.");
                notifies[index] = new XAnimSourceNotify(name,
                    numFrames == 0 ? 0.0f : frame / (float)numFrames);
            }
            if (stream.Position != stream.Length)
                throw new InvalidDataException($"XAnim '{assetName}' has trailing compiled data.");
            return new XAnimSourceParts
            {
                NumFrames = numFrames,
                Looped = looped,
                Framerate = framerate,
                AssetType = assetType,
                Bones = bones,
                Notifies = notifies,
                Delta = delta
            };
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException($"XAnim '{assetName}' has truncated compiled data.", exception);
        }
    }

    private static XAnimSourceTransTrack? ReadTrans(BinaryReader reader, ushort loopFrames, bool byteIndices, bool zeroMeansNull)
    {
        int count = reader.ReadUInt16();
        if (count == 0)
            return zeroMeansNull ? null : new XAnimSourceTransTrack { Type = XAnimSourceTransType.None };
        if (count == 1)
            return new XAnimSourceTransTrack { Type = XAnimSourceTransType.Constant, Constant = ReadVec3(reader) };
        ushort[] indices = ReadIndices(reader, count, loopFrames, byteIndices);
        byte small = reader.ReadByte();
        if (small is not (0 or 1))
            throw new InvalidDataException($"Compiled XAnim translation has invalid encoding {small}.");
        XAnimSourceVec3 mins = ReadVec3(reader);
        XAnimSourceVec3 encodedSize = ReadVec3(reader);
        float scale = small == 1 ? SmallScale : LargeScale;
        var size = new XAnimSourceVec3(encodedSize.X * scale, encodedSize.Y * scale, encodedSize.Z * scale);
        return small == 1
            ? new XAnimSourceTransTrack
            {
                Type = XAnimSourceTransType.Small, Indices = indices, Mins = mins, Size = size,
                SmallFrames = Enumerable.Range(0, count)
                    .Select(_ => new XAnimSourceSmallTrans(reader.ReadByte(), reader.ReadByte(), reader.ReadByte())).ToArray()
            }
            : new XAnimSourceTransTrack
            {
                Type = XAnimSourceTransType.Large, Indices = indices, Mins = mins, Size = size,
                LargeFrames = Enumerable.Range(0, count)
                    .Select(_ => new XAnimSourceLargeTrans(reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt16())).ToArray()
            };
    }

    private static ushort[] ReadIndices(BinaryReader reader, int count, ushort loopFrames, bool byteIndices)
    {
        if (count > loopFrames)
            throw new InvalidDataException("Compiled XAnim track exceeds its loop frame count.");
        var indices = new ushort[count];
        if (count == loopFrames)
        {
            for (int index = 0; index < count; index++) indices[index] = checked((ushort)index);
            return indices;
        }
        ushort prior = 0;
        for (int index = 0; index < count; index++)
        {
            ushort frame = byteIndices ? reader.ReadByte() : reader.ReadUInt16();
            if (frame >= loopFrames || (index > 0 && frame <= prior))
                throw new InvalidDataException("Compiled XAnim frame indices are invalid.");
            indices[index] = prior = frame;
        }
        return indices;
    }

    private static XAnimSourceQuat2 RestoreSimple(short value, bool flip)
    {
        float normalized = value / (float)short.MaxValue;
        short omitted = (short)(MathF.Sqrt(MathF.Max(0, 1 - normalized * normalized)) * short.MaxValue);
        return new XAnimSourceQuat2(flip ? (short)-value : value, flip ? (short)-omitted : omitted);
    }

    private static XAnimSourceQuat RestoreNormal(short x, short y, short z, bool flip)
    {
        float fx = x / (float)short.MaxValue, fy = y / (float)short.MaxValue, fz = z / (float)short.MaxValue;
        short w = (short)(MathF.Sqrt(MathF.Max(0, 1 - fx * fx - fy * fy - fz * fz)) * short.MaxValue);
        return flip
            ? new XAnimSourceQuat((short)-x, (short)-y, (short)-z, (short)-w)
            : new XAnimSourceQuat(x, y, z, w);
    }

    private static XAnimSourceVec3 ReadVec3(BinaryReader reader)
    {
        var value = new XAnimSourceVec3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
            throw new InvalidDataException("Compiled XAnim vector contains a non-finite component.");
        return value;
    }

    private static byte[] ReadBytes(BinaryReader reader, int count)
    {
        byte[] bytes = reader.ReadBytes(count);
        if (bytes.Length != count) throw new EndOfStreamException();
        return bytes;
    }

    private static string ReadString(BinaryReader reader)
    {
        var bytes = new List<byte>();
        for (int index = 0; index < 4096; index++)
        {
            byte value = reader.ReadByte();
            if (value == 0) return Encoding.Latin1.GetString(bytes.ToArray());
            bytes.Add(value);
        }
        throw new InvalidDataException("Compiled XAnim string exceeds 4096 bytes.");
    }
}

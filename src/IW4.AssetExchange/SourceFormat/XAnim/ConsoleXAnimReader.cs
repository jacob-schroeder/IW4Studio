using IW4.Assets.Assets.XAnim;
using IW4.FastFiles.Strings;

namespace IW4.AssetExchange.SourceFormat.XAnim;

/// <summary>
/// Reconstructs developer-facing animation tracks from the flat PS3 IW4
/// console streams. Console IW4 uses twelve part-count buckets and compressed
/// quaternion encodings rather than the ten PC buckets used by OAT's native
/// IW4 structures.
/// </summary>
internal static class ConsoleXAnimReader
{
    private const int ConsoleBoneCountLength = 12;
    private const int MaterializedBoneCountLength = 10;

    internal static XAnimSourceParts Read(XAnimPartsAsset asset, string assetName)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetName);

        int[] counts = ReadAndValidateCounts(asset, assetName);
        ushort numLoopFrames = GetNumLoopFrames(asset.NumFrames, assetName);
        bool useByteIndices = asset.NumFrames < 256;
        ValidateRootMetadata(asset, assetName, useByteIndices);

        string[] boneNames = ReadBoneNames(asset, assetName, counts[(int)ConsolePartType.All]);
        XAnimSourceNotify[] notifies = ReadNotifies(asset, assetName);
        var cursor = new FlatCursor(asset, assetName);
        XAnimSourceBoneTrack[] bones = CreateBones(boneNames);

        ReadQuaternionTracks(
            bones,
            counts,
            cursor,
            useByteIndices,
            numLoopFrames,
            assetName);
        ReadTranslationTracks(
            bones,
            counts,
            cursor,
            useByteIndices,
            numLoopFrames,
            assetName);
        cursor.ExpectEnd();

        XAnimSourceDeltaTrack? delta = ReadDelta(
            asset,
            useByteIndices,
            numLoopFrames,
            assetName);

        return new XAnimSourceParts
        {
            NumFrames = asset.NumFrames,
            Looped =
                (asset.Flags & CompiledXAnimWriter.FlagLooped) != 0,
            Framerate = asset.Framerate,
            AssetType = asset.AssetType,
            Bones = bones,
            Notifies = notifies,
            Delta = delta
        };
    }

    private static int[] ReadAndValidateCounts(
        XAnimPartsAsset asset,
        string assetName)
    {
        IReadOnlyList<byte> materializedCounts = asset.BoneCounts ??
            throw Invalid(assetName, "has no materialized bone-count table.");
        if (materializedCounts.Count != MaterializedBoneCountLength)
        {
            throw Invalid(
                assetName,
                $"has {materializedCounts.Count} middle bone-count buckets; expected {MaterializedBoneCountLength}.");
        }

        // The current PS3 mirror retains the historical PC field names. In the
        // proven console layout, DeltaFlags is boneCount[NO_QUAT], BoneCounts is
        // boneCount[1..10], and BoneNameCount is boneCount[ALL].
        var counts = new int[ConsoleBoneCountLength];
        counts[(int)ConsolePartType.NoQuat] = asset.DeltaFlags;
        for (int index = 0; index < materializedCounts.Count; index++)
            counts[index + 1] = materializedCounts[index];
        counts[(int)ConsolePartType.All] = asset.BoneNameCount;

        int quatCount = 0;
        for (int index = (int)ConsolePartType.NoQuat;
             index <= (int)ConsolePartType.PrecisionQuatNoSize;
             index++)
        {
            quatCount = checked(quatCount + counts[index]);
        }

        int transCount = 0;
        for (int index = (int)ConsolePartType.SmallTrans;
             index <= (int)ConsolePartType.NoTrans;
             index++)
        {
            transCount = checked(transCount + counts[index]);
        }

        int boneCount = counts[(int)ConsolePartType.All];
        if (quatCount != boneCount || transCount != boneCount)
        {
            throw Invalid(
                assetName,
                $"declares {boneCount} bones but its quaternion buckets total {quatCount} and translation buckets total {transCount}.");
        }

        return counts;
    }

    private static void ValidateRootMetadata(
        XAnimPartsAsset asset,
        string assetName,
        bool useByteIndices)
    {
        const byte KnownFlags =
            CompiledXAnimWriter.FlagLooped |
            CompiledXAnimWriter.FlagDelta |
            CompiledXAnimWriter.FlagDelta3D;
        byte unknownFlags = (byte)(asset.Flags & ~KnownFlags);
        if (unknownFlags != 0)
        {
            throw Invalid(
                assetName,
                $"has unmapped console XAnim flag bits 0x{unknownFlags:X2} " +
                $"in value 0x{asset.Flags:X2}.");
        }

        const byte DeltaFlags =
            CompiledXAnimWriter.FlagDelta |
            CompiledXAnimWriter.FlagDelta3D;
        if ((asset.Flags & DeltaFlags) == DeltaFlags)
        {
            throw Invalid(
                assetName,
                $"sets both 2D and 3D delta flags in console XAnim flag " +
                $"value 0x{asset.Flags:X2}.");
        }
        if (!float.IsFinite(asset.Framerate) || asset.Framerate < 0.0f)
        {
            throw Invalid(assetName, "has a non-finite or negative framerate.");
        }

        float roundedFramerate = MathF.Round(
            asset.Framerate,
            MidpointRounding.AwayFromZero);
        if (roundedFramerate > ushort.MaxValue)
        {
            throw Invalid(
                assetName,
                $"has framerate {asset.Framerate} which cannot be represented by the compiled source format.");
        }

        XAnimPackedDataStreams streams = asset.PackedDataStreams ??
            throw Invalid(assetName, "has no materialized packed-data streams.");
        RequireCount(
            streams.QuantizedBytes,
            asset.DataByteCount,
            assetName,
            "dataByte");
        RequireCount(
            streams.QuantizedShorts,
            asset.DataShortCount,
            assetName,
            "dataShort");
        RequireCount(
            streams.QuantizedInts,
            asset.DataIntCount,
            assetName,
            "dataInt");
        RequireCount(
            streams.RandomizedQuantizedBytes,
            asset.RandomDataByteCount,
            assetName,
            "randomDataByte");
        if (asset.RandomDataShortCount < 0)
        {
            throw Invalid(
                assetName,
                $"declares negative randomDataShort count {asset.RandomDataShortCount}.");
        }
        RequireCount(
            streams.RandomizedQuantizedShorts,
            asset.RandomDataShortCount,
            assetName,
            "randomDataShort");
        RequireCount(
            streams.RandomizedQuantizedInts,
            asset.RandomDataIntCount,
            assetName,
            "randomDataInt");

        if (asset.IndexCount < 0)
        {
            throw Invalid(
                assetName,
                $"declares negative root index count {asset.IndexCount}.");
        }
        XAnimFrameIndexStream indices = asset.Indices ??
            throw Invalid(assetName, "has no materialized root index stream.");
        RequireCount(
            indices.FrameIndices,
            asset.IndexCount,
            assetName,
            "indices");
        long expectedIndexBytesLong =
            (long)asset.IndexCount *
            (useByteIndices ? sizeof(byte) : sizeof(ushort));
        if (expectedIndexBytesLong > int.MaxValue)
        {
            throw Invalid(
                assetName,
                $"declares root index data too large to materialize ({expectedIndexBytesLong} bytes).");
        }
        int expectedIndexBytes = (int)expectedIndexBytesLong;
        if (indices.EncodedByteCount != expectedIndexBytes ||
            indices.IsByteEncoded != useByteIndices)
        {
            throw Invalid(
                assetName,
                $"has inconsistent root index encoding metadata: {indices.EncodedByteCount} bytes, " +
                $"byteEncoded={indices.IsByteEncoded}; expected {expectedIndexBytes} bytes, byteEncoded={useByteIndices}.");
        }

        IReadOnlyList<XAnimNotifyInfo> notify = asset.Notify ??
            throw Invalid(assetName, "has no materialized notify list.");
        RequireCount(notify, asset.NotifyCount, assetName, "notify");
    }

    private static string[] ReadBoneNames(
        XAnimPartsAsset asset,
        string assetName,
        int boneCount)
    {
        IReadOnlyList<ScriptStringReference> names = asset.Names ??
            throw Invalid(assetName, "has no materialized bone-name list.");
        RequireCount(names, boneCount, assetName, "bone name");

        var result = new string[boneCount];
        for (int index = 0; index < result.Length; index++)
        {
            ScriptStringReference reference = names[index] ??
                throw Invalid(assetName, $"bone name {index} is null.");
            string text = reference.Text ??
                throw Invalid(
                    assetName,
                    $"bone name {index} (script string {reference.RawLocalIndex}) was not materialized.");
            ValidateCString(text, assetName, $"bone name {index}");
            result[index] = text;
        }

        return result;
    }

    private static XAnimSourceNotify[] ReadNotifies(
        XAnimPartsAsset asset,
        string assetName)
    {
        var result = new XAnimSourceNotify[asset.NotifyCount];
        for (int index = 0; index < result.Length; index++)
        {
            XAnimNotifyInfo notify = asset.Notify[index] ??
                throw Invalid(assetName, $"notify {index} is null.");
            ScriptStringReference reference = notify.Name ??
                throw Invalid(assetName, $"notify {index} has no script-string reference.");
            string name = reference.Text ??
                throw Invalid(
                    assetName,
                    $"notify {index} name (script string {reference.RawLocalIndex}) was not materialized.");
            ValidateCString(name, assetName, $"notify {index} name");
            if (!float.IsFinite(notify.Time))
            {
                throw Invalid(assetName, $"notify {index} has a non-finite time.");
            }

            result[index] = new XAnimSourceNotify(name, notify.Time);
        }

        int sourceNotifyCount = result.Length;
        if (sourceNotifyCount > 0 &&
            result[^1].Name == "end" &&
            MathF.Abs(result[^1].Time - 1.0f) < 0.0001f)
        {
            sourceNotifyCount--;
        }
        if (sourceNotifyCount >= byte.MaxValue)
        {
            throw Invalid(
                assetName,
                $"has {sourceNotifyCount} source notifies; the compiled format supports at most 254.");
        }

        for (int index = 0; index < sourceNotifyCount; index++)
        {
            float scaled = result[index].Time * asset.NumFrames;
            if (!float.IsFinite(scaled))
            {
                throw Invalid(assetName, $"notify {index} produces a non-finite frame number.");
            }
            float frame = MathF.Round(scaled, MidpointRounding.AwayFromZero);
            if (frame < 0.0f || frame > ushort.MaxValue)
            {
                throw Invalid(
                    assetName,
                    $"notify {index} time {result[index].Time} cannot be represented by the compiled frame field.");
            }
        }

        return result;
    }

    private static XAnimSourceBoneTrack[] CreateBones(
        IReadOnlyList<string> boneNames)
    {
        var result = new XAnimSourceBoneTrack[boneNames.Count];
        for (int index = 0; index < result.Length; index++)
        {
            result[index] = new XAnimSourceBoneTrack
            {
                Name = boneNames[index],
                Quat = new XAnimSourceQuatTrack
                {
                    Type = XAnimSourceQuatType.None
                },
                Trans = new XAnimSourceTransTrack
                {
                    Type = XAnimSourceTransType.None
                }
            };
        }

        return result;
    }

    private static void ReadQuaternionTracks(
        XAnimSourceBoneTrack[] bones,
        IReadOnlyList<int> counts,
        FlatCursor cursor,
        bool useByteIndices,
        ushort numLoopFrames,
        string assetName)
    {
        int boneIndex = counts[(int)ConsolePartType.NoQuat];

        for (int index = 0;
             index < counts[(int)ConsolePartType.SimpleQuat];
             index++, boneIndex++)
        {
            string field = $"bone {boneIndex} simple quaternion";
            (ushort storedSize, ushort[] indices) = ReadDynamicHeader(
                cursor,
                useByteIndices,
                numLoopFrames,
                assetName,
                field);
            var frames = new XAnimSourceQuat2[storedSize + 1];
            for (int frame = 0; frame < frames.Length; frame++)
            {
                frames[frame] = DecodeSimpleQuat(
                    unchecked((ushort)cursor.PopRandomShort(field)));
            }
            bones[boneIndex].Quat = new XAnimSourceQuatTrack
            {
                Type = XAnimSourceQuatType.Simple,
                Indices = indices,
                SimpleFrames = frames
            };
        }

        for (int index = 0;
             index < counts[(int)ConsolePartType.NormalQuat];
             index++, boneIndex++)
        {
            string field = $"bone {boneIndex} normal quaternion";
            (ushort storedSize, ushort[] indices) = ReadDynamicHeader(
                cursor,
                useByteIndices,
                numLoopFrames,
                assetName,
                field);
            var frames = new XAnimSourceQuat[storedSize + 1];
            for (int frame = 0; frame < frames.Length; frame++)
            {
                frames[frame] = DecodeNormalQuat(
                    unchecked((uint)cursor.PopRandomInt(field)));
            }
            bones[boneIndex].Quat = new XAnimSourceQuatTrack
            {
                Type = XAnimSourceQuatType.Normal,
                Indices = indices,
                NormalFrames = frames
            };
        }

        for (int index = 0;
             index < counts[(int)ConsolePartType.PrecisionQuat];
             index++, boneIndex++)
        {
            string field = $"bone {boneIndex} precision quaternion";
            (ushort storedSize, ushort[] indices) = ReadDynamicHeader(
                cursor,
                useByteIndices,
                numLoopFrames,
                assetName,
                field);
            var frames = new XAnimSourceQuat[storedSize + 1];
            for (int frame = 0; frame < frames.Length; frame++)
            {
                frames[frame] = DecodePrecisionQuat(
                    unchecked((ushort)cursor.PopRandomShort(field)),
                    unchecked((ushort)cursor.PopRandomShort(field)),
                    unchecked((ushort)cursor.PopRandomShort(field)));
            }
            bones[boneIndex].Quat = new XAnimSourceQuatTrack
            {
                Type = XAnimSourceQuatType.Normal,
                Indices = indices,
                NormalFrames = frames
            };
        }

        for (int index = 0;
             index < counts[(int)ConsolePartType.SimpleQuatNoSize];
             index++, boneIndex++)
        {
            string field = $"bone {boneIndex} constant simple quaternion";
            bones[boneIndex].Quat = new XAnimSourceQuatTrack
            {
                Type = XAnimSourceQuatType.Simple,
                IsConstant = true,
                SimpleFrames =
                [
                    DecodeSimpleQuat(
                        unchecked((ushort)cursor.PopShort(field)))
                ]
            };
        }

        for (int index = 0;
             index < counts[(int)ConsolePartType.NormalQuatNoSize];
             index++, boneIndex++)
        {
            string field = $"bone {boneIndex} constant normal quaternion";
            bones[boneIndex].Quat = new XAnimSourceQuatTrack
            {
                Type = XAnimSourceQuatType.Normal,
                IsConstant = true,
                NormalFrames =
                [
                    DecodeNormalQuat(
                        unchecked((uint)cursor.PopInt(field)))
                ]
            };
        }

        for (int index = 0;
             index < counts[(int)ConsolePartType.PrecisionQuatNoSize];
             index++, boneIndex++)
        {
            string field = $"bone {boneIndex} constant precision quaternion";
            bones[boneIndex].Quat = new XAnimSourceQuatTrack
            {
                Type = XAnimSourceQuatType.Normal,
                IsConstant = true,
                NormalFrames =
                [
                    DecodePrecisionQuat(
                        unchecked((ushort)cursor.PopShort(field)),
                        unchecked((ushort)cursor.PopShort(field)),
                        unchecked((ushort)cursor.PopShort(field)))
                ]
            };
        }

        if (boneIndex != bones.Length)
        {
            throw Invalid(
                assetName,
                $"quaternion reconstruction ended at bone {boneIndex} of {bones.Length}.");
        }
    }

    private static void ReadTranslationTracks(
        XAnimSourceBoneTrack[] bones,
        IReadOnlyList<int> counts,
        FlatCursor cursor,
        bool useByteIndices,
        ushort numLoopFrames,
        string assetName)
    {
        var assigned = new bool[bones.Length];

        for (int index = 0;
             index < counts[(int)ConsolePartType.SmallTrans];
             index++)
        {
            int bone = ReadTranslationBone(cursor, assigned, assetName);
            string field = $"bone {bone} small translation";
            (
                ushort storedSize,
                XAnimSourceVec3 mins,
                XAnimSourceVec3 size,
                ushort[] indices) = ReadDynamicTranslationHeader(
                cursor,
                useByteIndices,
                numLoopFrames,
                assetName,
                field);
            var frames = new XAnimSourceSmallTrans[storedSize + 1];
            for (int frame = 0; frame < frames.Length; frame++)
            {
                frames[frame] = new XAnimSourceSmallTrans(
                    cursor.PopRandomByte(field),
                    cursor.PopRandomByte(field),
                    cursor.PopRandomByte(field));
            }
            bones[bone].Trans = new XAnimSourceTransTrack
            {
                Type = XAnimSourceTransType.Small,
                Indices = indices,
                Mins = mins,
                Size = size,
                SmallFrames = frames
            };
        }

        for (int index = 0;
             index < counts[(int)ConsolePartType.Trans];
             index++)
        {
            int bone = ReadTranslationBone(cursor, assigned, assetName);
            string field = $"bone {bone} translation";
            (
                ushort storedSize,
                XAnimSourceVec3 mins,
                XAnimSourceVec3 size,
                ushort[] indices) = ReadDynamicTranslationHeader(
                cursor,
                useByteIndices,
                numLoopFrames,
                assetName,
                field);
            var frames = new XAnimSourceLargeTrans[storedSize + 1];
            for (int frame = 0; frame < frames.Length; frame++)
            {
                frames[frame] = new XAnimSourceLargeTrans(
                    cursor.PopRandomShort(field),
                    cursor.PopRandomShort(field),
                    cursor.PopRandomShort(field));
            }
            bones[bone].Trans = new XAnimSourceTransTrack
            {
                Type = XAnimSourceTransType.Large,
                Indices = indices,
                Mins = mins,
                Size = size,
                LargeFrames = frames
            };
        }

        for (int index = 0;
             index < counts[(int)ConsolePartType.TransNoSize];
             index++)
        {
            int bone = ReadTranslationBone(cursor, assigned, assetName);
            string field = $"bone {bone} constant translation";
            XAnimSourceVec3 constant = cursor.ReadFloat3(field);
            ValidateFinite(constant, assetName, field);
            bones[bone].Trans = new XAnimSourceTransTrack
            {
                Type = XAnimSourceTransType.Constant,
                Constant = constant
            };
        }

        for (int index = 0;
             index < counts[(int)ConsolePartType.NoTrans];
             index++)
        {
            int bone = ReadTranslationBone(cursor, assigned, assetName);
            bones[bone].Trans = new XAnimSourceTransTrack
            {
                Type = XAnimSourceTransType.None
            };
        }

        int missing = Array.FindIndex(assigned, value => !value);
        if (missing >= 0)
        {
            throw Invalid(
                assetName,
                $"has no translation assignment for bone {missing}.");
        }
    }

    private static int ReadTranslationBone(
        FlatCursor cursor,
        bool[] assigned,
        string assetName)
    {
        int bone = cursor.PopByte("translation bone index");
        if ((uint)bone >= (uint)assigned.Length)
        {
            throw Invalid(
                assetName,
                $"translation stream references bone {bone}, but only {assigned.Length} bones exist.");
        }
        if (assigned[bone])
        {
            throw Invalid(
                assetName,
                $"translation stream assigns bone {bone} more than once.");
        }

        assigned[bone] = true;
        return bone;
    }

    private static (ushort StoredSize, ushort[] Indices) ReadDynamicHeader(
        FlatCursor cursor,
        bool useByteIndices,
        ushort numLoopFrames,
        string assetName,
        string field)
    {
        ushort storedSize = unchecked((ushort)cursor.PopShort(field));
        if (storedSize == 0)
        {
            throw Invalid(
                assetName,
                $"{field} is in a dynamic part bucket but declares only one frame.");
        }
        int frameCount = storedSize + 1;
        if (frameCount > numLoopFrames)
        {
            throw Invalid(
                assetName,
                $"{field} has {frameCount} keyed frames but the animation has only {numLoopFrames} loop frames.");
        }

        ushort[] indices = cursor.ReadPackedIndices(
            storedSize,
            useByteIndices,
            field);
        ValidateIndices(
            indices,
            numLoopFrames,
            assetName,
            field);
        return (storedSize, indices);
    }

    private static (
        ushort StoredSize,
        XAnimSourceVec3 Mins,
        XAnimSourceVec3 Size,
        ushort[] Indices) ReadDynamicTranslationHeader(
        FlatCursor cursor,
        bool useByteIndices,
        ushort numLoopFrames,
        string assetName,
        string field)
    {
        ushort storedSize = unchecked((ushort)cursor.PopShort(field));
        if (storedSize == 0)
        {
            throw Invalid(
                assetName,
                $"{field} is in a dynamic part bucket but declares only one frame.");
        }
        int frameCount = storedSize + 1;
        if (frameCount > numLoopFrames)
        {
            throw Invalid(
                assetName,
                $"{field} has {frameCount} keyed frames but the animation has only {numLoopFrames} loop frames.");
        }

        XAnimSourceVec3 mins = cursor.ReadFloat3(field);
        XAnimSourceVec3 size = cursor.ReadFloat3(field);
        ValidateDynamicTransBounds(mins, size, assetName, field);
        ushort[] indices = cursor.ReadPackedIndices(
            storedSize,
            useByteIndices,
            field);
        ValidateIndices(indices, numLoopFrames, assetName, field);
        return (storedSize, mins, size, indices);
    }

    private static XAnimSourceDeltaTrack? ReadDelta(
        XAnimPartsAsset asset,
        bool useByteIndices,
        ushort numLoopFrames,
        string assetName)
    {
        if (asset.DeltaPart is null)
        {
            if (asset.DeltaPartPointer.Raw != 0)
            {
                throw Invalid(
                    assetName,
                    "references a delta part that was not materialized.");
            }
            return null;
        }

        XAnimDeltaPart delta = asset.DeltaPart;
        if (delta.Quat2 is not null && delta.Quat is not null)
        {
            throw Invalid(
                assetName,
                "contains both 2D and 3D delta quaternion tracks.");
        }
        if (delta.Quat2 is null && delta.Quat2Pointer.Raw != 0)
        {
            throw Invalid(
                assetName,
                "references a 2D delta quaternion track that was not materialized.");
        }
        if (delta.Quat is null && delta.QuatPointer.Raw != 0)
        {
            throw Invalid(
                assetName,
                "references a 3D delta quaternion track that was not materialized.");
        }
        if (delta.Trans is null && delta.TransPointer.Raw != 0)
        {
            throw Invalid(
                assetName,
                "references a delta translation track that was not materialized.");
        }

        return new XAnimSourceDeltaTrack
        {
            Quat = delta.Quat2 is not null
                ? ReadDeltaQuat2(
                    delta.Quat2,
                    useByteIndices,
                    numLoopFrames,
                    assetName)
                : delta.Quat is not null
                    ? ReadDeltaQuat(
                        delta.Quat,
                        useByteIndices,
                        numLoopFrames,
                        assetName)
                    : null,
            Trans = delta.Trans is null
                ? null
                : ReadDeltaTrans(
                    delta.Trans,
                    useByteIndices,
                    numLoopFrames,
                    assetName)
        };
    }

    private static XAnimSourceDeltaQuatTrack ReadDeltaQuat2(
        XAnimDeltaPartQuat2 quat,
        bool useByteIndices,
        ushort numLoopFrames,
        string assetName)
    {
        const string Field = "2D delta quaternion";
        if (quat.Size == 0)
        {
            if (quat.Frame0 is null)
                throw Invalid(assetName, $"{Field} has no constant frame.");
            if (quat.Frames is not null)
                throw Invalid(assetName, $"{Field} has unexpected dynamic frames.");

            return new XAnimSourceDeltaQuatTrack
            {
                Frames2D =
                [
                    new XAnimSourceQuat2(
                        quat.Frame0.Value0,
                        quat.Frame0.Value1)
                ]
            };
        }

        if (quat.Frame0 is not null)
            throw Invalid(assetName, $"{Field} has an unexpected constant frame.");
        XAnimDeltaPartQuatDataFrames2 frames = quat.Frames ??
            throw Invalid(assetName, $"{Field} has no materialized dynamic frames.");
        int frameCount = ValidateDeltaFrameContainer(
            quat.Size,
            frames.FrameCount,
            frames.DynamicIndexByteCount,
            frames.DynamicFrames,
            frames.Frames,
            useByteIndices,
            numLoopFrames,
            assetName,
            Field);
        var values = new XAnimSourceQuat2[frameCount];
        for (int index = 0; index < values.Length; index++)
        {
            XQuat2 value = frames.Frames[index] ??
                throw Invalid(assetName, $"{Field} frame {index} is null.");
            values[index] = new XAnimSourceQuat2(value.Value0, value.Value1);
        }

        return new XAnimSourceDeltaQuatTrack
        {
            Indices = frames.DynamicFrames.FrameIndices.ToArray(),
            Frames2D = values
        };
    }

    private static XAnimSourceDeltaQuatTrack ReadDeltaQuat(
        XAnimDeltaPartQuat quat,
        bool useByteIndices,
        ushort numLoopFrames,
        string assetName)
    {
        const string Field = "3D delta quaternion";
        if (quat.Size == 0)
        {
            if (quat.Frame0 is null)
                throw Invalid(assetName, $"{Field} has no constant frame.");
            if (quat.Frames is not null)
                throw Invalid(assetName, $"{Field} has unexpected dynamic frames.");

            return new XAnimSourceDeltaQuatTrack
            {
                Is3D = true,
                Frames3D =
                [
                    new XAnimSourceQuat(
                        quat.Frame0.Value0,
                        quat.Frame0.Value1,
                        quat.Frame0.Value2,
                        quat.Frame0.Value3)
                ]
            };
        }

        if (quat.Frame0 is not null)
            throw Invalid(assetName, $"{Field} has an unexpected constant frame.");
        XAnimDeltaPartQuatDataFrames frames = quat.Frames ??
            throw Invalid(assetName, $"{Field} has no materialized dynamic frames.");
        int frameCount = ValidateDeltaFrameContainer(
            quat.Size,
            frames.FrameCount,
            frames.DynamicIndexByteCount,
            frames.DynamicFrames,
            frames.Frames,
            useByteIndices,
            numLoopFrames,
            assetName,
            Field);
        var values = new XAnimSourceQuat[frameCount];
        for (int index = 0; index < values.Length; index++)
        {
            XQuat value = frames.Frames[index] ??
                throw Invalid(assetName, $"{Field} frame {index} is null.");
            values[index] = new XAnimSourceQuat(
                value.Value0,
                value.Value1,
                value.Value2,
                value.Value3);
        }

        return new XAnimSourceDeltaQuatTrack
        {
            Is3D = true,
            Indices = frames.DynamicFrames.FrameIndices.ToArray(),
            Frames3D = values
        };
    }

    private static int ValidateDeltaFrameContainer<T>(
        ushort storedSize,
        int declaredFrameCount,
        int declaredIndexByteCount,
        XAnimDynamicFrames dynamicFrames,
        IReadOnlyList<T> frames,
        bool useByteIndices,
        ushort numLoopFrames,
        string assetName,
        string field)
    {
        int frameCount = storedSize + 1;
        if (frames is null)
            throw Invalid(assetName, $"{field} has no materialized frame list.");
        if (dynamicFrames is null)
            throw Invalid(assetName, $"{field} has no materialized frame indices.");
        if (frameCount > numLoopFrames)
        {
            throw Invalid(
                assetName,
                $"{field} has {frameCount} keyed frames but the animation has only {numLoopFrames} loop frames.");
        }
        if (declaredFrameCount != frameCount || frames.Count != frameCount)
        {
            throw Invalid(
                assetName,
                $"{field} declares {declaredFrameCount} dynamic frames and contains {frames.Count}; expected {frameCount}.");
        }

        ValidateDynamicIndices(
            dynamicFrames,
            declaredIndexByteCount,
            frameCount,
            useByteIndices,
            numLoopFrames,
            assetName,
            field);
        return frameCount;
    }

    private static XAnimSourceTransTrack ReadDeltaTrans(
        XAnimPartTrans trans,
        bool useByteIndices,
        ushort numLoopFrames,
        string assetName)
    {
        const string Field = "delta translation";
        if (trans.SmallTrans is not 0 and not 1)
        {
            throw Invalid(
                assetName,
                $"{Field} has invalid smallTrans flag {trans.SmallTrans}.");
        }

        if (trans.Size == 0)
        {
            if (trans.Frame0 is null)
                throw Invalid(assetName, $"{Field} has no constant frame.");
            if (trans.Frames is not null)
                throw Invalid(assetName, $"{Field} has unexpected dynamic frames.");
            var constant = new XAnimSourceVec3(
                trans.Frame0.X,
                trans.Frame0.Y,
                trans.Frame0.Z);
            ValidateFinite(constant, assetName, Field);
            return new XAnimSourceTransTrack
            {
                Type = XAnimSourceTransType.Constant,
                Constant = constant
            };
        }

        if (trans.Frame0 is not null)
            throw Invalid(assetName, $"{Field} has an unexpected constant frame.");
        XAnimPartTransFrames frames = trans.Frames ??
            throw Invalid(assetName, $"{Field} has no materialized dynamic frames.");
        int frameCount = trans.Size + 1;
        if (frameCount > numLoopFrames)
        {
            throw Invalid(
                assetName,
                $"{Field} has {frameCount} keyed frames but the animation has only {numLoopFrames} loop frames.");
        }
        XAnimDynamicFrames dynamicFrames = frames.DynamicFrames ??
            throw Invalid(assetName, $"{Field} has no materialized frame indices.");
        ValidateDynamicIndices(
            dynamicFrames,
            dynamicFrames.EncodedByteCount,
            frameCount,
            useByteIndices,
            numLoopFrames,
            assetName,
            Field);

        XAnimVec3 sourceMins = frames.Mins ??
            throw Invalid(assetName, $"{Field} has no materialized minimum bounds.");
        XAnimVec3 sourceSize = frames.Size ??
            throw Invalid(assetName, $"{Field} has no materialized quantization size.");
        var mins = new XAnimSourceVec3(
            sourceMins.X,
            sourceMins.Y,
            sourceMins.Z);
        var size = new XAnimSourceVec3(
            sourceSize.X,
            sourceSize.Y,
            sourceSize.Z);
        ValidateDynamicTransBounds(mins, size, assetName, Field);

        if (trans.SmallTrans != 0)
        {
            if (frames.FramePayload is not SmallXAnimTransFramePayload payload ||
                payload.Frames.Count != frameCount)
            {
                int actualCount = frames.FramePayload is SmallXAnimTransFramePayload actual
                    ? actual.Frames.Count
                    : 0;
                throw Invalid(
                    assetName,
                    $"{Field} requires {frameCount} materialized byte frames but contains {actualCount}.");
            }

            var values = new XAnimSourceSmallTrans[frameCount];
            for (int index = 0; index < values.Length; index++)
            {
                SmallXAnimTransFrame value = payload.Frames[index] ??
                    throw Invalid(assetName, $"{Field} frame {index} is null.");
                values[index] = new XAnimSourceSmallTrans(
                    value.X,
                    value.Y,
                    value.Z);
            }
            return new XAnimSourceTransTrack
            {
                Type = XAnimSourceTransType.Small,
                Indices = dynamicFrames.FrameIndices.ToArray(),
                Mins = mins,
                Size = size,
                SmallFrames = values
            };
        }

        if (frames.FramePayload is not LargeXAnimTransFramePayload largePayload ||
            largePayload.Frames.Count != frameCount)
        {
            int actualCount = frames.FramePayload is LargeXAnimTransFramePayload actual
                ? actual.Frames.Count
                : 0;
            throw Invalid(
                assetName,
                $"{Field} requires {frameCount} materialized short frames but contains {actualCount}.");
        }

        var largeValues = new XAnimSourceLargeTrans[frameCount];
        for (int index = 0; index < largeValues.Length; index++)
        {
            LargeXAnimTransFrame value = largePayload.Frames[index] ??
                throw Invalid(assetName, $"{Field} frame {index} is null.");
            largeValues[index] = new XAnimSourceLargeTrans(
                value.X,
                value.Y,
                value.Z);
        }
        return new XAnimSourceTransTrack
        {
            Type = XAnimSourceTransType.Large,
            Indices = dynamicFrames.FrameIndices.ToArray(),
            Mins = mins,
            Size = size,
            LargeFrames = largeValues
        };
    }

    private static void ValidateDynamicIndices(
        XAnimDynamicFrames dynamicFrames,
        int declaredByteCount,
        int frameCount,
        bool useByteIndices,
        ushort numLoopFrames,
        string assetName,
        string field)
    {
        if (dynamicFrames is null)
            throw Invalid(assetName, $"{field} has no materialized frame indices.");
        int expectedByteCount = checked(
            frameCount * (useByteIndices ? sizeof(byte) : sizeof(ushort)));
        if (declaredByteCount != expectedByteCount ||
            dynamicFrames.EncodedByteCount != expectedByteCount ||
            dynamicFrames.FrameIndices.Count != frameCount)
        {
            throw Invalid(
                assetName,
                $"{field} has inconsistent dynamic index data: declared {declaredByteCount} bytes, " +
                $"materialized {dynamicFrames.EncodedByteCount} bytes and {dynamicFrames.FrameIndices.Count} values; " +
                $"expected {expectedByteCount} bytes and {frameCount} values.");
        }

        ValidateIndices(
            dynamicFrames.FrameIndices,
            numLoopFrames,
            assetName,
            field);
    }

    private static void ValidateIndices(
        IReadOnlyList<ushort> indices,
        ushort numLoopFrames,
        string assetName,
        string field)
    {
        for (int index = 0; index < indices.Count; index++)
        {
            ushort value = indices[index];
            if (value >= numLoopFrames)
            {
                throw Invalid(
                    assetName,
                    $"{field} frame index {index} has value {value}, outside {numLoopFrames} loop frames.");
            }
            if (index > 0 && value <= indices[index - 1])
            {
                throw Invalid(
                    assetName,
                    $"{field} frame indices are not strictly increasing at position {index}.");
            }
        }

        if (indices.Count == numLoopFrames)
        {
            for (int index = 0; index < indices.Count; index++)
            {
                if (indices[index] != index)
                {
                    throw Invalid(
                        assetName,
                        $"{field} covers every loop frame but is not sequential at position {index}.");
                }
            }
        }
    }

    private static XAnimSourceQuat2 DecodeSimpleQuat(ushort packed)
    {
        int encodedRatio = SignExtend(packed & 0x3fff, 14);
        float ratio = encodedRatio / 8191.0f;
        float sign = (packed & 0x8000) == 0 ? 1.0f : -1.0f;
        float inverseLength = sign / MathF.Sqrt(ratio * ratio + 1.0f);
        float value = ratio * inverseLength;
        float omitted = inverseLength;
        if ((packed & 0x4000) != 0)
            (value, omitted) = (omitted, value);

        return new XAnimSourceQuat2(
            ToInt16(value),
            ToInt16(omitted));
    }

    private static XAnimSourceQuat DecodeNormalQuat(uint packed)
    {
        float ratio0 = SignExtend((int)(packed & 0x1ff), 9) / 255.0f;
        float ratio1 = SignExtend((int)((packed >> 9) & 0x3ff), 10) / 511.0f;
        float ratio2 = SignExtend((int)((packed >> 19) & 0x3ff), 10) / 511.0f;
        int rotation = (int)((packed >> 29) & 0x3);
        float sign = (packed & 0x80000000) == 0 ? 1.0f : -1.0f;
        return NormalizeAndRotate(ratio0, ratio1, ratio2, rotation, sign);
    }

    private static XAnimSourceQuat DecodePrecisionQuat(
        ushort word0,
        ushort word1,
        ushort word2)
    {
        ulong packed = (ulong)word0 << 32 | (ulong)word1 << 16 | word2;
        float ratio0 = SignExtend((int)(packed & 0x7fff), 15) / 16383.0f;
        float ratio1 = SignExtend((int)((packed >> 15) & 0x7fff), 15) / 16383.0f;
        float ratio2 = SignExtend((int)((packed >> 30) & 0x7fff), 15) / 16383.0f;
        int rotation = (int)((packed >> 45) & 0x3);
        float sign = (packed & (1UL << 47)) == 0 ? 1.0f : -1.0f;
        return NormalizeAndRotate(ratio0, ratio1, ratio2, rotation, sign);
    }

    private static XAnimSourceQuat NormalizeAndRotate(
        float ratio0,
        float ratio1,
        float ratio2,
        int rotation,
        float sign)
    {
        float inverseLength = sign / MathF.Sqrt(
            ratio0 * ratio0 +
            ratio1 * ratio1 +
            ratio2 * ratio2 +
            1.0f);
        Span<float> values =
        [
            ratio0 * inverseLength,
            ratio1 * inverseLength,
            ratio2 * inverseLength,
            inverseLength
        ];
        return new XAnimSourceQuat(
            ToInt16(values[rotation]),
            ToInt16(values[(rotation + 1) & 3]),
            ToInt16(values[(rotation + 2) & 3]),
            ToInt16(values[(rotation + 3) & 3]));
    }

    private static short ToInt16(float value)
    {
        float clamped = Math.Clamp(value, -1.0f, 1.0f);
        return (short)(clamped * short.MaxValue);
    }

    private static int SignExtend(int value, int bitCount)
    {
        int sign = 1 << (bitCount - 1);
        return (value ^ sign) - sign;
    }

    private static void ValidateDynamicTransBounds(
        XAnimSourceVec3 mins,
        XAnimSourceVec3 size,
        string assetName,
        string field)
    {
        ValidateFinite(mins, assetName, $"{field} mins");
        ValidateFinite(size, assetName, $"{field} size");
        if (size.X < 0.0f || size.Y < 0.0f || size.Z < 0.0f)
        {
            throw Invalid(assetName, $"{field} has a negative quantization size.");
        }
    }

    private static void ValidateFinite(
        XAnimSourceVec3 value,
        string assetName,
        string field)
    {
        if (!float.IsFinite(value.X) ||
            !float.IsFinite(value.Y) ||
            !float.IsFinite(value.Z))
        {
            throw Invalid(assetName, $"{field} contains a non-finite value.");
        }
    }

    private static void ValidateCString(
        string value,
        string assetName,
        string field)
    {
        if (string.IsNullOrEmpty(value) || value.Contains('\0'))
            throw Invalid(assetName, $"{field} is not a valid C string.");
    }

    private static ushort GetNumLoopFrames(
        ushort numFrames,
        string assetName)
    {
        if (numFrames == ushort.MaxValue)
        {
            throw Invalid(
                assetName,
                "uses 65535 frames, which cannot be represented by the compiled loop-frame count.");
        }

        return checked((ushort)(numFrames + 1));
    }

    private static void RequireCount<T>(
        IReadOnlyList<T> values,
        int expected,
        string assetName,
        string field)
    {
        if (values is null)
            throw Invalid(assetName, $"has no materialized {field} stream.");
        if (values.Count != expected)
        {
            throw Invalid(
                assetName,
                $"declares {expected} {field} values but contains {values.Count}.");
        }
    }

    private static InvalidDataException Invalid(
        string assetName,
        string message) =>
        new($"XAnim '{assetName}' {message}");

    private enum ConsolePartType
    {
        NoQuat = 0,
        SimpleQuat = 1,
        NormalQuat = 2,
        PrecisionQuat = 3,
        SimpleQuatNoSize = 4,
        NormalQuatNoSize = 5,
        PrecisionQuatNoSize = 6,
        SmallTrans = 7,
        Trans = 8,
        TransNoSize = 9,
        NoTrans = 10,
        All = 11
    }

    private sealed class FlatCursor
    {
        private readonly string _assetName;
        private readonly IReadOnlyList<byte> _dataByte;
        private readonly IReadOnlyList<short> _dataShort;
        private readonly IReadOnlyList<int> _dataInt;
        private readonly IReadOnlyList<byte> _randomDataByte;
        private readonly IReadOnlyList<short> _randomDataShort;
        private readonly IReadOnlyList<int> _randomDataInt;
        private readonly IReadOnlyList<ushort> _indices;

        private int _dataBytePosition;
        private int _dataShortPosition;
        private int _dataIntPosition;
        private int _randomDataBytePosition;
        private int _randomDataShortPosition;
        private int _randomDataIntPosition;
        private int _indicesPosition;

        public FlatCursor(XAnimPartsAsset asset, string assetName)
        {
            _assetName = assetName;
            XAnimPackedDataStreams streams = asset.PackedDataStreams;
            _dataByte = streams.QuantizedBytes;
            _dataShort = streams.QuantizedShorts;
            _dataInt = streams.QuantizedInts;
            _randomDataByte = streams.RandomizedQuantizedBytes;
            _randomDataShort = streams.RandomizedQuantizedShorts;
            _randomDataInt = streams.RandomizedQuantizedInts;
            _indices = asset.Indices.FrameIndices;
        }

        public byte PopByte(string field) =>
            Pop(_dataByte, ref _dataBytePosition, "dataByte", field);

        public short PopShort(string field) =>
            Pop(_dataShort, ref _dataShortPosition, "dataShort", field);

        public int PopInt(string field) =>
            Pop(_dataInt, ref _dataIntPosition, "dataInt", field);

        public byte PopRandomByte(string field) =>
            Pop(
                _randomDataByte,
                ref _randomDataBytePosition,
                "randomDataByte",
                field);

        public short PopRandomShort(string field) =>
            Pop(
                _randomDataShort,
                ref _randomDataShortPosition,
                "randomDataShort",
                field);

        public int PopRandomInt(string field) =>
            Pop(
                _randomDataInt,
                ref _randomDataIntPosition,
                "randomDataInt",
                field);

        public XAnimSourceVec3 ReadFloat3(string field) =>
            new(
                BitConverter.Int32BitsToSingle(PopInt(field)),
                BitConverter.Int32BitsToSingle(PopInt(field)),
                BitConverter.Int32BitsToSingle(PopInt(field)));

        public ushort[] ReadPackedIndices(
            ushort storedSize,
            bool useByteIndices,
            string field)
        {
            int count = storedSize + 1;
            var result = new ushort[count];
            if (useByteIndices)
            {
                for (int index = 0; index < result.Length; index++)
                    result[index] = PopByte(field);
                return result;
            }

            if (storedSize >= 64)
            {
                for (int index = 0; index < result.Length; index++)
                {
                    result[index] = Pop(
                        _indices,
                        ref _indicesPosition,
                        "indices",
                        field);
                }

                int checkpointCount = ((count - 2) / 256) + 2;
                for (int index = 0; index < checkpointCount; index++)
                    _ = PopShort($"{field} index checkpoint");
                return result;
            }

            for (int index = 0; index < result.Length; index++)
                result[index] = unchecked((ushort)PopShort(field));
            return result;
        }

        public void ExpectEnd()
        {
            ExpectEnd(_dataByte.Count, _dataBytePosition, "dataByte");
            ExpectEnd(_dataShort.Count, _dataShortPosition, "dataShort");
            ExpectEnd(_dataInt.Count, _dataIntPosition, "dataInt");
            ExpectEnd(
                _randomDataByte.Count,
                _randomDataBytePosition,
                "randomDataByte");
            ExpectEnd(
                _randomDataShort.Count,
                _randomDataShortPosition,
                "randomDataShort");
            ExpectEnd(
                _randomDataInt.Count,
                _randomDataIntPosition,
                "randomDataInt");
            ExpectEnd(_indices.Count, _indicesPosition, "indices");
        }

        private T Pop<T>(
            IReadOnlyList<T> values,
            ref int position,
            string stream,
            string field)
        {
            if (position >= values.Count)
            {
                throw Invalid(
                    _assetName,
                    $"exhausted {stream} while reading {field} at element {position}.");
            }

            return values[position++];
        }

        private void ExpectEnd(int count, int position, string stream)
        {
            if (position != count)
            {
                throw Invalid(
                    _assetName,
                    $"left {count - position} unread {stream} values after reconstructing all bone tracks.");
            }
        }
    }
}

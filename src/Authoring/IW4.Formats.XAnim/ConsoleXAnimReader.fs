namespace IW4.Formats.SourceFormat.XAnim

open System
open System.Collections.Generic
open System.IO
open IW4.Game.Assets.XAnim
open IW4.Game.ScriptStrings

module internal ConsoleXAnimReader =
    let private fail assetName message =
        raise (InvalidDataException($"XAnim '{assetName}' {message}"))

    let private requireCount (values: IReadOnlyList<'T>) expected assetName field =
        if isNull (box values) then fail assetName $"has no materialized {field} stream."
        if values.Count <> expected then
            fail assetName $"declares {expected} {field} values but contains {values.Count}."

    let private validateCString value assetName field =
        if String.IsNullOrEmpty(value) || value.Contains('\000') then
            fail assetName $"{field} is not a valid C string."

    let private validateFinite (value: XAnimSourceVec3) assetName field =
        if not (Single.IsFinite(value.X) && Single.IsFinite(value.Y) && Single.IsFinite(value.Z)) then
            fail assetName $"{field} contains a non-finite value."

    let private validateDynamicTransBounds mins size assetName field =
        validateFinite mins assetName $"{field} mins"
        validateFinite size assetName $"{field} size"
        if size.X < 0.0f || size.Y < 0.0f || size.Z < 0.0f then
            fail assetName $"{field} has a negative quantization size."

    let private getNumLoopFrames (numFrames: uint16) assetName =
        if numFrames = UInt16.MaxValue then
            fail assetName "uses 65535 frames, which cannot be represented by the compiled loop-frame count."
        uint16 (int numFrames + 1)

    let private validateIndices (indices: IReadOnlyList<uint16>) numLoopFrames assetName field =
        for index = 0 to indices.Count - 1 do
            let value = indices[index]
            if value >= numLoopFrames then
                fail assetName $"{field} frame index {index} has value {value}, outside {numLoopFrames} loop frames."
            if index > 0 && value <= indices[index - 1] then
                fail assetName $"{field} frame indices are not strictly increasing at position {index}."
        if indices.Count = int numLoopFrames then
            for index = 0 to indices.Count - 1 do
                if indices[index] <> uint16 index then
                    fail assetName $"{field} covers every loop frame but is not sequential at position {index}."

    let private readAndValidateCounts (asset: XAnimPartsAsset) assetName =
        let materializedCounts = asset.BoneCounts
        if isNull (box materializedCounts) then fail assetName "has no materialized bone-count table."
        if materializedCounts.Count <> 10 then
            fail assetName $"has {materializedCounts.Count} middle bone-count buckets; expected 10."
        let counts = Array.init (int XAnimPartType.Count) (fun index -> int (asset.GetBoneCount(enum<XAnimPartType> index)))
        let quatCount = [int XAnimPartType.NoQuat .. int XAnimPartType.PrecisionQuatNoSize] |> List.sumBy (fun index -> counts[index])
        let transCount = [int XAnimPartType.SmallTrans .. int XAnimPartType.NoTrans] |> List.sumBy (fun index -> counts[index])
        let boneCount = counts[int XAnimPartType.All]
        if quatCount <> boneCount || transCount <> boneCount then
            fail assetName $"declares {boneCount} bones but its quaternion buckets total {quatCount} and translation buckets total {transCount}."
        counts

    let private validateRootMetadata (asset: XAnimPartsAsset) assetName useByteIndices =
        let knownFlags = int XAnimFlags.Loop ||| int XAnimFlags.Delta ||| int XAnimFlags.Delta3D
        let unknownFlags = int asset.Flags &&& (~~~knownFlags) &&& 0xff
        if unknownFlags <> 0 then
            fail assetName $"has unmapped console XAnim flag bits 0x{unknownFlags:X2} in value 0x{asset.Flags:X2}."
        let deltaFlags = int XAnimFlags.Delta ||| int XAnimFlags.Delta3D
        if (int asset.Flags &&& deltaFlags) = deltaFlags then
            fail assetName $"sets both 2D and 3D delta flags in console XAnim flag value 0x{asset.Flags:X2}."
        if not (Single.IsFinite(asset.Framerate)) || asset.Framerate < 0.0f then
            fail assetName "has a non-finite or negative framerate."
        let roundedFramerate = MathF.Round(asset.Framerate, MidpointRounding.AwayFromZero)
        if roundedFramerate > float32 UInt16.MaxValue then
            fail assetName $"has framerate {asset.Framerate} which cannot be represented by the compiled source format."
        let streams = asset.PackedDataStreams
        if isNull streams then fail assetName "has no materialized packed-data streams."
        requireCount streams.QuantizedBytes (int asset.DataByteCount) assetName "dataByte"
        requireCount streams.QuantizedShorts (int asset.DataShortCount) assetName "dataShort"
        requireCount streams.QuantizedInts (int asset.DataIntCount) assetName "dataInt"
        requireCount streams.RandomizedQuantizedBytes (int asset.RandomDataByteCount) assetName "randomDataByte"
        if asset.RandomDataShortCount < 0 then
            fail assetName $"declares negative randomDataShort count {asset.RandomDataShortCount}."
        requireCount streams.RandomizedQuantizedShorts asset.RandomDataShortCount assetName "randomDataShort"
        requireCount streams.RandomizedQuantizedInts (int asset.RandomDataIntCount) assetName "randomDataInt"
        if asset.IndexCount < 0 then fail assetName $"declares negative root index count {asset.IndexCount}."
        let indices = asset.Indices
        if isNull indices then fail assetName "has no materialized root index stream."
        requireCount indices.FrameIndices asset.IndexCount assetName "indices"
        let expectedIndexBytesLong = int64 asset.IndexCount * (if useByteIndices then 1L else 2L)
        if expectedIndexBytesLong > int64 Int32.MaxValue then
            fail assetName $"declares root index data too large to materialize ({expectedIndexBytesLong} bytes)."
        let expectedIndexBytes = int expectedIndexBytesLong
        if indices.EncodedByteCount <> expectedIndexBytes || indices.IsByteEncoded <> useByteIndices then
            fail assetName $"has inconsistent root index encoding metadata: {indices.EncodedByteCount} bytes, byteEncoded={indices.IsByteEncoded}; expected {expectedIndexBytes} bytes, byteEncoded={useByteIndices}."
        let notify = asset.Notify
        if isNull (box notify) then fail assetName "has no materialized notify list."
        requireCount notify (int asset.NotifyCount) assetName "notify"

    let private readBoneNames (asset: XAnimPartsAsset) assetName boneCount =
        let names = asset.Names
        if isNull (box names) then fail assetName "has no materialized bone-name list."
        requireCount names boneCount assetName "bone name"
        Array.init boneCount (fun index ->
            let reference = names[index]
            if isNull reference then fail assetName $"bone name {index} is null."
            let value = reference.Text
            if isNull value then
                fail assetName $"bone name {index} (script string {reference.RawLocalIndex}) was not materialized."
            validateCString value assetName $"bone name {index}"
            value)

    let private readNotifies (asset: XAnimPartsAsset) assetName =
        let result = Array.init (int asset.NotifyCount) (fun index ->
            let notify = asset.Notify[index]
            if isNull notify then fail assetName $"notify {index} is null."
            let reference = notify.Name
            if isNull reference then fail assetName $"notify {index} has no script-string reference."
            let name = reference.Text
            if isNull name then
                fail assetName $"notify {index} name (script string {reference.RawLocalIndex}) was not materialized."
            validateCString name assetName $"notify {index} name"
            if not (Single.IsFinite(notify.Time)) then fail assetName $"notify {index} has a non-finite time."
            { Name = name; Time = notify.Time })
        let sourceNotifyCount =
            if result.Length > 0 && result[result.Length - 1].Name = "end" && MathF.Abs(result[result.Length - 1].Time - 1.0f) < 0.0001f then
                result.Length - 1
            else result.Length
        if sourceNotifyCount >= int Byte.MaxValue then
            fail assetName $"has {sourceNotifyCount} source notifies; the compiled format supports at most 254."
        for index = 0 to sourceNotifyCount - 1 do
            let scaled = result[index].Time * float32 asset.NumFrames
            if not (Single.IsFinite(scaled)) then
                fail assetName $"notify {index} produces a non-finite frame number."
            let frame = MathF.Round(scaled, MidpointRounding.AwayFromZero)
            if frame < 0.0f || frame > float32 UInt16.MaxValue then
                fail assetName $"notify {index} time {result[index].Time} cannot be represented by the compiled frame field."
        result

    type private FlatCursor(asset: XAnimPartsAsset, assetName: string) =
        let streams = asset.PackedDataStreams
        let dataByte = streams.QuantizedBytes
        let dataShort = streams.QuantizedShorts
        let dataInt = streams.QuantizedInts
        let randomDataByte = streams.RandomizedQuantizedBytes
        let randomDataShort = streams.RandomizedQuantizedShorts
        let randomDataInt = streams.RandomizedQuantizedInts
        let indices = asset.Indices.FrameIndices
        let mutable dataBytePosition = 0
        let mutable dataShortPosition = 0
        let mutable dataIntPosition = 0
        let mutable randomDataBytePosition = 0
        let mutable randomDataShortPosition = 0
        let mutable randomDataIntPosition = 0
        let mutable indicesPosition = 0
        let pop (values: IReadOnlyList<'T>) (position: byref<int>) stream field =
            if position >= values.Count then
                fail assetName $"exhausted {stream} while reading {field} at element {position}."
            let value = values[position]
            position <- position + 1
            value
        let expectEnd count position stream =
            if position <> count then
                fail assetName $"left {count - position} unread {stream} values after reconstructing all bone tracks."
        member _.PopByte(field) = pop dataByte &dataBytePosition "dataByte" field
        member _.PopShort(field) = pop dataShort &dataShortPosition "dataShort" field
        member _.PopInt(field) = pop dataInt &dataIntPosition "dataInt" field
        member _.PopRandomByte(field) = pop randomDataByte &randomDataBytePosition "randomDataByte" field
        member _.PopRandomShort(field) = pop randomDataShort &randomDataShortPosition "randomDataShort" field
        member _.PopRandomInt(field) = pop randomDataInt &randomDataIntPosition "randomDataInt" field
        member this.ReadFloat3(field) =
            XAnimSourceVec3(BitConverter.Int32BitsToSingle(this.PopInt(field)), BitConverter.Int32BitsToSingle(this.PopInt(field)), BitConverter.Int32BitsToSingle(this.PopInt(field)))
        member _.ReadPackedIndices(storedSize: uint16, useByteIndices, field) =
            let count = int storedSize + 1
            let result = Array.zeroCreate<uint16> count
            if useByteIndices then
                for index = 0 to count - 1 do result[index] <- uint16 (pop dataByte &dataBytePosition "dataByte" field)
            elif storedSize >= 64us then
                for index = 0 to count - 1 do result[index] <- pop indices &indicesPosition "indices" field
                let checkpointCount = ((count - 2) / 256) + 2
                for _ = 0 to checkpointCount - 1 do
                    pop dataShort &dataShortPosition "dataShort" $"{field} index checkpoint" |> ignore
            else
                for index = 0 to count - 1 do result[index] <- uint16 (pop dataShort &dataShortPosition "dataShort" field)
            result
        member _.ExpectEnd() =
            expectEnd dataByte.Count dataBytePosition "dataByte"
            expectEnd dataShort.Count dataShortPosition "dataShort"
            expectEnd dataInt.Count dataIntPosition "dataInt"
            expectEnd randomDataByte.Count randomDataBytePosition "randomDataByte"
            expectEnd randomDataShort.Count randomDataShortPosition "randomDataShort"
            expectEnd randomDataInt.Count randomDataIntPosition "randomDataInt"
            expectEnd indices.Count indicesPosition "indices"

    let private readDynamicHeader (cursor: FlatCursor) useByteIndices numLoopFrames assetName field =
        let storedSize = uint16 (cursor.PopShort(field))
        if storedSize = 0us then fail assetName $"{field} is in a dynamic part bucket but declares only one frame."
        let frameCount = int storedSize + 1
        if frameCount > int numLoopFrames then
            fail assetName $"{field} has {frameCount} keyed frames but the animation has only {numLoopFrames} loop frames."
        let indices = cursor.ReadPackedIndices(storedSize, useByteIndices, field)
        validateIndices indices numLoopFrames assetName field
        storedSize, indices

    let private readDynamicTranslationHeader (cursor: FlatCursor) useByteIndices numLoopFrames assetName field =
        let storedSize = uint16 (cursor.PopShort(field))
        if storedSize = 0us then fail assetName $"{field} is in a dynamic part bucket but declares only one frame."
        let frameCount = int storedSize + 1
        if frameCount > int numLoopFrames then
            fail assetName $"{field} has {frameCount} keyed frames but the animation has only {numLoopFrames} loop frames."
        let mins = cursor.ReadFloat3(field)
        let size = cursor.ReadFloat3(field)
        validateDynamicTransBounds mins size assetName field
        let indices = cursor.ReadPackedIndices(storedSize, useByteIndices, field)
        validateIndices indices numLoopFrames assetName field
        storedSize, mins, size, indices

    let private signExtend value bitCount =
        let sign = 1 <<< (bitCount - 1)
        (value ^^^ sign) - sign

    let private toInt16 value =
        int16 (Math.Clamp(value, -1.0f, 1.0f) * float32 Int16.MaxValue)

    let private decodeSimpleQuat (packed: uint16) =
        let encodedRatio = signExtend (int packed &&& 0x3fff) 14
        let ratio = float32 encodedRatio / 8191.0f
        let sign = if (int packed &&& 0x8000) = 0 then 1.0f else -1.0f
        let inverseLength = sign / MathF.Sqrt(ratio * ratio + 1.0f)
        let value = ratio * inverseLength
        let omitted = inverseLength
        if (int packed &&& 0x4000) <> 0 then XAnimSourceQuat2(toInt16 omitted, toInt16 value)
        else XAnimSourceQuat2(toInt16 value, toInt16 omitted)

    let private normalizeAndRotate ratio0 ratio1 ratio2 rotation sign =
        let inverseLength = sign / MathF.Sqrt(ratio0 * ratio0 + ratio1 * ratio1 + ratio2 * ratio2 + 1.0f)
        let value0 = toInt16 (ratio0 * inverseLength)
        let value1 = toInt16 (ratio1 * inverseLength)
        let value2 = toInt16 (ratio2 * inverseLength)
        let value3 = toInt16 inverseLength
        // Both packed encodings carry a two-bit rotation. Keep decoding allocation-free.
        match rotation with
        | 0 -> XAnimSourceQuat(value0, value1, value2, value3)
        | 1 -> XAnimSourceQuat(value1, value2, value3, value0)
        | 2 -> XAnimSourceQuat(value2, value3, value0, value1)
        | 3 -> XAnimSourceQuat(value3, value0, value1, value2)
        | _ -> raise (ArgumentOutOfRangeException(nameof rotation))

    let private decodeNormalQuat (packed: uint32) =
        let ratio0 = float32 (signExtend (int (packed &&& 0x1ffu)) 9) / 255.0f
        let ratio1 = float32 (signExtend (int ((packed >>> 9) &&& 0x3ffu)) 10) / 511.0f
        let ratio2 = float32 (signExtend (int ((packed >>> 19) &&& 0x3ffu)) 10) / 511.0f
        let rotation = int ((packed >>> 29) &&& 0x3u)
        let sign = if (packed &&& 0x80000000u) = 0u then 1.0f else -1.0f
        normalizeAndRotate ratio0 ratio1 ratio2 rotation sign

    let private decodePrecisionQuat (word0: uint16) (word1: uint16) (word2: uint16) =
        let packed = (uint64 word0 <<< 32) ||| (uint64 word1 <<< 16) ||| uint64 word2
        let ratio0 = float32 (signExtend (int (packed &&& 0x7fffUL)) 15) / 16383.0f
        let ratio1 = float32 (signExtend (int ((packed >>> 15) &&& 0x7fffUL)) 15) / 16383.0f
        let ratio2 = float32 (signExtend (int ((packed >>> 30) &&& 0x7fffUL)) 15) / 16383.0f
        let rotation = int ((packed >>> 45) &&& 0x3UL)
        let sign = if (packed &&& (1UL <<< 47)) = 0UL then 1.0f else -1.0f
        normalizeAndRotate ratio0 ratio1 ratio2 rotation sign

    let private readQuaternionTracks (bones: XAnimSourceBoneTrack[]) (counts: int[]) (cursor: FlatCursor) useByteIndices numLoopFrames assetName =
        let mutable boneIndex = counts[int XAnimPartType.NoQuat]
        let assign track =
            bones[boneIndex] <- { bones[boneIndex] with Quat = track }
            boneIndex <- boneIndex + 1
        for _ = 1 to counts[int XAnimPartType.SimpleQuat] do
            let field = $"bone {boneIndex} simple quaternion"
            let storedSize, indices = readDynamicHeader cursor useByteIndices numLoopFrames assetName field
            let frames = Array.init (int storedSize + 1) (fun _ -> decodeSimpleQuat (uint16 (cursor.PopRandomShort(field))))
            assign (XAnimSourceQuatTrack.SimpleAnimated(XAnimKeyFrames<XAnimSourceQuat2>.Create(indices, frames, numLoopFrames, field)))
        for _ = 1 to counts[int XAnimPartType.NormalQuat] do
            let field = $"bone {boneIndex} normal quaternion"
            let storedSize, indices = readDynamicHeader cursor useByteIndices numLoopFrames assetName field
            let frames = Array.init (int storedSize + 1) (fun _ -> decodeNormalQuat (uint32 (cursor.PopRandomInt(field))))
            assign (XAnimSourceQuatTrack.NormalAnimated(XAnimKeyFrames<XAnimSourceQuat>.Create(indices, frames, numLoopFrames, field)))
        for _ = 1 to counts[int XAnimPartType.PrecisionQuat] do
            let field = $"bone {boneIndex} precision quaternion"
            let storedSize, indices = readDynamicHeader cursor useByteIndices numLoopFrames assetName field
            let frames = Array.init (int storedSize + 1) (fun _ ->
                decodePrecisionQuat
                    (uint16 (cursor.PopRandomShort(field)))
                    (uint16 (cursor.PopRandomShort(field)))
                    (uint16 (cursor.PopRandomShort(field))))
            assign (XAnimSourceQuatTrack.NormalAnimated(XAnimKeyFrames<XAnimSourceQuat>.Create(indices, frames, numLoopFrames, field)))
        for _ = 1 to counts[int XAnimPartType.SimpleQuatNoSize] do
            let field = $"bone {boneIndex} constant simple quaternion"
            assign (XAnimSourceQuatTrack.SimpleConstant(decodeSimpleQuat (uint16 (cursor.PopShort(field)))))
        for _ = 1 to counts[int XAnimPartType.NormalQuatNoSize] do
            let field = $"bone {boneIndex} constant normal quaternion"
            assign (XAnimSourceQuatTrack.NormalConstant(decodeNormalQuat (uint32 (cursor.PopInt(field)))))
        for _ = 1 to counts[int XAnimPartType.PrecisionQuatNoSize] do
            let field = $"bone {boneIndex} constant precision quaternion"
            assign (XAnimSourceQuatTrack.NormalConstant(
                decodePrecisionQuat
                    (uint16 (cursor.PopShort(field)))
                    (uint16 (cursor.PopShort(field)))
                    (uint16 (cursor.PopShort(field)))))
        if boneIndex <> bones.Length then
            fail assetName $"quaternion reconstruction ended at bone {boneIndex} of {bones.Length}."

    let private readTranslationBone (cursor: FlatCursor) (assigned: bool[]) assetName =
        let bone = int (cursor.PopByte("translation bone index"))
        if bone >= assigned.Length then
            fail assetName $"translation stream references bone {bone}, but only {assigned.Length} bones exist."
        if assigned[bone] then fail assetName $"translation stream assigns bone {bone} more than once."
        assigned[bone] <- true
        bone

    let private readTranslationTracks (bones: XAnimSourceBoneTrack[]) (counts: int[]) (cursor: FlatCursor) useByteIndices numLoopFrames assetName =
        let assigned = Array.zeroCreate<bool> bones.Length
        let assign bone track = bones[bone] <- { bones[bone] with Trans = track }
        for _ = 1 to counts[int XAnimPartType.SmallTrans] do
            let bone = readTranslationBone cursor assigned assetName
            let field = $"bone {bone} small translation"
            let storedSize, mins, size, indices = readDynamicTranslationHeader cursor useByteIndices numLoopFrames assetName field
            let frames = Array.init (int storedSize + 1) (fun _ ->
                XAnimSourceSmallTrans(cursor.PopRandomByte(field), cursor.PopRandomByte(field), cursor.PopRandomByte(field)))
            assign bone (XAnimSourceTransTrack.Small(mins, size, XAnimKeyFrames<XAnimSourceSmallTrans>.Create(indices, frames, numLoopFrames, field)))
        for _ = 1 to counts[int XAnimPartType.Trans] do
            let bone = readTranslationBone cursor assigned assetName
            let field = $"bone {bone} translation"
            let storedSize, mins, size, indices = readDynamicTranslationHeader cursor useByteIndices numLoopFrames assetName field
            let frames = Array.init (int storedSize + 1) (fun _ ->
                XAnimSourceLargeTrans(cursor.PopRandomShort(field), cursor.PopRandomShort(field), cursor.PopRandomShort(field)))
            assign bone (XAnimSourceTransTrack.Large(mins, size, XAnimKeyFrames<XAnimSourceLargeTrans>.Create(indices, frames, numLoopFrames, field)))
        for _ = 1 to counts[int XAnimPartType.TransNoSize] do
            let bone = readTranslationBone cursor assigned assetName
            let field = $"bone {bone} constant translation"
            let constant = cursor.ReadFloat3(field)
            validateFinite constant assetName field
            assign bone (XAnimSourceTransTrack.Constant constant)
        for _ = 1 to counts[int XAnimPartType.NoTrans] do
            let bone = readTranslationBone cursor assigned assetName
            assign bone XAnimSourceTransTrack.None
        match Array.tryFindIndex not assigned with
        | Some missing -> fail assetName $"has no translation assignment for bone {missing}."
        | None -> ()

    let private validateDynamicIndices (dynamicFrames: XAnimDynamicFrames) declaredByteCount frameCount useByteIndices numLoopFrames assetName field =
        if isNull dynamicFrames then fail assetName $"{field} has no materialized frame indices."
        let expectedByteCount = frameCount * (if useByteIndices then 1 else 2)
        if declaredByteCount <> expectedByteCount ||
           dynamicFrames.EncodedByteCount <> expectedByteCount ||
           dynamicFrames.FrameIndices.Count <> frameCount then
            fail assetName $"{field} has inconsistent dynamic index data: declared {declaredByteCount} bytes, materialized {dynamicFrames.EncodedByteCount} bytes and {dynamicFrames.FrameIndices.Count} values; expected {expectedByteCount} bytes and {frameCount} values."
        validateIndices dynamicFrames.FrameIndices numLoopFrames assetName field

    let private validateDeltaFrameContainer storedSize declaredFrameCount declaredIndexByteCount
                                            (dynamicFrames: XAnimDynamicFrames) (frames: IReadOnlyList<'T>)
                                            useByteIndices numLoopFrames assetName field =
        let frameCount = int storedSize + 1
        if isNull (box frames) then fail assetName $"{field} has no materialized frame list."
        if isNull dynamicFrames then fail assetName $"{field} has no materialized frame indices."
        if frameCount > int numLoopFrames then
            fail assetName $"{field} has {frameCount} keyed frames but the animation has only {numLoopFrames} loop frames."
        if declaredFrameCount <> frameCount || frames.Count <> frameCount then
            fail assetName $"{field} declares {declaredFrameCount} dynamic frames and contains {frames.Count}; expected {frameCount}."
        validateDynamicIndices dynamicFrames declaredIndexByteCount frameCount useByteIndices numLoopFrames assetName field
        frameCount

    let private readDeltaQuat2 (quat: XAnimDeltaPartQuat2) useByteIndices numLoopFrames assetName =
        let field = "2D delta quaternion"
        if quat.Size = 0us then
            if isNull quat.Frame0 then fail assetName $"{field} has no constant frame."
            if not (isNull quat.Frames) then fail assetName $"{field} has unexpected dynamic frames."
            XAnimSourceDeltaQuatTrack.SimpleConstant(XAnimSourceQuat2(quat.Frame0.Value0, quat.Frame0.Value1))
        else
            if not (isNull quat.Frame0) then fail assetName $"{field} has an unexpected constant frame."
            let frames = quat.Frames
            if isNull frames then fail assetName $"{field} has no materialized dynamic frames."
            let frameCount = validateDeltaFrameContainer quat.Size frames.FrameCount frames.DynamicIndexByteCount
                                                        frames.DynamicFrames frames.Frames useByteIndices numLoopFrames assetName field
            let values = Array.init frameCount (fun index ->
                let value = frames.Frames[index]
                if isNull value then fail assetName $"{field} frame {index} is null."
                XAnimSourceQuat2(value.Value0, value.Value1))
            let indices = frames.DynamicFrames.FrameIndices
            XAnimSourceDeltaQuatTrack.SimpleAnimated(XAnimKeyFrames<XAnimSourceQuat2>.Create(indices, values, numLoopFrames, field))

    let private readDeltaQuat (quat: XAnimDeltaPartQuat) useByteIndices numLoopFrames assetName =
        let field = "3D delta quaternion"
        if quat.Size = 0us then
            if isNull quat.Frame0 then fail assetName $"{field} has no constant frame."
            if not (isNull quat.Frames) then fail assetName $"{field} has unexpected dynamic frames."
            XAnimSourceDeltaQuatTrack.NormalConstant(XAnimSourceQuat(quat.Frame0.Value0, quat.Frame0.Value1, quat.Frame0.Value2, quat.Frame0.Value3))
        else
            if not (isNull quat.Frame0) then fail assetName $"{field} has an unexpected constant frame."
            let frames = quat.Frames
            if isNull frames then fail assetName $"{field} has no materialized dynamic frames."
            let frameCount = validateDeltaFrameContainer quat.Size frames.FrameCount frames.DynamicIndexByteCount
                                                        frames.DynamicFrames frames.Frames useByteIndices numLoopFrames assetName field
            let values = Array.init frameCount (fun index ->
                let value = frames.Frames[index]
                if isNull value then fail assetName $"{field} frame {index} is null."
                XAnimSourceQuat(value.Value0, value.Value1, value.Value2, value.Value3))
            let indices = frames.DynamicFrames.FrameIndices
            XAnimSourceDeltaQuatTrack.NormalAnimated(XAnimKeyFrames<XAnimSourceQuat>.Create(indices, values, numLoopFrames, field))

    let private readDeltaTrans (trans: XAnimPartTrans) useByteIndices numLoopFrames assetName =
        let field = "delta translation"
        if trans.SmallTrans <> 0uy && trans.SmallTrans <> 1uy then
            fail assetName $"{field} has invalid smallTrans flag {trans.SmallTrans}."
        if trans.Size = 0us then
            if isNull trans.Frame0 then fail assetName $"{field} has no constant frame."
            if not (isNull trans.Frames) then fail assetName $"{field} has unexpected dynamic frames."
            let constant = XAnimSourceVec3(trans.Frame0.X, trans.Frame0.Y, trans.Frame0.Z)
            validateFinite constant assetName field
            XAnimSourceTransTrack.Constant constant
        else
            if not (isNull trans.Frame0) then fail assetName $"{field} has an unexpected constant frame."
            let frames = trans.Frames
            if isNull frames then fail assetName $"{field} has no materialized dynamic frames."
            let frameCount = int trans.Size + 1
            if frameCount > int numLoopFrames then
                fail assetName $"{field} has {frameCount} keyed frames but the animation has only {numLoopFrames} loop frames."
            let dynamicFrames = frames.DynamicFrames
            if isNull dynamicFrames then fail assetName $"{field} has no materialized frame indices."
            validateDynamicIndices dynamicFrames dynamicFrames.EncodedByteCount frameCount useByteIndices numLoopFrames assetName field
            let sourceMins = frames.Mins
            if isNull sourceMins then fail assetName $"{field} has no materialized minimum bounds."
            let sourceSize = frames.Size
            if isNull sourceSize then fail assetName $"{field} has no materialized quantization size."
            let mins = XAnimSourceVec3(sourceMins.X, sourceMins.Y, sourceMins.Z)
            let size = XAnimSourceVec3(sourceSize.X, sourceSize.Y, sourceSize.Z)
            validateDynamicTransBounds mins size assetName field
            if trans.SmallTrans <> 0uy then
                match frames.FramePayload with
                | :? SmallXAnimTransFramePayload as payload when payload.Frames.Count = frameCount ->
                    let values = Array.init frameCount (fun index ->
                        let value = payload.Frames[index]
                        if isNull value then fail assetName $"{field} frame {index} is null."
                        XAnimSourceSmallTrans(value.X, value.Y, value.Z))
                    XAnimSourceTransTrack.Small(mins, size, XAnimKeyFrames<XAnimSourceSmallTrans>.Create(dynamicFrames.FrameIndices, values, numLoopFrames, field))
                | other ->
                    let actualCount =
                        match other with
                        | :? SmallXAnimTransFramePayload as payload -> payload.Frames.Count
                        | _ -> 0
                    fail assetName $"{field} requires {frameCount} materialized byte frames but contains {actualCount}."
            else
                match frames.FramePayload with
                | :? LargeXAnimTransFramePayload as payload when payload.Frames.Count = frameCount ->
                    let values = Array.init frameCount (fun index ->
                        let value = payload.Frames[index]
                        if isNull value then fail assetName $"{field} frame {index} is null."
                        XAnimSourceLargeTrans(value.X, value.Y, value.Z))
                    XAnimSourceTransTrack.Large(mins, size, XAnimKeyFrames<XAnimSourceLargeTrans>.Create(dynamicFrames.FrameIndices, values, numLoopFrames, field))
                | other ->
                    let actualCount =
                        match other with
                        | :? LargeXAnimTransFramePayload as payload -> payload.Frames.Count
                        | _ -> 0
                    fail assetName $"{field} requires {frameCount} materialized short frames but contains {actualCount}."

    let private readDelta (asset: XAnimPartsAsset) useByteIndices numLoopFrames assetName =
        let delta = asset.DeltaPart
        if isNull delta then
            if asset.DeltaPartPointer.Raw <> 0 then
                fail assetName "references a delta part that was not materialized."
            None
        else
            if not (isNull delta.Quat2) && not (isNull delta.Quat) then
                fail assetName "contains both 2D and 3D delta quaternion tracks."
            if isNull delta.Quat2 && delta.Quat2Pointer.Raw <> 0 then
                fail assetName "references a 2D delta quaternion track that was not materialized."
            if isNull delta.Quat && delta.QuatPointer.Raw <> 0 then
                fail assetName "references a 3D delta quaternion track that was not materialized."
            if isNull delta.Trans && delta.TransPointer.Raw <> 0 then
                fail assetName "references a delta translation track that was not materialized."
            let quat =
                if not (isNull delta.Quat2) then Some (readDeltaQuat2 delta.Quat2 useByteIndices numLoopFrames assetName)
                elif not (isNull delta.Quat) then Some (readDeltaQuat delta.Quat useByteIndices numLoopFrames assetName)
                else None
            let trans =
                if isNull delta.Trans then None
                else Some (readDeltaTrans delta.Trans useByteIndices numLoopFrames assetName)
            Some { Quat = quat; Trans = trans }

    let Read(asset: XAnimPartsAsset, assetName: string) : XAnimSourceParts =
        ArgumentNullException.ThrowIfNull(asset)
        ArgumentException.ThrowIfNullOrWhiteSpace(assetName)
        let counts = readAndValidateCounts asset assetName
        let numLoopFrames = getNumLoopFrames asset.NumFrames assetName
        let useByteIndices = asset.NumFrames < 256us
        validateRootMetadata asset assetName useByteIndices
        let boneNames = readBoneNames asset assetName counts[int XAnimPartType.All]
        let notifies = readNotifies asset assetName
        let cursor = FlatCursor(asset, assetName)
        let bones: XAnimSourceBoneTrack[] =
            boneNames |> Array.map (fun name -> { Name = name; Quat = XAnimSourceQuatTrack.None; Trans = XAnimSourceTransTrack.None })
        readQuaternionTracks bones counts cursor useByteIndices numLoopFrames assetName
        readTranslationTracks bones counts cursor useByteIndices numLoopFrames assetName
        cursor.ExpectEnd()
        let delta = readDelta asset useByteIndices numLoopFrames assetName
        { NumFrames = asset.NumFrames
          Looped = (asset.AnimationFlags &&& XAnimFlags.Loop) <> XAnimFlags.None
          Framerate = asset.Framerate
          AssetType = asset.AssetType
          Bones = Array.AsReadOnly(bones)
          Notifies = Array.AsReadOnly(notifies)
          Delta = delta }

namespace IW4.Formats.SourceFormat.XAnim

open System
open System.Collections.Generic
open System.IO
open System.Text

module internal CompiledXAnimWriter =
    let private smallTransSizeScale = 0.003921568859368563f
    let private largeTransSizeScale = 0.00001525902189314365f

    type private EncodedQuatTrack =
        { Flip: bool
          Values: int16[] }

    let private multiplyComponent (value: int16) sign field frame componentIndex =
        let result = int value * sign
        if result < int Int16.MinValue || result > int Int16.MaxValue then
            raise (InvalidDataException(
                $"{field} frame {frame} component {componentIndex} cannot be negated without overflowing int16."))
        int16 result

    let private encodeSimpleFrames (frames: IReadOnlyList<XAnimSourceQuat2>) allowFlip field =
        if frames.Count = 0 then { Flip = false; Values = [||] }
        else
            let flip = allowFlip && frames[0].Value1 < 0s
            let values = Array.zeroCreate<int16> frames.Count
            for index = 0 to frames.Count - 1 do
                let frame = frames[index]
                let omittedNegative = frame.Value1 < 0s
                let mutable continuityNegated = false
                if index > 0 && omittedNegative <> flip then
                    let previous = frames[index - 1]
                    let dot =
                        int64 previous.Value0 * int64 frame.Value0
                        + int64 previous.Value1 * int64 frame.Value1
                    continuityNegated <- dot > 0L
                let sign = if flip <> continuityNegated then -1 else 1
                values[index] <- multiplyComponent frame.Value0 sign field index 0
            { Flip = flip; Values = values }

    let private encodeNormalFrames (frames: IReadOnlyList<XAnimSourceQuat>) allowFlip field =
        if frames.Count = 0 then { Flip = false; Values = [||] }
        else
            let flip = allowFlip && frames[0].Value3 < 0s
            let values = Array.zeroCreate<int16> (Checked.(*) frames.Count 3)
            for index = 0 to frames.Count - 1 do
                let frame = frames[index]
                let omittedNegative = frame.Value3 < 0s
                let mutable continuityNegated = false
                if index > 0 && omittedNegative <> flip then
                    let previous = frames[index - 1]
                    let dot =
                        int64 previous.Value0 * int64 frame.Value0
                        + int64 previous.Value1 * int64 frame.Value1
                        + int64 previous.Value2 * int64 frame.Value2
                        + int64 previous.Value3 * int64 frame.Value3
                    continuityNegated <- dot > 0L
                let sign = if flip <> continuityNegated then -1 else 1
                let output = index * 3
                values[output] <- multiplyComponent frame.Value0 sign field index 0
                values[output + 1] <- multiplyComponent frame.Value1 sign field index 1
                values[output + 2] <- multiplyComponent frame.Value2 sign field index 2
            { Flip = flip; Values = values }

    let private encodeQuatTrack quat field =
        match quat with
        | XAnimSourceQuatTrack.None -> { Flip = false; Values = [||] }
        | XAnimSourceQuatTrack.SimpleConstant frame ->
            encodeSimpleFrames [| frame |] true field
        | XAnimSourceQuatTrack.NormalConstant frame ->
            encodeNormalFrames [| frame |] true field
        | XAnimSourceQuatTrack.SimpleAnimated frames ->
            encodeSimpleFrames frames.Values true field
        | XAnimSourceQuatTrack.NormalAnimated frames ->
            encodeNormalFrames frames.Values true field

    let private writeValues (writer: BinaryWriter) (values: IReadOnlyList<int16>) expectedCount =
        if values.Count <> expectedCount then
            raise (InvalidDataException(
                $"A quaternion track requires {expectedCount} encoded values but contains {values.Count}."))
        for value in values do writer.Write(value)

    let private writeIndices (writer: BinaryWriter) (indices: IReadOnlyList<uint16>)
                             (numLoopFrames: uint16) useByteIndices field =
        if indices.Count < int numLoopFrames then
            if useByteIndices then
                for index in indices do
                    if index > uint16 Byte.MaxValue then
                        raise (InvalidDataException(
                            $"{field} index {index} cannot be represented by the byte index format."))
                    writer.Write(byte index)
            else
                for index in indices do writer.Write(index)

    let private writeVec3 (writer: BinaryWriter) (value: XAnimSourceVec3) =
        writer.Write(value.X)
        writer.Write(value.Y)
        writer.Write(value.Z)

    let private writeEncodedSizeComponent (writer: BinaryWriter) (value: float32)
                                          (scale: float32) field componentName =
        let encoded = value / scale
        if not (Single.IsFinite(encoded)) then
            raise (InvalidDataException(
                $"{field} size component {componentName} cannot be represented by the compiled source format."))
        writer.Write(encoded)

    let private writeEncodedSize (writer: BinaryWriter) (size: XAnimSourceVec3) smallTrans field =
        let scale = if smallTrans then smallTransSizeScale else largeTransSizeScale
        writeEncodedSizeComponent writer size.X scale field "X"
        writeEncodedSizeComponent writer size.Y scale field "Y"
        writeEncodedSizeComponent writer size.Z scale field "Z"

    let private writeCString (writer: BinaryWriter) (value: string) field =
        for character in value do
            if character = '\000' || int character > int Byte.MaxValue then
                raise (InvalidDataException($"{field} cannot be encoded as a one-byte IW4 string."))
        writer.Write(Encoding.Latin1.GetBytes(value))
        writer.Write(0uy)

    let private writeQuatTrack (writer: BinaryWriter) quat (encoded: EncodedQuatTrack)
                               (numLoopFrames: uint16) useByteIndices =
        match quat with
        | XAnimSourceQuatTrack.None -> writer.Write(0us)
        | XAnimSourceQuatTrack.SimpleConstant _ ->
            writer.Write(1us)
            writeValues writer encoded.Values 1
        | XAnimSourceQuatTrack.NormalConstant _ ->
            writer.Write(1us)
            writeValues writer encoded.Values 3
        | XAnimSourceQuatTrack.SimpleAnimated frames ->
            let frameCount = frames.Count
            writer.Write(Checked.uint16 frameCount)
            writeIndices writer frames.Indices numLoopFrames useByteIndices "bone quaternion"
            writeValues writer encoded.Values frameCount
        | XAnimSourceQuatTrack.NormalAnimated frames ->
            let frameCount = frames.Count
            writer.Write(Checked.uint16 frameCount)
            writeIndices writer frames.Indices numLoopFrames useByteIndices "bone quaternion"
            writeValues writer encoded.Values (Checked.(*) frameCount 3)

    let private writeTransTrack (writer: BinaryWriter) trans (numLoopFrames: uint16)
                                useByteIndices field =
        match trans with
        | XAnimSourceTransTrack.None -> writer.Write(0us)
        | XAnimSourceTransTrack.Constant value ->
            writer.Write(1us)
            writeVec3 writer value
        | XAnimSourceTransTrack.Small(mins, size, frames) ->
            writer.Write(Checked.uint16 frames.Count)
            writeIndices writer frames.Indices numLoopFrames useByteIndices field
            writer.Write(1uy)
            writeVec3 writer mins
            writeEncodedSize writer size true field
            for frame in frames.Values do
                writer.Write(frame.X)
                writer.Write(frame.Y)
                writer.Write(frame.Z)
        | XAnimSourceTransTrack.Large(mins, size, frames) ->
            writer.Write(Checked.uint16 frames.Count)
            writeIndices writer frames.Indices numLoopFrames useByteIndices field
            writer.Write(0uy)
            writeVec3 writer mins
            writeEncodedSize writer size false field
            for frame in frames.Values do
                writer.Write(frame.X)
                writer.Write(frame.Y)
                writer.Write(frame.Z)

    let private writeDeltaQuat (writer: BinaryWriter) quat (numLoopFrames: uint16) useByteIndices =
        match quat with
        | None -> writer.Write(0us)
        | Some (XAnimSourceDeltaQuatTrack.SimpleConstant frame) ->
            let encoded = encodeSimpleFrames [| frame |] false "2D delta quaternion"
            writer.Write(1us)
            writeValues writer encoded.Values 1
        | Some (XAnimSourceDeltaQuatTrack.NormalConstant frame) ->
            let encoded = encodeNormalFrames [| frame |] false "3D delta quaternion"
            writer.Write(1us)
            writeValues writer encoded.Values 3
        | Some (XAnimSourceDeltaQuatTrack.SimpleAnimated frames) ->
            let encoded = encodeSimpleFrames frames.Values false "2D delta quaternion"
            writer.Write(Checked.uint16 frames.Count)
            writeIndices writer frames.Indices numLoopFrames useByteIndices "2D delta quaternion"
            writeValues writer encoded.Values frames.Count
        | Some (XAnimSourceDeltaQuatTrack.NormalAnimated frames) ->
            let encoded = encodeNormalFrames frames.Values false "3D delta quaternion"
            writer.Write(Checked.uint16 frames.Count)
            writeIndices writer frames.Indices numLoopFrames useByteIndices "3D delta quaternion"
            writeValues writer encoded.Values (Checked.(*) frames.Count 3)

    let private writeNotifies (writer: BinaryWriter) (parts: XAnimSourceParts) =
        let mutable notifyCount = parts.Notifies.Count
        if notifyCount > 0 then
            let last = parts.Notifies[notifyCount - 1]
            if last.Name = "end" && MathF.Abs(last.Time - 1.0f) < 0.0001f then
                notifyCount <- notifyCount - 1
        writer.Write(Checked.byte notifyCount)
        for index = 0 to notifyCount - 1 do
            let notify = parts.Notifies[index]
            writeCString writer notify.Name $"notify {index} name"
            let frame =
                if parts.NumFrames = 0us then 0us
                else
                    Checked.uint16 (MathF.Round(notify.Time * float32 parts.NumFrames,
                                                 MidpointRounding.AwayFromZero))
            writer.Write(frame)

    let Write (stream: Stream, parts: XAnimSourceParts) =
        ArgumentNullException.ThrowIfNull(stream)
        ArgumentNullException.ThrowIfNull(parts)

        let numLoopFrames = Checked.uint16 (int parts.NumFrames + 1)
        let useByteIndices = parts.NumFrames < 256us
        let hasDelta3D =
            match parts.Delta with
            | None -> false
            | Some delta ->
                match delta.Quat with
                | Some (XAnimSourceDeltaQuatTrack.NormalConstant _)
                | Some (XAnimSourceDeltaQuatTrack.NormalAnimated _) -> true
                | None
                | Some (XAnimSourceDeltaQuatTrack.SimpleConstant _)
                | Some (XAnimSourceDeltaQuatTrack.SimpleAnimated _) -> false
        let version = if hasDelta3D then 18us else 17us

        let encodedBoneQuats =
            Array.init parts.Bones.Count (fun index ->
                let bone = parts.Bones[index]
                encodeQuatTrack bone.Quat $"bone {index} ('{bone.Name}') quaternion")

        let flags =
            (if parts.Looped then 0x01 else 0)
            ||| (if parts.Delta.IsSome then (if hasDelta3D then 0x04 else 0x02) else 0)

        use writer = new BinaryWriter(stream, Encoding.Latin1, true)
        writer.Write(version)
        writer.Write(if parts.Looped then parts.NumFrames else numLoopFrames)
        writer.Write(Checked.uint16 parts.Bones.Count)
        writer.Write(byte flags)
        writer.Write(parts.AssetType)
        writer.Write(Checked.uint16 (MathF.Round(parts.Framerate, MidpointRounding.AwayFromZero)))

        match parts.Delta with
        | None -> ()
        | Some delta ->
            writeDeltaQuat writer delta.Quat numLoopFrames useByteIndices
            match delta.Trans with
            | None -> writer.Write(0us)
            | Some trans ->
                writeTransTrack writer trans numLoopFrames useByteIndices "delta translation"

        if parts.Bones.Count <> 0 then
            let maskSize = (parts.Bones.Count + 7) / 8
            let flipMask = Array.zeroCreate<byte> maskSize
            let simpleMask = Array.zeroCreate<byte> maskSize
            for index = 0 to parts.Bones.Count - 1 do
                if encodedBoneQuats[index].Flip then
                    flipMask[index / 8] <- flipMask[index / 8] ||| byte (1 <<< (index % 8))
                match parts.Bones[index].Quat with
                | XAnimSourceQuatTrack.None
                | XAnimSourceQuatTrack.SimpleConstant _
                | XAnimSourceQuatTrack.SimpleAnimated _ ->
                    simpleMask[index / 8] <- simpleMask[index / 8] ||| byte (1 <<< (index % 8))
                | XAnimSourceQuatTrack.NormalConstant _
                | XAnimSourceQuatTrack.NormalAnimated _ -> ()
            writer.Write(flipMask)
            writer.Write(simpleMask)
            for index = 0 to parts.Bones.Count - 1 do
                writeCString writer parts.Bones[index].Name $"bone {index} name"
            for index = 0 to parts.Bones.Count - 1 do
                let bone = parts.Bones[index]
                writeQuatTrack writer bone.Quat encodedBoneQuats[index] numLoopFrames useByteIndices
                writeTransTrack writer bone.Trans numLoopFrames useByteIndices
                    $"bone {index} ('{bone.Name}') translation"

        writeNotifies writer parts

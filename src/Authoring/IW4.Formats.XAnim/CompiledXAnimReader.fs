namespace IW4.Formats.SourceFormat.XAnim

open System
open System.Collections.Generic
open System.IO
open System.Text

module internal CompiledXAnimReader =
    let private smallScale = 0.003921568859368563f
    let private largeScale = 0.00001525902189314365f

    let private readBytes (reader: BinaryReader) count =
        let bytes = reader.ReadBytes(count)
        if bytes.Length <> count then raise (EndOfStreamException())
        bytes

    let private readString (reader: BinaryReader) =
        let bytes = ResizeArray<byte>()
        let mutable terminated = false
        let mutable index = 0
        while index < 4096 && not terminated do
            let value = reader.ReadByte()
            if value = 0uy then terminated <- true else bytes.Add(value)
            index <- index + 1
        if not terminated then
            raise (InvalidDataException("Compiled XAnim string exceeds 4096 bytes."))
        Encoding.Latin1.GetString(bytes.ToArray())

    let private readVec3 (reader: BinaryReader) =
        let value = XAnimSourceVec3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle())
        if not (Single.IsFinite(value.X) && Single.IsFinite(value.Y) && Single.IsFinite(value.Z)) then
            raise (InvalidDataException("Compiled XAnim vector contains a non-finite component."))
        value

    let private readIndices (reader: BinaryReader) count (loopFrames: uint16) byteIndices =
        if count > int loopFrames then
            raise (InvalidDataException("Compiled XAnim track exceeds its loop frame count."))
        let indices = Array.zeroCreate<uint16> count
        if count = int loopFrames then
            for index = 0 to count - 1 do indices[index] <- uint16 index
        else
            let mutable prior = 0us
            for index = 0 to count - 1 do
                let frame = if byteIndices then uint16 (reader.ReadByte()) else reader.ReadUInt16()
                if frame >= loopFrames || (index > 0 && frame <= prior) then
                    raise (InvalidDataException("Compiled XAnim frame indices are invalid."))
                indices[index] <- frame
                prior <- frame
        indices

    let private restoreSimple (value: int16) flip =
        let normalized = float32 value / float32 Int16.MaxValue
        let omitted =
            int16 (MathF.Sqrt(MathF.Max(0.0f, 1.0f - normalized * normalized)) * float32 Int16.MaxValue)
        XAnimSourceQuat2(
            (if flip then int16 (-int value) else value),
            (if flip then int16 (-int omitted) else omitted))

    let private restoreNormal (x: int16) (y: int16) (z: int16) flip =
        let fx = float32 x / float32 Int16.MaxValue
        let fy = float32 y / float32 Int16.MaxValue
        let fz = float32 z / float32 Int16.MaxValue
        let w =
            int16 (MathF.Sqrt(MathF.Max(0.0f, 1.0f - fx * fx - fy * fy - fz * fz))
                   * float32 Int16.MaxValue)
        if flip then
            XAnimSourceQuat(int16 (-int x), int16 (-int y), int16 (-int z), int16 (-int w))
        else
            XAnimSourceQuat(x, y, z, w)

    let private readTrans (reader: BinaryReader) (loopFrames: uint16) byteIndices zeroMeansNull =
        let count = int (reader.ReadUInt16())
        if count = 0 then
            if zeroMeansNull then None else Some XAnimSourceTransTrack.None
        elif count = 1 then
            Some (XAnimSourceTransTrack.Constant (readVec3 reader))
        else
            let indices = readIndices reader count loopFrames byteIndices
            let small = reader.ReadByte()
            if small <> 0uy && small <> 1uy then
                raise (InvalidDataException($"Compiled XAnim translation has invalid encoding {small}."))
            let mins = readVec3 reader
            let encodedSize = readVec3 reader
            let scale = if small = 1uy then smallScale else largeScale
            let size = XAnimSourceVec3(encodedSize.X * scale, encodedSize.Y * scale, encodedSize.Z * scale)
            if small = 1uy then
                let frames =
                    Array.init count (fun _ ->
                        XAnimSourceSmallTrans(reader.ReadByte(), reader.ReadByte(), reader.ReadByte()))
                Some (XAnimSourceTransTrack.Small(mins, size,
                    XAnimKeyFrames<XAnimSourceSmallTrans>.Create(indices, frames, loopFrames, "translation")))
            else
                let frames =
                    Array.init count (fun _ ->
                        XAnimSourceLargeTrans(reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt16()))
                Some (XAnimSourceTransTrack.Large(mins, size,
                    XAnimKeyFrames<XAnimSourceLargeTrans>.Create(indices, frames, loopFrames, "translation")))

    let Read (stream: Stream, assetName: string) : XAnimSourceParts =
        use reader = new BinaryReader(stream, Encoding.Latin1, true)
        try
            let version = reader.ReadUInt16()
            if version <> 17us && version <> 18us then
                raise (InvalidDataException($"XAnim '{assetName}' has unsupported compiled version {version}."))
            let storedFrames = reader.ReadUInt16()
            let boneCount = int (reader.ReadUInt16())
            let flags = reader.ReadByte()
            if (int flags &&& (~~~0x07)) <> 0
               || (int flags &&& 0x06) = 0x06
               || (version = 17us && (int flags &&& 0x04) <> 0) then
                raise (InvalidDataException($"XAnim '{assetName}' has invalid compiled flags 0x{flags:X2}."))
            let assetType = reader.ReadByte()
            let framerate = reader.ReadUInt16()
            let looped = (int flags &&& 0x01) <> 0
            if not looped && storedFrames = 0us then
                raise (InvalidDataException($"XAnim '{assetName}' has invalid frame or rate header."))
            let numFrames = if looped then storedFrames else storedFrames - 1us
            let loopFrames = Checked.uint16 (int numFrames + 1)
            let byteIndices = numFrames < 256us

            let delta =
                if (int flags &&& 0x06) = 0 then None
                else
                    let is3D = (int flags &&& 0x04) <> 0
                    let count = int (reader.ReadUInt16())
                    let deltaQuat =
                        if count = 0 then None
                        elif count = 1 then
                            if is3D then
                                Some (XAnimSourceDeltaQuatTrack.NormalConstant
                                    (restoreNormal (reader.ReadInt16()) (reader.ReadInt16()) (reader.ReadInt16()) false))
                            else
                                Some (XAnimSourceDeltaQuatTrack.SimpleConstant
                                    (restoreSimple (reader.ReadInt16()) false))
                        else
                            let indices = readIndices reader count loopFrames byteIndices
                            if is3D then
                                let frames = Array.init count (fun _ ->
                                    restoreNormal (reader.ReadInt16()) (reader.ReadInt16()) (reader.ReadInt16()) false)
                                Some (XAnimSourceDeltaQuatTrack.NormalAnimated
                                    (XAnimKeyFrames<XAnimSourceQuat>.Create(indices, frames, loopFrames, "delta quaternion")))
                            else
                                let frames = Array.init count (fun _ -> restoreSimple (reader.ReadInt16()) false)
                                Some (XAnimSourceDeltaQuatTrack.SimpleAnimated
                                    (XAnimKeyFrames<XAnimSourceQuat2>.Create(indices, frames, loopFrames, "delta quaternion")))
                    Some { Quat = deltaQuat; Trans = readTrans reader loopFrames byteIndices true }

            let maskSize = (boneCount + 7) / 8
            let flipMask = readBytes reader maskSize
            let simpleMask = readBytes reader maskSize
            let names = Array.init boneCount (fun _ -> readString reader)
            let bones =
                Array.init boneCount (fun index ->
                    let simple = (int simpleMask[index / 8] &&& (1 <<< (index % 8))) <> 0
                    let flip = (int flipMask[index / 8] &&& (1 <<< (index % 8))) <> 0
                    let count = int (reader.ReadUInt16())
                    let quat =
                        if count = 0 then
                            if not simple || flip then
                                raise (InvalidDataException($"XAnim '{assetName}' has invalid empty bone quaternion {index}."))
                            XAnimSourceQuatTrack.None
                        elif count = 1 then
                            if simple then
                                XAnimSourceQuatTrack.SimpleConstant(restoreSimple (reader.ReadInt16()) flip)
                            else
                                XAnimSourceQuatTrack.NormalConstant(
                                    restoreNormal (reader.ReadInt16()) (reader.ReadInt16()) (reader.ReadInt16()) flip)
                        else
                            let indices = readIndices reader count loopFrames byteIndices
                            if simple then
                                let frames = Array.init count (fun _ -> restoreSimple (reader.ReadInt16()) flip)
                                XAnimSourceQuatTrack.SimpleAnimated(
                                    XAnimKeyFrames<XAnimSourceQuat2>.Create(indices, frames, loopFrames, "bone quaternion"))
                            else
                                let frames = Array.init count (fun _ ->
                                    restoreNormal (reader.ReadInt16()) (reader.ReadInt16()) (reader.ReadInt16()) flip)
                                XAnimSourceQuatTrack.NormalAnimated(
                                    XAnimKeyFrames<XAnimSourceQuat>.Create(indices, frames, loopFrames, "bone quaternion"))
                    let trans =
                        match readTrans reader loopFrames byteIndices false with
                        | Some value -> value
                        | Option.None ->
                            raise (InvalidDataException($"XAnim '{assetName}' bone {index} has no translation track."))
                    { Name = names[index]; Quat = quat; Trans = trans })

            let notifyCount = int (reader.ReadByte())
            let notifies =
                Array.init notifyCount (fun index ->
                    let name = readString reader
                    let frame = reader.ReadUInt16()
                    if frame > numFrames then
                        raise (InvalidDataException($"XAnim '{assetName}' notify {index} is outside its frame range."))
                    { Name = name
                      Time = if numFrames = 0us then 0.0f else float32 frame / float32 numFrames })
            if stream.Position <> stream.Length then
                raise (InvalidDataException($"XAnim '{assetName}' has trailing compiled data."))
            { NumFrames = numFrames
              Looped = looped
              Framerate = float32 framerate
              AssetType = assetType
              Bones = Array.AsReadOnly(bones)
              Notifies = Array.AsReadOnly(notifies)
              Delta = delta }
        with :? EndOfStreamException as ex ->
            raise (InvalidDataException($"XAnim '{assetName}' has truncated compiled data.", ex))

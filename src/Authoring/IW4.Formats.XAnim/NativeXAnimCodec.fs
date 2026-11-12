namespace IW4.Formats.SourceFormat.XAnim

open System
open System.Collections.Generic
open System.IO
open System.Text.Json
open System.Text.Json.Serialization
open IW4.Game.Assets.XAnim
open IW4.Game.Pointers
open IW4.Game.ScriptStrings

// Opt the internal wire properties into System.Text.Json without exposing these DTOs as public API.
// Byte arrays remain JSON base64 strings.
[<AllowNullLiteral>]
type internal NativeAnimation() =
    [<JsonInclude>]
    member val Format: string = null with get, set
    [<JsonInclude>]
    member val Version = 0 with get, set
    [<JsonInclude>]
    member val Name: string = null with get, set
    [<JsonInclude>]
    member val DataByteCount = 0us with get, set
    [<JsonInclude>]
    member val DataShortCount = 0us with get, set
    [<JsonInclude>]
    member val DataIntCount = 0us with get, set
    [<JsonInclude>]
    member val RandomDataByteCount = 0us with get, set
    [<JsonInclude>]
    member val RandomDataIntCount = 0us with get, set
    [<JsonInclude>]
    member val RandomDataShortCount = 0 with get, set
    [<JsonInclude>]
    member val IndexCount = 0 with get, set
    [<JsonInclude>]
    member val NumFrames = 0us with get, set
    [<JsonInclude>]
    member val Flags = 0uy with get, set
    [<JsonInclude>]
    member val DeltaFlags = 0uy with get, set
    [<JsonInclude>]
    member val BoneCounts: byte[] = null with get, set
    [<JsonInclude>]
    member val BoneNameCount = 0uy with get, set
    [<JsonInclude>]
    member val NotifyCount = 0uy with get, set
    [<JsonInclude>]
    member val AssetType = 0uy with get, set
    [<JsonInclude>]
    member val Pad1F = 0uy with get, set
    [<JsonInclude>]
    member val Framerate = 0.0f with get, set
    [<JsonInclude>]
    member val Frequency = 0.0f with get, set
    [<JsonInclude>]
    member val Names: string[] = null with get, set
    [<JsonInclude>]
    member val Notify: NativeNotify[] = null with get, set
    [<JsonInclude>]
    member val PackedDataStreams: NativeStreams = null with get, set
    [<JsonInclude>]
    member val Indices: uint16[] = null with get, set
    [<JsonInclude>]
    member val DeltaPart: NativeDelta = null with get, set

and [<AllowNullLiteral>] internal NativeNotify() =
    [<JsonInclude>]
    member val Name: string = null with get, set
    [<JsonInclude>]
    member val Time = 0.0f with get, set

and [<AllowNullLiteral>] internal NativeStreams() =
    [<JsonInclude>]
    member val QuantizedBytes: byte[] = null with get, set
    [<JsonInclude>]
    member val QuantizedShorts: int16[] = null with get, set
    [<JsonInclude>]
    member val QuantizedInts: int[] = null with get, set
    [<JsonInclude>]
    member val RandomizedQuantizedShorts: int16[] = null with get, set
    [<JsonInclude>]
    member val RandomizedQuantizedBytes: byte[] = null with get, set
    [<JsonInclude>]
    member val RandomizedQuantizedInts: int[] = null with get, set

and [<AllowNullLiteral>] internal NativeDelta() =
    [<JsonInclude>]
    member val Trans: NativeTrans = null with get, set
    [<JsonInclude>]
    member val Quat2: NativeQuat2 = null with get, set
    [<JsonInclude>]
    member val Quat: NativeQuat = null with get, set

and [<AllowNullLiteral>] internal NativeTrans() =
    [<JsonInclude>]
    member val Size = 0us with get, set
    [<JsonInclude>]
    member val SmallTrans = 0uy with get, set
    [<JsonInclude>]
    member val Pad3 = 0uy with get, set
    [<JsonInclude>]
    member val Frame0: XAnimPartTransFrame0 = null with get, set
    [<JsonInclude>]
    member val Frames: NativeTransFrames = null with get, set

and [<AllowNullLiteral>] internal NativeTransFrames() =
    [<JsonInclude>]
    member val Mins: XAnimVec3 = null with get, set
    [<JsonInclude>]
    member val Size: XAnimVec3 = null with get, set
    [<JsonInclude>]
    member val Indices: uint16[] = null with get, set
    [<JsonInclude>]
    member val SmallFrames: SmallXAnimTransFrame[] = null with get, set
    [<JsonInclude>]
    member val LargeFrames: LargeXAnimTransFrame[] = null with get, set

and [<AllowNullLiteral>] internal NativeQuat2() =
    [<JsonInclude>]
    member val Size = 0us with get, set
    [<JsonInclude>]
    member val Pad2 = 0uy with get, set
    [<JsonInclude>]
    member val Pad3 = 0uy with get, set
    [<JsonInclude>]
    member val Frame0: XQuat2 = null with get, set
    [<JsonInclude>]
    member val Frames: NativeQuat2Frames = null with get, set

and [<AllowNullLiteral>] internal NativeQuat2Frames() =
    [<JsonInclude>]
    member val Indices: uint16[] = null with get, set
    [<JsonInclude>]
    member val Values: XQuat2[] = null with get, set

and [<AllowNullLiteral>] internal NativeQuat() =
    [<JsonInclude>]
    member val Size = 0us with get, set
    [<JsonInclude>]
    member val Pad2 = 0uy with get, set
    [<JsonInclude>]
    member val Pad3 = 0uy with get, set
    [<JsonInclude>]
    member val Frame0: XQuat = null with get, set
    [<JsonInclude>]
    member val Frames: NativeQuatFrames = null with get, set

and [<AllowNullLiteral>] internal NativeQuatFrames() =
    [<JsonInclude>]
    member val Indices: uint16[] = null with get, set
    [<JsonInclude>]
    member val Values: XQuat[] = null with get, set

type private QuatBranch<'T> =
    | Constant of 'T
    | Animated of uint16[] * 'T[]

type private TransBranch =
    | TransConstant of XAnimPartTransFrame0
    | SmallAnimated of XAnimVec3 * XAnimVec3 * uint16[] * SmallXAnimTransFrame[]
    | LargeAnimated of XAnimVec3 * XAnimVec3 * uint16[] * LargeXAnimTransFrame[]

module internal NativeXAnimCodec =
    let private format = "iw4-ps3-xanim"
    let private options = JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true)

    let private fail name message = raise (InvalidDataException($"XAnim '{name}' {message}"))
    let private required (value: 'T) name message =
        if isNull (box value) then fail name message
        value

    let private text value =
        if String.IsNullOrWhiteSpace(value) || (value |> Seq.exists Char.IsControl) then
            raise (InvalidDataException("XAnim script strings must have semantic text."))
        value

    let private scriptString value =
        ScriptStringReference(0us, text value, ScriptStringHandle.Null, Unchecked.defaultof<_>)

    let private finiteVec (value: XAnimVec3) =
        Single.IsFinite(value.X) && Single.IsFinite(value.Y) && Single.IsFinite(value.Z)

    let private copy (values: IReadOnlyList<'T>) =
        if isNull (box values) then null else values |> Seq.toArray

    let private checkIndices (indices: uint16[]) numFrames name kind =
        if numFrames <= uint16 Byte.MaxValue && (indices |> Array.exists (fun value -> value > uint16 Byte.MaxValue)) then
            fail name $"has a {kind} index outside the native byte encoding."

    let private quatBranch size (frame0: 'T) hasFrames (indices: uint16[]) (values: 'T[]) numFrames name kind : QuatBranch<'T> =
        if size = 0us then
            if isNull (box frame0) || hasFrames then
                fail name $"has incomplete {kind} delta frames."
            Constant frame0
        else
            if not (isNull (box frame0)) || not hasFrames || isNull indices || indices.Length <> int size + 1 then
                fail name $"has incomplete {kind} delta frames."
            checkIndices indices numFrames name kind
            if isNull values || values.Length <> int size + 1 || values |> Array.exists (fun value -> isNull (box value)) then
                fail name $"has incomplete {kind} frames."
            Animated(indices, values)

    let private transBranch size smallTrans (frame0: XAnimPartTransFrame0) hasFrames mins extent
        (indices: uint16[]) (smallFrames: SmallXAnimTransFrame[]) (largeFrames: LargeXAnimTransFrame[]) numFrames name =
        if size = 0us then
            if isNull frame0 || hasFrames then fail name "has incomplete translation delta frames."
            TransConstant frame0
        else
            if not (isNull frame0) || not hasFrames || isNull indices || indices.Length <> int size + 1 then
                fail name "has incomplete translation delta frames."
            checkIndices indices numFrames name "translation"
            if isNull mins || isNull extent || not (finiteVec mins && finiteVec extent) then
                fail name "has incomplete translation frame payload."
            if smallTrans = 0uy then
                if not (isNull smallFrames) || isNull largeFrames || largeFrames.Length <> int size + 1 || Array.exists isNull largeFrames then
                    fail name "has incomplete translation frame payload."
                LargeAnimated(mins, extent, indices, largeFrames)
            else
                if not (isNull largeFrames) || isNull smallFrames || smallFrames.Length <> int size + 1 || Array.exists isNull smallFrames then
                    fail name "has incomplete translation frame payload."
                SmallAnimated(mins, extent, indices, smallFrames)

    let private fromNativeQuat2 (value: NativeQuat2) numFrames name =
        let frames = if isNull value.Frames then null else value.Frames.Values
        let indices = if isNull value.Frames then null else value.Frames.Indices
        quatBranch value.Size value.Frame0 (not (isNull value.Frames)) indices frames numFrames name "quat2"

    let private fromNativeQuat (value: NativeQuat) numFrames name =
        let frames = if isNull value.Frames then null else value.Frames.Values
        let indices = if isNull value.Frames then null else value.Frames.Indices
        quatBranch value.Size value.Frame0 (not (isNull value.Frames)) indices frames numFrames name "quat"

    let private fromNativeTrans (value: NativeTrans) numFrames name =
        let frames = value.Frames
        transBranch value.Size value.SmallTrans value.Frame0 (not (isNull frames))
            (if isNull frames then null else frames.Mins)
            (if isNull frames then null else frames.Size)
            (if isNull frames then null else frames.Indices)
            (if isNull frames then null else frames.SmallFrames)
            (if isNull frames then null else frames.LargeFrames)
            numFrames name

    let private fromAssetQuat2 (value: XAnimDeltaPartQuat2) numFrames name =
        let frames = value.Frames
        quatBranch value.Size value.Frame0 (not (isNull frames))
            (if isNull frames || isNull frames.DynamicFrames then null else copy frames.DynamicFrames.FrameIndices)
            (if isNull frames then null else copy frames.Frames)
            numFrames name "quat2"

    let private fromAssetQuat (value: XAnimDeltaPartQuat) numFrames name =
        let frames = value.Frames
        quatBranch value.Size value.Frame0 (not (isNull frames))
            (if isNull frames || isNull frames.DynamicFrames then null else copy frames.DynamicFrames.FrameIndices)
            (if isNull frames then null else copy frames.Frames)
            numFrames name "quat"

    let private fromAssetTrans (value: XAnimPartTrans) numFrames name =
        let frames = value.Frames
        let small, large =
            if isNull frames then null, null
            else
                match frames.FramePayload with
                | :? SmallXAnimTransFramePayload as payload ->
                    copy payload.Frames, null
                | :? LargeXAnimTransFramePayload as payload ->
                    null, copy payload.Frames
                | _ -> null, null
        transBranch value.Size value.SmallTrans value.Frame0 (not (isNull frames))
            (if isNull frames then null else frames.Mins)
            (if isNull frames then null else frames.Size)
            (if isNull frames || isNull frames.DynamicFrames then null else copy frames.DynamicFrames.FrameIndices)
            small large numFrames name

    let private toAssetQuat2 (value: NativeQuat2) numFrames name =
        match fromNativeQuat2 value numFrames name with
        | Constant frame -> XAnimDeltaPartQuat2(Size = value.Size, Pad2 = value.Pad2, Pad3 = value.Pad3, Frame0 = frame)
        | Animated(indices, frames) ->
            XAnimDeltaPartQuat2(Size = value.Size, Pad2 = value.Pad2, Pad3 = value.Pad3,
                Frames = XAnimDeltaPartQuatDataFrames2(DynamicFrames = XAnimDynamicFrames(FrameIndices = indices), Frames = frames, FrameCount = frames.Length))

    let private toAssetQuat (value: NativeQuat) numFrames name =
        match fromNativeQuat value numFrames name with
        | Constant frame -> XAnimDeltaPartQuat(Size = value.Size, Pad2 = value.Pad2, Pad3 = value.Pad3, Frame0 = frame)
        | Animated(indices, frames) ->
            XAnimDeltaPartQuat(Size = value.Size, Pad2 = value.Pad2, Pad3 = value.Pad3,
                Frames = XAnimDeltaPartQuatDataFrames(DynamicFrames = XAnimDynamicFrames(FrameIndices = indices), Frames = frames, FrameCount = frames.Length))

    let private toAssetTrans (value: NativeTrans) numFrames name =
        let makeFrames mins extent indices payload =
            XAnimPartTransFrames(Mins = mins, Size = extent, DynamicFrames = XAnimDynamicFrames(FrameIndices = indices), FramePayload = payload)
        match fromNativeTrans value numFrames name with
        | TransConstant frame -> XAnimPartTrans(Size = value.Size, SmallTrans = value.SmallTrans, Pad3 = value.Pad3, Frame0 = frame)
        | SmallAnimated(mins, extent, indices, frames) ->
            XAnimPartTrans(Size = value.Size, SmallTrans = value.SmallTrans, Pad3 = value.Pad3,
                Frames = makeFrames mins extent indices (SmallXAnimTransFramePayload(Frames = frames)))
        | LargeAnimated(mins, extent, indices, frames) ->
            XAnimPartTrans(Size = value.Size, SmallTrans = value.SmallTrans, Pad3 = value.Pad3,
                Frames = makeFrames mins extent indices (LargeXAnimTransFramePayload(Frames = frames)))

    let private toNativeQuat2 (value: XAnimDeltaPartQuat2) numFrames name =
        match fromAssetQuat2 value numFrames name with
        | Constant frame -> NativeQuat2(Size = value.Size, Pad2 = value.Pad2, Pad3 = value.Pad3, Frame0 = frame)
        | Animated(indices, frames) ->
            NativeQuat2(Size = value.Size, Pad2 = value.Pad2, Pad3 = value.Pad3,
                Frames = NativeQuat2Frames(Indices = indices, Values = frames))

    let private toNativeQuat (value: XAnimDeltaPartQuat) numFrames name =
        match fromAssetQuat value numFrames name with
        | Constant frame -> NativeQuat(Size = value.Size, Pad2 = value.Pad2, Pad3 = value.Pad3, Frame0 = frame)
        | Animated(indices, frames) ->
            NativeQuat(Size = value.Size, Pad2 = value.Pad2, Pad3 = value.Pad3,
                Frames = NativeQuatFrames(Indices = indices, Values = frames))

    let private toNativeTrans (value: XAnimPartTrans) numFrames name =
        let makeFrames mins extent indices small large =
            NativeTransFrames(Mins = mins, Size = extent, Indices = indices, SmallFrames = small, LargeFrames = large)
        match fromAssetTrans value numFrames name with
        | TransConstant frame -> NativeTrans(Size = value.Size, SmallTrans = value.SmallTrans, Pad3 = value.Pad3, Frame0 = frame)
        | SmallAnimated(mins, extent, indices, frames) ->
            NativeTrans(Size = value.Size, SmallTrans = value.SmallTrans, Pad3 = value.Pad3,
                Frames = makeFrames mins extent indices frames null)
        | LargeAnimated(mins, extent, indices, frames) ->
            NativeTrans(Size = value.Size, SmallTrans = value.SmallTrans, Pad3 = value.Pad3,
                Frames = makeFrames mins extent indices null frames)

    let private validate (asset: XAnimPartsAsset) name =
        let boneCounts = required asset.BoneCounts name "has missing native arrays."
        let names = required asset.Names name "has missing native arrays."
        let notify = required asset.Notify name "has missing native arrays."
        let streams = required asset.PackedDataStreams name "has missing native arrays."
        let indices = required asset.Indices name "has missing native arrays."
        let count (values: IReadOnlyList<'T>) =
            (required values name "has incomplete native streams or counts.").Count
        if boneCounts.Count <> 10 || int asset.BoneNameCount <> names.Count || int asset.NotifyCount <> notify.Count ||
            int asset.DataByteCount <> count streams.QuantizedBytes ||
            int asset.DataShortCount <> count streams.QuantizedShorts ||
            int asset.DataIntCount <> count streams.QuantizedInts ||
            int asset.RandomDataByteCount <> count streams.RandomizedQuantizedBytes ||
            asset.RandomDataShortCount <> count streams.RandomizedQuantizedShorts ||
            int asset.RandomDataIntCount <> count streams.RandomizedQuantizedInts ||
            asset.IndexCount <> count indices.FrameIndices ||
            not (Single.IsFinite asset.Framerate && Single.IsFinite asset.Frequency) then
            fail name "has incomplete native streams or counts."
        for value in names do
            text (if isNull value then null else value.Text) |> ignore
        for value in notify do
            if isNull value then fail name "has a null notify."
            text (if isNull value.Name then null else value.Name.Text) |> ignore
            if not (Single.IsFinite value.Time) then fail name "has a non-finite notify time."
        if asset.NumFrames <= uint16 Byte.MaxValue && (indices.FrameIndices |> Seq.exists (fun value -> value > uint16 Byte.MaxValue)) then
            fail name "has an index outside the native byte encoding."
        if isNull asset.DeltaPart then
            if asset.DeltaPartPointer.Type <> PointerType.Null then fail name "has an unmaterialized delta part."
        else
            let delta = asset.DeltaPart
            if (isNull delta.Trans && delta.TransPointer.Type <> PointerType.Null) ||
               (isNull delta.Quat2 && delta.Quat2Pointer.Type <> PointerType.Null) ||
               (isNull delta.Quat && delta.QuatPointer.Type <> PointerType.Null) then
                fail name "has an unmaterialized delta branch."

    let Serialize (asset: XAnimPartsAsset) (name: string) : string =
        validate asset name
        let streams = asset.PackedDataStreams
        let delta = asset.DeltaPart
        let nativeDelta =
            if isNull delta then null
            else
                let trans = if isNull delta.Trans then null else toNativeTrans delta.Trans asset.NumFrames name
                let quat2 = if isNull delta.Quat2 then null else toNativeQuat2 delta.Quat2 asset.NumFrames name
                let quat = if isNull delta.Quat then null else toNativeQuat delta.Quat asset.NumFrames name
                NativeDelta(Trans = trans, Quat2 = quat2, Quat = quat)
        let document = NativeAnimation(
            Format = format, Version = 1, Name = name,
            DataByteCount = asset.DataByteCount, DataShortCount = asset.DataShortCount,
            DataIntCount = asset.DataIntCount, RandomDataByteCount = asset.RandomDataByteCount,
            RandomDataIntCount = asset.RandomDataIntCount, RandomDataShortCount = asset.RandomDataShortCount,
            IndexCount = asset.IndexCount, NumFrames = asset.NumFrames, Flags = asset.Flags,
            DeltaFlags = asset.DeltaFlags, BoneCounts = (asset.BoneCounts |> Seq.toArray),
            BoneNameCount = asset.BoneNameCount, NotifyCount = asset.NotifyCount,
            AssetType = asset.AssetType, Pad1F = asset.Pad1F,
            Framerate = asset.Framerate, Frequency = asset.Frequency,
            Names = (asset.Names |> Seq.map (fun value -> text value.Text) |> Seq.toArray),
            Notify = (asset.Notify |> Seq.map (fun value -> NativeNotify(Name = text value.Name.Text, Time = value.Time)) |> Seq.toArray),
            PackedDataStreams = NativeStreams(
                QuantizedBytes = (streams.QuantizedBytes |> Seq.toArray),
                QuantizedShorts = (streams.QuantizedShorts |> Seq.toArray),
                QuantizedInts = (streams.QuantizedInts |> Seq.toArray),
                RandomizedQuantizedShorts = (streams.RandomizedQuantizedShorts |> Seq.toArray),
                RandomizedQuantizedBytes = (streams.RandomizedQuantizedBytes |> Seq.toArray),
                RandomizedQuantizedInts = (streams.RandomizedQuantizedInts |> Seq.toArray)),
            Indices = (asset.Indices.FrameIndices |> Seq.toArray),
            DeltaPart = nativeDelta)
        JsonSerializer.Serialize(document, options)

    let Deserialize (json: string) (name: string) : XAnimPartsAsset =
        let source = JsonSerializer.Deserialize<NativeAnimation>(json, options)
        if isNull source then fail name "has an empty native source document."
        if source.Format <> format || source.Version <> 1 || source.Name <> name then
            fail name "has invalid native source fields."
        let streams = required source.PackedDataStreams name "has missing native arrays."
        let boneCounts = required source.BoneCounts name "has missing native arrays."
        let names = required source.Names name "has missing native arrays."
        let notify = required source.Notify name "has missing native arrays."
        let indices = required source.Indices name "has missing native arrays."
        if isNull streams.QuantizedBytes || isNull streams.QuantizedShorts || isNull streams.QuantizedInts ||
           isNull streams.RandomizedQuantizedShorts || isNull streams.RandomizedQuantizedBytes ||
           isNull streams.RandomizedQuantizedInts then fail name "has missing packed streams."
        let delta = source.DeltaPart
        let assetDelta =
            if isNull delta then null
            else
                let trans = if isNull delta.Trans then null else toAssetTrans delta.Trans source.NumFrames name
                let quat2 = if isNull delta.Quat2 then null else toAssetQuat2 delta.Quat2 source.NumFrames name
                let quat = if isNull delta.Quat then null else toAssetQuat delta.Quat source.NumFrames name
                XAnimDeltaPart(Trans = trans, Quat2 = quat2, Quat = quat)
        let asset = XAnimPartsAsset(
            Name = name, DataByteCount = source.DataByteCount, DataShortCount = source.DataShortCount,
            DataIntCount = source.DataIntCount, RandomDataByteCount = source.RandomDataByteCount,
            RandomDataIntCount = source.RandomDataIntCount, RandomDataShortCount = source.RandomDataShortCount,
            IndexCount = source.IndexCount, NumFrames = source.NumFrames, Flags = source.Flags,
            DeltaFlags = source.DeltaFlags, BoneCounts = boneCounts,
            BoneNameCount = source.BoneNameCount, NotifyCount = source.NotifyCount,
            AssetType = source.AssetType, Pad1F = source.Pad1F,
            Framerate = source.Framerate, Frequency = source.Frequency,
            Names = (names |> Array.map scriptString),
            Notify = (notify |> Array.map (fun value ->
                if isNull value then fail name "has a null notify."
                XAnimNotifyInfo(scriptString value.Name, value.Time))),
            PackedDataStreams = XAnimPackedDataStreams(
                QuantizedBytes = streams.QuantizedBytes, QuantizedShorts = streams.QuantizedShorts,
                QuantizedInts = streams.QuantizedInts,
                RandomizedQuantizedShorts = streams.RandomizedQuantizedShorts,
                RandomizedQuantizedBytes = streams.RandomizedQuantizedBytes,
                RandomizedQuantizedInts = streams.RandomizedQuantizedInts),
            Indices = XAnimFrameIndexStream(FrameIndices = indices),
            DeltaPart = assetDelta)
        validate asset name
        asset

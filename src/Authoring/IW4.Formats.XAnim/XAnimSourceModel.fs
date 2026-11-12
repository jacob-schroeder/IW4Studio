namespace IW4.Formats.SourceFormat.XAnim

open System
open System.Collections.Generic
open System.IO

[<Struct>]
type internal XAnimSourceQuat2(value0: int16, value1: int16) =
    member _.Value0 = value0
    member _.Value1 = value1

[<Struct>]
type internal XAnimSourceQuat(value0: int16, value1: int16, value2: int16, value3: int16) =
    member _.Value0 = value0
    member _.Value1 = value1
    member _.Value2 = value2
    member _.Value3 = value3

[<Struct>]
type internal XAnimSourceVec3(x: float32, y: float32, z: float32) =
    member _.X = x
    member _.Y = y
    member _.Z = z

[<Struct>]
type internal XAnimSourceSmallTrans(x: byte, y: byte, z: byte) =
    member _.X = x
    member _.Y = y
    member _.Z = z

[<Struct>]
type internal XAnimSourceLargeTrans(x: int16, y: int16, z: int16) =
    member _.X = x
    member _.Y = y
    member _.Z = z

/// An owned animated track with at least two frames and matching, ordered indices.
/// Readers validate wire-specific encodings before constructing this semantic value.
[<Sealed>]
type internal XAnimKeyFrames<'T> private
    (indices: IReadOnlyList<uint16>, values: IReadOnlyList<'T>) =

    member _.Indices = indices
    member _.Values = values
    member _.Count = values.Count

    static member Create
        (indices: IReadOnlyList<uint16>, values: IReadOnlyList<'T>, numLoopFrames: uint16, field: string) =
        ArgumentNullException.ThrowIfNull(indices)
        ArgumentNullException.ThrowIfNull(values)
        let ownedIndices = Seq.toArray indices
        let ownedValues = Seq.toArray values
        if ownedIndices.Length < 2 || ownedIndices.Length <> ownedValues.Length then
            raise (InvalidDataException($"{field} requires at least two matching frames; it has {ownedIndices.Length} indices and {ownedValues.Length} frames."))
        for index = 0 to ownedIndices.Length - 1 do
            if ownedIndices[index] >= numLoopFrames ||
               (index > 0 && ownedIndices[index] <= ownedIndices[index - 1]) then
                raise (InvalidDataException($"{field} has invalid frame indices."))
        XAnimKeyFrames(Array.AsReadOnly(ownedIndices), Array.AsReadOnly(ownedValues))

[<RequireQualifiedAccess; NoEquality; NoComparison>]
type internal XAnimSourceQuatTrack =
    | None
    | SimpleConstant of XAnimSourceQuat2
    | NormalConstant of XAnimSourceQuat
    | SimpleAnimated of XAnimKeyFrames<XAnimSourceQuat2>
    | NormalAnimated of XAnimKeyFrames<XAnimSourceQuat>

[<RequireQualifiedAccess; NoEquality; NoComparison>]
type internal XAnimSourceTransTrack =
    | None
    | Constant of XAnimSourceVec3
    | Small of mins: XAnimSourceVec3 * size: XAnimSourceVec3 * frames: XAnimKeyFrames<XAnimSourceSmallTrans>
    | Large of mins: XAnimSourceVec3 * size: XAnimSourceVec3 * frames: XAnimKeyFrames<XAnimSourceLargeTrans>

[<NoEquality; NoComparison>]
type internal XAnimSourceBoneTrack =
    { Name: string
      Quat: XAnimSourceQuatTrack
      Trans: XAnimSourceTransTrack }

[<RequireQualifiedAccess; NoEquality; NoComparison>]
type internal XAnimSourceDeltaQuatTrack =
    | SimpleConstant of XAnimSourceQuat2
    | NormalConstant of XAnimSourceQuat
    | SimpleAnimated of XAnimKeyFrames<XAnimSourceQuat2>
    | NormalAnimated of XAnimKeyFrames<XAnimSourceQuat>

[<NoEquality; NoComparison>]
type internal XAnimSourceDeltaTrack =
    { Quat: XAnimSourceDeltaQuatTrack option
      Trans: XAnimSourceTransTrack option }

[<Struct>]
type internal XAnimSourceNotify =
    { Name: string
      Time: float32 }

[<NoEquality; NoComparison>]
type internal XAnimSourceParts =
    { NumFrames: uint16
      Looped: bool
      Framerate: float32
      AssetType: byte
      Bones: IReadOnlyList<XAnimSourceBoneTrack>
      Notifies: IReadOnlyList<XAnimSourceNotify>
      Delta: XAnimSourceDeltaTrack option }

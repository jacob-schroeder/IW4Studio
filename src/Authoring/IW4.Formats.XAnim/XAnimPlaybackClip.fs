namespace IW4.Formats.SourceFormat.XAnim

open System
open System.Collections.Generic
open System.Numerics

/// One sampled local XAnim bone transform. Rotation replaces the model-local
/// rotation; a missing quaternion stream is the native identity rotation.
/// Translation is added to the XModel's native local Trans offset.
[<Struct>]
type XAnimLocalBoneTransform =
    { Rotation: Quaternion
      Translation: Vector3 }

module private XAnimSampling =
    let private quaternionScale = 1.0f / float32 Int16.MaxValue

    let private normalize (value: Quaternion) =
        let lengthSquared = value.LengthSquared()
        if not (Single.IsFinite(lengthSquared)) || lengthSquared <= Single.Epsilon then
            Quaternion.Identity
        else
            let inverseLength = 1.0f / MathF.Sqrt(lengthSquared)
            Quaternion(value.X * inverseLength, value.Y * inverseLength,
                       value.Z * inverseLength, value.W * inverseLength)

    let private normalizeLerp (fromValue: Quaternion) (toValue: Quaternion) amount =
        let target =
            if Quaternion.Dot(fromValue, toValue) < 0.0f then
                Quaternion(-toValue.X, -toValue.Y, -toValue.Z, -toValue.W)
            else
                toValue
        normalize (Quaternion(
            fromValue.X + (target.X - fromValue.X) * amount,
            fromValue.Y + (target.Y - fromValue.Y) * amount,
            fromValue.Z + (target.Z - fromValue.Z) * amount,
            fromValue.W + (target.W - fromValue.W) * amount))

    let private decodeSimple (value: XAnimSourceQuat2) =
        normalize (Quaternion(0.0f, 0.0f,
                              float32 value.Value0 * quaternionScale,
                              float32 value.Value1 * quaternionScale))

    let private decodeNormal (value: XAnimSourceQuat) =
        normalize (Quaternion(float32 value.Value0 * quaternionScale,
                              float32 value.Value1 * quaternionScale,
                              float32 value.Value2 * quaternionScale,
                              float32 value.Value3 * quaternionScale))

    let private vector (value: XAnimSourceVec3) = Vector3(value.X, value.Y, value.Z)

    let private decodeSmall (value: XAnimSourceSmallTrans) (mins: XAnimSourceVec3) (size: XAnimSourceVec3) =
        vector mins + Vector3(float32 value.X * size.X, float32 value.Y * size.Y, float32 value.Z * size.Z)

    let private decodeLarge (value: XAnimSourceLargeTrans) (mins: XAnimSourceVec3) (size: XAnimSourceVec3) =
        // Large translation components are stored as signed words but sampled as unsigned values.
        vector mins + Vector3(float32 (uint16 value.X) * size.X,
                              float32 (uint16 value.Y) * size.Y,
                              float32 (uint16 value.Z) * size.Z)

    let private findKeyPair (indices: IReadOnlyList<uint16>) frame =
        if frame <= float32 indices[0] then
            struct (0, 0, 0.0f)
        else
            let last = indices.Count - 1
            if frame >= float32 indices[last] then
                struct (last, last, 0.0f)
            else
                let mutable low = 0
                let mutable high = last
                while high - low > 1 do
                    let middle = low + (high - low) / 2
                    if frame < float32 indices[middle] then high <- middle
                    else low <- middle
                let amount = (frame - float32 indices[low]) / float32 (int indices[high] - int indices[low])
                struct (low, high, amount)

    let rotation track frame =
        match track with
        | XAnimSourceQuatTrack.None -> Quaternion.Identity
        | XAnimSourceQuatTrack.SimpleConstant value -> decodeSimple value
        | XAnimSourceQuatTrack.NormalConstant value -> decodeNormal value
        | XAnimSourceQuatTrack.SimpleAnimated frames ->
            let struct (first, second, amount) = findKeyPair frames.Indices frame
            normalizeLerp (decodeSimple frames.Values[first]) (decodeSimple frames.Values[second]) amount
        | XAnimSourceQuatTrack.NormalAnimated frames ->
            let struct (first, second, amount) = findKeyPair frames.Indices frame
            normalizeLerp (decodeNormal frames.Values[first]) (decodeNormal frames.Values[second]) amount

    let translation track frame =
        match track with
        | XAnimSourceTransTrack.None -> Vector3.Zero
        | XAnimSourceTransTrack.Constant value -> vector value
        | XAnimSourceTransTrack.Small(mins, size, frames) ->
            let struct (first, second, amount) = findKeyPair frames.Indices frame
            Vector3.Lerp(decodeSmall frames.Values[first] mins size, decodeSmall frames.Values[second] mins size, amount)
        | XAnimSourceTransTrack.Large(mins, size, frames) ->
            let struct (first, second, amount) = findKeyPair frames.Indices frame
            Vector3.Lerp(decodeLarge frames.Values[first] mins size, decodeLarge frames.Values[second] mins size, amount)

/// Cached semantic projection of one XAnim. Compressed streams are decoded once.
/// Root-motion delta tracks remain separate and are not applied to the per-bone pose.
[<Sealed>]
type XAnimPlaybackClip internal (parts: XAnimSourceParts) =
    do ArgumentNullException.ThrowIfNull(parts)
    let boneNames = Array.AsReadOnly(parts.Bones |> Seq.map (fun bone -> bone.Name) |> Seq.toArray)

    member _.NumFrames = int parts.NumFrames
    member _.Framerate = parts.Framerate
    member _.Looped = parts.Looped
    member _.BoneCount = parts.Bones.Count
    member _.BoneNames: IReadOnlyList<string> = boneNames
    member _.DurationSeconds =
        if parts.Framerate > 0.0f then float parts.NumFrames / float parts.Framerate else 0.0

    /// Samples a frame in the inclusive range 0..NumFrames. Callers own time wrapping.
    member _.Sample(frame: float32, destination: Span<XAnimLocalBoneTransform>) =
        if not (Single.IsFinite(frame)) then
            raise (ArgumentOutOfRangeException(nameof frame))
        if destination.Length < parts.Bones.Count then
            raise (ArgumentException($"XAnim sampling requires {parts.Bones.Count} destination transforms.", nameof destination))
        let clampedFrame = Math.Clamp(frame, 0.0f, float32 parts.NumFrames)
        for index = 0 to parts.Bones.Count - 1 do
            let bone = parts.Bones[index]
            destination[index] <-
                { Rotation = XAnimSampling.rotation bone.Quat clampedFrame
                  Translation = XAnimSampling.translation bone.Trans clampedFrame }

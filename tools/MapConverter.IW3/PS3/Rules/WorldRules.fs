namespace MapConverter.Game.IW3.PS3.Rules

open System
open System.Collections.Generic
open IW4.Game.Assets.GfxMap
open IW4.Game.Assets.Material
open IW4.Game.Math

/// Keep invalid source data distinct from valid IW3 data IW4 cannot represent.
/// The stream reader supplies the fastfile/root/offset context for either case.
[<RequireQualifiedAccess>]
type internal WorldRuleError =
    | Invalid of message: string
    | Unsupported of message: string

    member this.Message =
        match this with
        | WorldRuleError.Invalid message
        | WorldRuleError.Unsupported message -> message

module internal WorldRules =
    /// Retain the world reader's double intermediate and midpoint-minus-minimum
    /// half-size calculation. Model/collision endpoint arithmetic is different.
    let EndpointBounds(mins: float32 array, maxs: float32 array) =
        if Array.exists2 (fun minimum maximum ->
            not (Single.IsFinite minimum) || not (Single.IsFinite maximum) || minimum > maximum) mins maxs then
            Error "GfxWorld has invalid source endpoint bounds"
        else
            let middle = Array.map2 (fun minimum maximum -> float32 ((float minimum + float maximum) * 0.5)) mins maxs
            let half = Array.map2 (-) middle mins
            Ok (Bounds(
                MidPoint = Vec3(X = middle[0], Y = middle[1], Z = middle[2]),
                HalfSize = Vec3(X = half[0], Y = half[1], Z = half[2])))

    let AabbTreeBounds(mins: float32 array, maxs: float32 array,
                       childCount: uint16, surfaceCount: uint16, modelCount: uint16,
                       modelPointer: uint32, childrenOffset: int32) =
        // COD4 uses inverted FLT_MAX endpoints for a childless, contentless tree.
        // Normalize its unused target bounds to zero midpoint and half-size.
        if mins.Length = 3 && maxs.Length = 3 &&
           childCount = 0us && surfaceCount = 0us && modelCount = 0us &&
           modelPointer = 0u && childrenOffset = 0x28 &&
           Array.forall (fun minimum -> minimum = Single.MaxValue) mins &&
           Array.forall (fun maximum -> maximum = -Single.MaxValue) maxs then
            Ok (Bounds(MidPoint = Vec3(), HalfSize = Vec3()))
        else
            EndpointBounds(mins, maxs)

    /// COD4 PS3 b4bf8 reads uint32 count/start at source +0x30/+0x34.
    /// An empty model may use FFFFFFFF; IW4 keeps the sentinel in uint16.
    let BrushModel(mins: float32 array, maxs: float32 array,
                   surfaceCount: uint32, firstSurface: uint32, modelIndex: int) =
        if surfaceCount > uint32 UInt16.MaxValue ||
           (firstSurface > uint32 UInt16.MaxValue &&
            not (surfaceCount = 0u && firstSurface = UInt32.MaxValue)) then
            Error (WorldRuleError.Unsupported $"GfxWorld brush model {modelIndex} surface range exceeds IW4 field widths")
        else
            EndpointBounds(mins, maxs)
            |> Result.mapError WorldRuleError.Invalid
            |> Result.map (fun bounds ->
                let half = bounds.HalfSize
                GfxBrushModel(
                    BoundsMins = [| bounds.MidPoint.X; bounds.MidPoint.Y; bounds.MidPoint.Z |],
                    BoundsMaxs = [| half.X; half.Y; half.Z |],
                    Radius = MathF.Sqrt(half.X * half.X + half.Y * half.Y + half.Z * half.Z),
                    SurfaceCount = uint16 surfaceCount,
                    StartSurfIndex = if firstSurface = UInt32.MaxValue then UInt16.MaxValue else uint16 firstSurface))

    /// IW3 PS3 0x3006fc/0x300714 uploads ARGB unchanged. IW4 0x363940
    /// instead packs authored RGBA into ARGB. Zero retains the light-grid path.
    let GroundLighting(sourceArgb: uint32) =
        GfxColor((sourceArgb <<< 8) ||| (sourceArgb >>> 24))

    /// Count the material-sorted physical draw ranges without reordering the
    /// spatial surface-index sequence addressed by cell AABBs and sky positions.
    let SurfaceRangeEnds(surfaces: IReadOnlyList<GfxSurface>, spatialOrder: uint16 array,
                         skyPositions: int array) =
        let countSurface state surfaceIndex =
            state |> Result.bind (fun (seen, opaque, transparent, unlit) ->
                if int surfaceIndex >= surfaces.Count || Set.contains surfaceIndex seen then
                    Error (WorldRuleError.Invalid "GfxWorld has an invalid or duplicate sorted surface index")
                else
                    let material = surfaces[int surfaceIndex].Material
                    if isNull material then
                        Error (WorldRuleError.Invalid $"GfxWorld surface {surfaceIndex} has no converted material")
                    else
                        let seen = Set.add surfaceIndex seen
                        match material.CameraRegion with
                        | GfxCameraRegionType.LitOpaque -> Ok (seen, opaque + 1u, transparent, unlit)
                        | GfxCameraRegionType.LitTrans -> Ok (seen, opaque, transparent + 1u, unlit)
                        | GfxCameraRegionType.None -> Ok (seen, opaque, transparent, unlit + 1u)
                        | GfxCameraRegionType.Emissive -> Ok (seen, opaque, transparent, unlit)
                        | _ -> Error (WorldRuleError.Unsupported $"world material '{material.Info.Name}' camera region"))

        Array.fold countSurface (Ok (Set.empty, 0u, 0u, 0u)) spatialOrder
        |> Result.bind (fun (_, opaque, transparent, unlit) ->
            if skyPositions |> Array.exists (fun position -> position < 0 || position >= spatialOrder.Length) then
                Error (WorldRuleError.Invalid "GfxWorld sky refers to an invalid sorted surface position")
            else
                let lit = opaque + transparent
                Ok (struct (opaque, lit, lit + unlit)))

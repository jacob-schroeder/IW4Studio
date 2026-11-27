namespace MapConverter.Game.IW3.Rules

open System
open IW4.Game.Math
open IW4.Game.Assets.Fx

module internal EffectRules =
    /// Source Fx and sound names use a leading comma for an external
    /// definition. The converted map owns the resolved source definition.
    let ReferenceName(prefix: string, sourceName: string) : string =
        prefix + (if sourceName.StartsWith(",", StringComparison.Ordinal) then sourceName.Substring(1) else sourceName)

    /// IW4 inserted SparkCloud and SparkFountain after Cloud; source types
    /// Model through Runner must skip those two target slots.
    let ElementType(sourceType: byte) : Result<FxElemType, string> =
        match sourceType with
        | 0uy -> Ok FxElemType.SpriteBillboard
        | 1uy -> Ok FxElemType.SpriteOriented
        | 2uy -> Ok FxElemType.Tail
        | 3uy -> Ok FxElemType.Trail
        | 4uy -> Ok FxElemType.Cloud
        | 5uy -> Ok FxElemType.Model
        | 6uy -> Ok FxElemType.OmniLight
        | 7uy -> Ok FxElemType.SpotLight
        | 8uy -> Ok FxElemType.Sound
        | 9uy -> Ok FxElemType.Decal
        | 10uy -> Ok FxElemType.Runner
        | _ -> Error $"COD4 Fx element type {sourceType}"

    /// COD4 has only split distance. Zero disables it, and the absent IW4
    /// arc/time modes stay disabled. Catalog capture retains permissive reads.
    let TrailSplit(splitDistance: int, converting: bool) : Result<struct (float32 * float32 * float32), string> =
        if converting && splitDistance < 0 then
            Error "Fx trail split distance is negative"
        else
            let inverseDistance = if splitDistance = 0 then 0.0f else 1.0f / float32 splitDistance
            Ok (struct (inverseDistance, 0.0f, 0.0f))

    /// Retain the double midpoint intermediate and rounded-midpoint-minus-min
    /// half size used by the source reader; validate only during conversion.
    let CollisionBounds(minimum: Vec3, maximum: Vec3, converting: bool) : Result<Bounds, string> =
        if converting &&
           (not (Single.IsFinite minimum.X) || not (Single.IsFinite maximum.X) || minimum.X > maximum.X ||
            not (Single.IsFinite minimum.Y) || not (Single.IsFinite maximum.Y) || minimum.Y > maximum.Y ||
            not (Single.IsFinite minimum.Z) || not (Single.IsFinite maximum.Z) || minimum.Z > maximum.Z) then
            Error "Fx collision bounds are invalid"
        else
            let middleX = float32 ((float minimum.X + float maximum.X) * 0.5)
            let middleY = float32 ((float minimum.Y + float maximum.Y) * 0.5)
            let middleZ = float32 ((float minimum.Z + float maximum.Z) * 0.5)
            Ok (Bounds(
                Vec3(middleX, middleY, middleZ),
                Vec3(middleX - minimum.X, middleY - minimum.Y, middleZ - minimum.Z)))

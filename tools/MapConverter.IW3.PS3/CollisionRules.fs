namespace MapConverter.Game.IW3.PS3.Rules

open System
open System.Collections.Generic
open System.IO
open System.Linq
open IW4.Game.Assets.ColMap
open IW4.Game.Math

module internal CollisionRules =
    let BoundsFromEndpoints (min: Vec3) (max: Vec3) : Bounds =
        if min.X > max.X || min.Y > max.Y || min.Z > max.Z then
            raise (InvalidDataException("An IW3 PS3 model has inverted bounds."))

        Bounds(
            MidPoint = Vec3(X = (min.X + max.X) / 2.0f,
                            Y = (min.Y + max.Y) / 2.0f,
                            Z = (min.Z + max.Z) / 2.0f),
            HalfSize = Vec3(X = (max.X - min.X) / 2.0f,
                            Y = (max.Y - min.Y) / 2.0f,
                            Z = (max.Z - min.Z) / 2.0f))

    let CreateClipStaticModelRoot (models: IReadOnlyList<ClipStaticModel>) : SModelAabbNode =
        // IW4 starts point traces at node zero even with no static models.
        if models.Count = 0 then
            SModelAabbNode()
        else
            let minX = Enumerable.Min<ClipStaticModel>(models, Func<ClipStaticModel, float32>(fun model -> model.Bounds.MidPoint.X - model.Bounds.HalfSize.X))
            let minY = Enumerable.Min<ClipStaticModel>(models, Func<ClipStaticModel, float32>(fun model -> model.Bounds.MidPoint.Y - model.Bounds.HalfSize.Y))
            let minZ = Enumerable.Min<ClipStaticModel>(models, Func<ClipStaticModel, float32>(fun model -> model.Bounds.MidPoint.Z - model.Bounds.HalfSize.Z))
            let maxX = Enumerable.Max<ClipStaticModel>(models, Func<ClipStaticModel, float32>(fun model -> model.Bounds.MidPoint.X + model.Bounds.HalfSize.X))
            let maxY = Enumerable.Max<ClipStaticModel>(models, Func<ClipStaticModel, float32>(fun model -> model.Bounds.MidPoint.Y + model.Bounds.HalfSize.Y))
            let maxZ = Enumerable.Max<ClipStaticModel>(models, Func<ClipStaticModel, float32>(fun model -> model.Bounds.MidPoint.Z + model.Bounds.HalfSize.Z))

            SModelAabbNode(
                Bounds = Bounds(
                    MidPoint = Vec3(X = (minX + maxX) * 0.5f,
                                    Y = (minY + maxY) * 0.5f,
                                    Z = (minZ + maxZ) * 0.5f),
                    HalfSize = Vec3(X = (maxX - minX) * 0.5f,
                                    Y = (maxY - minY) * 0.5f,
                                    Z = (maxZ - minZ) * 0.5f)),
                FirstChild = 0us,
                ChildCount = Checked.uint16 models.Count)

    let CreateAabb (origin: Vec3) (halfSize: Vec3) (materialIndex: uint16)
                   (childCount: uint16) (firstChildOrPartitionIndex: int)
                   (index: int) (count: int) (materialCount: int)
                   (partitionCount: int) : Result<CollisionAabbTree, string> =
        let tree = CollisionAabbTree(
            Origin = origin,
            HalfSize = halfSize,
            MaterialIndex = materialIndex,
            ChildCount = childCount,
            FirstChildOrPartitionIndex = firstChildOrPartitionIndex)

        if not (Single.IsFinite tree.Origin.X) || not (Single.IsFinite tree.Origin.Y) ||
           not (Single.IsFinite tree.Origin.Z) || not (Single.IsFinite tree.HalfSize.X) ||
           not (Single.IsFinite tree.HalfSize.Y) || not (Single.IsFinite tree.HalfSize.Z) ||
           tree.HalfSize.X < 0.0f || tree.HalfSize.Y < 0.0f || tree.HalfSize.Z < 0.0f ||
           int tree.MaterialIndex >= materialCount then
            Error(sprintf "clipmap AABB %d has invalid bounds or material" index)
        elif firstChildOrPartitionIndex < 0 ||
             (if childCount = 0us then
                  firstChildOrPartitionIndex >= partitionCount
              else
                  int64 firstChildOrPartitionIndex + int64 childCount > int64 count) then
            Error(sprintf "clipmap AABB %d has an invalid child or partition range" index)
        else
            Ok tree

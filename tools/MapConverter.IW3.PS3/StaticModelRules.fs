namespace MapConverter.Game.IW3.PS3.Rules

open System
open System.Collections.Generic
open System.IO
open System.Linq
open IW4.Game.Assets.GfxMap
open IW4.Game.Assets.XModel
open IW4.Game.Math

[<NoEquality; NoComparison>]
type internal StaticModelConversion =
    { Dpvs: GfxWorldDpvsStatic
      CellTrees: GfxCellTree array
      CellTreeCounts: GfxCellTreeCount array
      ShadowGeometry: GfxShadowGeometry array
      SplitModels: XModelAsset array array }

module internal StaticModelRules =
    let private invalid message = raise (InvalidDataException message)
    let private unsupported message = raise (NotSupportedException message)

    let private coveringBounds (bounds: Bounds array) =
        let minimum axis = Enumerable.Min<Bounds>(bounds, Func<Bounds, float32>(fun b -> axis b.MidPoint - axis b.HalfSize))
        let maximum axis = Enumerable.Max<Bounds>(bounds, Func<Bounds, float32>(fun b -> axis b.MidPoint + axis b.HalfSize))
        let minX, maxX = minimum (fun v -> v.X), maximum (fun v -> v.X)
        let minY, maxY = minimum (fun v -> v.Y), maximum (fun v -> v.Y)
        let minZ, maxZ = minimum (fun v -> v.Z), maximum (fun v -> v.Z)
        Bounds(
            MidPoint = Vec3(X = (minX + maxX) * 0.5f, Y = (minY + maxY) * 0.5f, Z = (minZ + maxZ) * 0.5f),
            HalfSize = Vec3(X = (maxX - minX) * 0.5f, Y = (maxY - minY) * 0.5f, Z = (maxZ - minZ) * 0.5f))

    /// Source-authored static tables only. No source assets or index arrays are
    /// mutated; dependent runtime storage is allocated by the existing linker.
    let Convert(dpvs: GfxWorldDpvsStatic, cells: GfxCellTree array, shadows: GfxShadowGeometry array) =
        if dpvs.SModelCount <> uint32 dpvs.SModelDrawInsts.Count ||
           dpvs.SModelInsts.Count <> dpvs.SModelDrawInsts.Count || dpvs.VisibilityCounts.Count <> 8 then
            invalid "GfxWorld static-model counts do not match their authored tables."
        if dpvs.SModelVisData.Count <> 0 || dpvs.SModelVisDataPointers.Count <> 0 then
            unsupported "Static-model conversion requires authored visibility storage, not populated runtime visibility."

        let partsByName = Dictionary<string, XModelAsset array>(StringComparer.Ordinal)
        let sourceByName = Dictionary<string, XModelAsset>(StringComparer.Ordinal)
        let splits = ResizeArray<XModelAsset array>()
        let draws = ResizeArray<GfxStaticModelDrawInst>()
        let instances = ResizeArray<GfxStaticModelInst>()
        let remap = Array.zeroCreate<uint16 array> dpvs.SModelDrawInsts.Count
        for index = 0 to dpvs.SModelDrawInsts.Count - 1 do
            let row = dpvs.SModelDrawInsts[index]
            let model = row.Model
            if isNull model || String.IsNullOrWhiteSpace model.Name then
                invalid $"GfxWorld static model {index} has no named XModel."
            let parts =
                match partsByName.TryGetValue model.Name with
                | true, existing ->
                    if not (Object.ReferenceEquals(sourceByName[model.Name], model)) then
                        invalid $"GfxWorld has conflicting static model definitions for '{model.Name}'."
                    existing
                | _ ->
                    let converted = ModelRules.SplitStaticModel model
                    partsByName.Add(model.Name, converted)
                    sourceByName.Add(model.Name, model)
                    if converted.Length > 1 then splits.Add converted
                    converted
            if parts.Length > 1 && row.FirstMaterialSkinIndex <> 0uy then
                unsupported $"Static model '{model.Name}' needs material-skin remapping before it can be split."
            if int64 draws.Count + int64 parts.Length > int64 UInt16.MaxValue + 1L then
                unsupported "Splitting static models exceeds IW4's 16-bit model-index range."
            remap[index] <- Array.init parts.Length (fun part -> uint16 (draws.Count + part))
            for part in parts do
                draws.Add(
                    GfxStaticModelDrawInst(
                        Placement = row.Placement, Model = part,
                        CullDist = row.CullDist, LightingHandle = row.LightingHandle,
                        ReflectionProbeIndex = row.ReflectionProbeIndex, PrimaryLightIndex = row.PrimaryLightIndex,
                        Flags = row.Flags, FirstMaterialSkinIndex = row.FirstMaterialSkinIndex,
                        GroundLighting = row.GroundLighting))
                instances.Add dpvs.SModelInsts[index]

        let expandIndices label (indices: IReadOnlyList<uint16>) =
            indices |> Seq.collect (fun index ->
                if int index >= remap.Length then invalid $"{label} references missing static model {index}."
                remap[int index]) |> Seq.toArray

        // Native PS3 0x352590 remaps each cell root's model list after sorting
        // draws, but not independent child lists. Bloc staticmodels07 retained
        // one complete terminal node per cell to avoid stale child indices.
        // This preserves cell/portal visibility at the cost of coarser culling.
        let trees = cells |> Array.mapi (fun cellIndex cell ->
            if cell.AabbTrees.Count = 0 then cell
            else
                let root = cell.AabbTrees[0]
                let start = int root.StartSurfIndex
                let finish = start + int root.SurfaceCount
                if finish > dpvs.SortedSurfIndex.Count then
                    invalid $"GfxWorld cell {cellIndex} has an invalid root surface span."
                if int root.SModelIndexCount <> root.SModelIndexes.Count then
                    invalid $"GfxWorld cell {cellIndex} has an inconsistent root model count."
                let sourceIds = root.SModelIndexes |> Set.ofSeq
                if sourceIds.Count <> root.SModelIndexes.Count then
                    invalid $"GfxWorld cell {cellIndex} has duplicate root model indices."
                for node in cell.AabbTrees do
                    if int node.SModelIndexCount <> node.SModelIndexes.Count ||
                       (node.SModelIndexes |> Seq.exists (fun id -> not (Set.contains id sourceIds))) then
                        unsupported $"GfxWorld cell {cellIndex} has child models absent from its aggregate root."
                    if node.SurfaceCount <> 0us &&
                       (int node.StartSurfIndex < start || int node.StartSurfIndex + int node.SurfaceCount > finish) then
                        unsupported $"GfxWorld cell {cellIndex} has child surfaces absent from its aggregate root."
                let ids = expandIndices $"GfxWorld cell {cellIndex}" root.SModelIndexes
                if ids.Length > GfxAabbTree.MaxIndexCount || int root.SurfaceCount > GfxAabbTree.MaxIndexCount then
                    unsupported $"GfxWorld cell {cellIndex} exceeds the native visibility scratch-buffer limit after expansion."
                let bounds =
                    [| yield! cell.AabbTrees |> Seq.map (fun node -> node.Bounds)
                       for id in ids do yield instances[int id].Bounds
                       for position = start to finish - 1 do
                           let surface = int dpvs.SortedSurfIndex[position]
                           if surface >= dpvs.SurfaceBounds.Count then
                               invalid $"GfxWorld cell {cellIndex} references a missing surface bound."
                           yield dpvs.SurfaceBounds[surface].Bounds |]
                GfxCellTree(AabbTrees = [|
                    GfxAabbTree(
                        Bounds = coveringBounds bounds,
                        SurfaceCount = root.SurfaceCount, StartSurfIndex = root.StartSurfIndex,
                        ChildCount = 0us, ChildrenOffset = 0,
                        SModelIndexCount = uint16 ids.Length, SModelIndexes = Array.AsReadOnly ids) |]))

        let shadowGeometry = shadows |> Array.mapi (fun index row ->
            if int row.SModelCount <> row.SModelIndex.Count then
                invalid $"GfxWorld shadow geometry {index} has an inconsistent model count."
            let ids = expandIndices $"GfxWorld shadow geometry {index}" row.SModelIndex
            if ids.Length > int UInt16.MaxValue then
                unsupported $"GfxWorld shadow geometry {index} exceeds IW4's model-count field after expansion."
            GfxShadowGeometry(
                SurfaceCount = row.SurfaceCount, SortedSurfIndexPointer = row.SortedSurfIndexPointer,
                SortedSurfIndex = row.SortedSurfIndex, SModelCount = uint16 ids.Length,
                SModelIndex = Array.AsReadOnly ids))

        let visibility = dpvs.VisibilityCounts |> Seq.toArray
        visibility[6] <- max visibility[6] (uint32 ((draws.Count + 127) / 128 * 4))
        let converted = GfxWorldDpvsStatic(
            SModelCount = uint32 draws.Count, StaticSurfaceCount = dpvs.StaticSurfaceCount,
            LitSurfsBegin = dpvs.LitSurfsBegin, LitSurfsEnd = dpvs.LitSurfsEnd,
            VisibilityCounts = Array.AsReadOnly visibility,
            SurfaceVisDataPointers = dpvs.SurfaceVisDataPointers, SurfaceVisData = dpvs.SurfaceVisData,
            SortedSurfIndexPointer = dpvs.SortedSurfIndexPointer, SortedSurfIndexAddress = dpvs.SortedSurfIndexAddress,
            SortedSurfIndex = dpvs.SortedSurfIndex, SModelInsts = instances.AsReadOnly(),
            SurfacesPointer = dpvs.SurfacesPointer, SurfacesAddress = dpvs.SurfacesAddress, Surfaces = dpvs.Surfaces,
            AuthoredSurfaceIndexByRuntimeSlot = dpvs.AuthoredSurfaceIndexByRuntimeSlot,
            SurfaceBoundsPointer = dpvs.SurfaceBoundsPointer, SurfaceBoundsAddress = dpvs.SurfaceBoundsAddress,
            SurfaceBounds = dpvs.SurfaceBounds, SModelDrawInsts = draws.AsReadOnly(),
            SurfaceMaterialsPointer = dpvs.SurfaceMaterialsPointer, SurfaceMaterialsAddress = dpvs.SurfaceMaterialsAddress,
            SurfaceMaterials = dpvs.SurfaceMaterials, SurfaceCastsSunShadowPointer = dpvs.SurfaceCastsSunShadowPointer,
            SurfaceCastsSunShadowAddress = dpvs.SurfaceCastsSunShadowAddress,
            SurfaceCastsSunShadow = dpvs.SurfaceCastsSunShadow, UsageCount = dpvs.UsageCount)
        { Dpvs = converted
          CellTrees = trees
          CellTreeCounts = trees |> Array.map (fun cell -> GfxCellTreeCount(uint32 cell.AabbTrees.Count))
          ShadowGeometry = shadowGeometry
          SplitModels = splits.ToArray() }

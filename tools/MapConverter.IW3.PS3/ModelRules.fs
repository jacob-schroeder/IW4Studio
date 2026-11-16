namespace MapConverter.Game.IW3.PS3.Rules

open System
open System.IO
open System.Globalization
open IW4.Game.Assets.GfxMap
open IW4.Game.Assets.XModel

module internal ModelRules =
    let SplitStaticModel (model: XModelAsset) : XModelAsset array =
        if isNull model then nullArg (nameof model)

        let activeLods = model.Lods |> Seq.truncate (int model.NumLods) |> Seq.toArray
        if activeLods.Length <> int model.NumLods then
            raise (InvalidDataException("Static model has fewer LOD rows than NumLods."))

        let limit = GfxStaticModelDrawInst.MaxLodSurfaceCount
        if activeLods |> Array.forall (fun lod -> not (isNull lod) && int lod.NumSurfs <= limit) then
            [| model |]
        else
            let name = model.Name
            if String.IsNullOrWhiteSpace name then
                raise (InvalidDataException("Oversized static model has no name."))
            if model.NumLods <> 1uy || model.CollLod <> Byte.MaxValue ||
               model.NumCollSurfs <> 0 || model.CollSurfs.Count <> 0 ||
               (model.InvHighMipRadius |> Seq.exists (fun radius -> radius <> 0us)) then
                raise (NotSupportedException($"Static model '{name}' cannot be split: only one collision-free LOD with zero mip radii is supported."))

            let lod = activeLods[0]
            if isNull lod then
                raise (InvalidDataException($"Static model '{name}' has no active LOD."))
            let mesh = lod.ModelSurfs
            if isNull mesh || String.IsNullOrWhiteSpace mesh.Name ||
               lod.SurfIndex <> 0us || int lod.NumSurfs <> int model.NumSurfs ||
               int mesh.NumSurfs <> int model.NumSurfs || mesh.Surfaces.Count <> int model.NumSurfs ||
               model.Materials.Count <> int model.NumSurfs ||
               model.InvHighMipRadius.Count <> int model.NumSurfs then
                raise (InvalidDataException($"Static model '{name}' has inconsistent LOD, surface, material, or mip tables."))
            if model.Lods |> Seq.skip 1 |> Seq.exists (fun inactive ->
                isNull inactive || inactive.NumSurfs <> 0us || not (isNull inactive.ModelSurfs)) then
                raise (NotSupportedException($"Static model '{name}' has populated inactive LODs."))
            if model.BoneInfo.Count <> int model.NumBones then
                raise (InvalidDataException($"Static model '{name}' has an incomplete BoneInfo table."))
            if mesh.Surfaces |> Seq.exists (fun surface ->
                isNull surface || surface.PartBits.Count <> 6) then
                raise (InvalidDataException($"Static model '{name}' has incomplete surface part bits."))

            // Bloc staticmodels05 split the one-LOD apartments at the native four-bit draw-token limit.
            // staticmodels06 established that each part needs its own inline BoneInfo allocation.
            [| for first in 0 .. limit .. mesh.Surfaces.Count - 1 do
                   let partNumber = first / limit
                   let suffix = if partNumber = 0 then "" else "__iw4_part" + partNumber.ToString(CultureInfo.InvariantCulture)
                   let count = min limit (mesh.Surfaces.Count - first)
                   let surfaces = [| for index in first .. first + count - 1 -> mesh.Surfaces[index] |]
                   let partBits = Array.zeroCreate<uint32> 6
                   for surface in surfaces do
                       for bit in 0 .. 5 do
                           partBits[bit] <- partBits[bit] ||| surface.PartBits[bit]

                   let partMesh = XModelSurfsAsset(
                       Name = mesh.Name + suffix,
                       NumSurfs = uint16 count,
                       Pad0A = mesh.Pad0A,
                       PartBits = partBits,
                       Surfaces = surfaces)
                   let partLod = XModelLodInfo(
                       Dist = lod.Dist,
                       NumSurfs = uint16 count,
                       SurfIndex = 0us,
                       PartBits = partBits,
                       ModelSurfs = partMesh)
                   let lods =
                       [| yield partLod
                          for inactive in model.Lods |> Seq.skip 1 do
                              yield XModelLodInfo(
                                  Dist = inactive.Dist,
                                  NumSurfs = inactive.NumSurfs,
                                  SerializedNumSurfs = inactive.SerializedNumSurfs,
                                  SurfIndex = inactive.SurfIndex,
                                  SerializedSurfIndex = inactive.SerializedSurfIndex,
                                  PartBits = inactive.PartBits,
                                  SerializedPartBits = inactive.SerializedPartBits) |]
                   let materials = [| for index in first .. first + count - 1 -> model.Materials[index] |]
                   yield XModelAsset(
                       Name = name + suffix,
                       NumBones = model.NumBones,
                       NumRootBones = model.NumRootBones,
                       NumSurfs = byte count,
                       LodRampType = model.LodRampType,
                       Scale = model.Scale,
                       NoScalePartBits = model.NoScalePartBits,
                       BoneNames = model.BoneNames,
                       ParentList = model.ParentList,
                       Quats = model.Quats,
                       Trans = model.Trans,
                       PartClassification = model.PartClassification,
                       BaseMat = model.BaseMat,
                       Materials = materials,
                       Lods = lods,
                       MaxLoadedLod = model.MaxLoadedLod,
                       NumLods = model.NumLods,
                       CollLod = model.CollLod,
                       Flags = model.Flags,
                       NumCollSurfs = model.NumCollSurfs,
                       Contents = model.Contents,
                       CollSurfs = model.CollSurfs,
                       BoneInfo = (model.BoneInfo |> Seq.toArray),
                       Radius = model.Radius,
                       Bounds = model.Bounds,
                       InvHighMipRadius = Array.zeroCreate<uint16> count,
                       MemUsage = model.MemUsage,
                       PhysPreset = model.PhysPreset,
                       PhysCollmap = model.PhysCollmap) |]

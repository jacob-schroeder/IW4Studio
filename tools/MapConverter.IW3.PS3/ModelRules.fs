namespace MapConverter.Game.IW3.PS3.Rules

open System
open System.Buffers.Binary
open System.Collections.Generic
open System.IO
open System.Globalization
open IW4.Game.Assets.GfxMap
open IW4.Game.Assets.XModel
open IW4.Game.Codecs.XModel

module internal ModelRules =
    let private splitStaticModel (nameFirstPart: bool) (model: XModelAsset) : XModelAsset array =
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
                   let suffix =
                       if partNumber = 0 && not nameFirstPart then ""
                       else "__iw4_part" + partNumber.ToString(CultureInfo.InvariantCulture)
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

    let SplitStaticModel (model: XModelAsset) = splitStaticModel false model

    /// Keep the complete source XModel available to nonstatic consumers.
    let SplitStaticModelForImport (model: XModelAsset) = splitStaticModel true model

    /// Reuse only byte-identical packed vertices within each rigid bone list.
    let CompactRigidSurface (source: XSurface) : XSurface =
        if isNull source then nullArg (nameof source)

        let stride = XSurfaceVertexCodec.StreamStride
        if source.Deformed || source.VertListCount = 0 ||
           source.VertListCount <> source.VertList.Count ||
           source.Verts0.Count <> int source.VertCount * stride ||
           source.Verts1.Count <> source.Verts0.Count ||
           source.TriIndices.Count <> int source.TriCount * 3 then
            source
        else
            let mutable valid = true
            let mutable vertexEnd = 0
            let mutable triangleEnd = 0
            for rigid in source.VertList do
                if isNull rigid then
                    valid <- false
                else
                    let vertexStart = vertexEnd
                    vertexEnd <- vertexEnd + int rigid.VertCount
                    if int rigid.TriOffset <> triangleEnd || vertexEnd > int source.VertCount ||
                       triangleEnd + int rigid.TriCount > int source.TriCount then
                        valid <- false
                    else
                        triangleEnd <- triangleEnd + int rigid.TriCount
                        for index in int rigid.TriOffset * 3 .. triangleEnd * 3 - 1 do
                            let vertex = int source.TriIndices[index]
                            if vertex < vertexStart || vertex >= vertexEnd then
                                valid <- false

            if not valid || vertexEnd <> int source.VertCount ||
               triangleEnd <> int source.TriCount then
                source
            else
                let verts0 = Array.zeroCreate<byte> source.Verts0.Count
                let verts1 = Array.zeroCreate<byte> source.Verts1.Count
                let remap = Array.zeroCreate<uint16> (int source.VertCount)
                let rigidLists = Array.zeroCreate<XRigidVertList> source.VertListCount
                let packed = Array.zeroCreate<byte> (stride * 2)
                let readWord offset =
                    BinaryPrimitives.ReadUInt64BigEndian(ReadOnlySpan<byte>(packed, offset, 8))
                let mutable nextVertex = 0
                let mutable sourceVertex = 0
                for listIndex in 0 .. rigidLists.Length - 1 do
                    let rigid = source.VertList[listIndex]
                    let firstVertex = nextVertex
                    let vertexByBytes = Dictionary<struct (uint64 * uint64 * uint64 * uint64), uint16>()
                    let lastSourceVertex = sourceVertex + int rigid.VertCount
                    while sourceVertex < lastSourceVertex do
                        let sourceByte = sourceVertex * stride
                        for offset in 0 .. stride - 1 do
                            packed[offset] <- source.Verts0[sourceByte + offset]
                            packed[stride + offset] <- source.Verts1[sourceByte + offset]
                        let key = struct (readWord 0, readWord 8, readWord 16, readWord 24)
                        let mutable matchIndex = 0us
                        if not (vertexByBytes.TryGetValue(key, &matchIndex)) then
                            matchIndex <- uint16 nextVertex
                            nextVertex <- nextVertex + 1
                            let targetByte = int matchIndex * stride
                            Array.Copy(packed, 0, verts0, targetByte, stride)
                            Array.Copy(packed, stride, verts1, targetByte, stride)
                            vertexByBytes.Add(key, matchIndex)
                        remap[sourceVertex] <- matchIndex
                        sourceVertex <- sourceVertex + 1
                    rigidLists[listIndex] <- XRigidVertList(
                        BoneOffset = rigid.BoneOffset,
                        VertCount = uint16 (nextVertex - firstVertex),
                        TriOffset = rigid.TriOffset,
                        TriCount = rigid.TriCount,
                        CollisionTreePointer = rigid.CollisionTreePointer,
                        CollisionTree = rigid.CollisionTree)

                if nextVertex = int source.VertCount then
                    source
                else
                    let indices = source.TriIndices |> Seq.map (fun index -> remap[int index]) |> Seq.toArray
                    let byteCount = nextVertex * stride
                    XSurface(
                        TileMode = source.TileMode,
                        DeformedRaw = source.DeformedRaw,
                        StreamFlags = source.StreamFlags,
                        Pad03 = source.Pad03,
                        VertCount = uint16 nextVertex,
                        TriCount = source.TriCount,
                        TriIndicesPointer = source.TriIndicesPointer,
                        TriIndices = indices,
                        VertexInfo = source.VertexInfo,
                        Verts0Pointer = source.Verts0Pointer,
                        Verts0 = Array.sub verts0 0 byteCount,
                        Vb0 = source.Vb0,
                        Verts1Pointer = source.Verts1Pointer,
                        Verts1 = Array.sub verts1 0 byteCount,
                        Vb1 = source.Vb1,
                        VertListCount = source.VertListCount,
                        VertListPointer = source.VertListPointer,
                        VertList = rigidLists,
                        IndexBuffer = source.IndexBuffer,
                        PartBits = source.PartBits)

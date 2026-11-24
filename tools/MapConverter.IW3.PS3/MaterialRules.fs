namespace MapConverter.Game.IW3.PS3.Rules

open System
open System.Buffers.Binary
open System.Collections.Generic
open System.IO
open IW4.Game.Assets.Material
open IW4.Game.Assets.Image
open IW4.Game.Assets.TechniqueSet

module internal MaterialRules =
    /// COD4 PS3 0xca368 uses the same 0x48-byte water root and three
    /// float32 arrays as IW4 PS3. Preserve them; PC complex-array rules do
    /// not apply to this source platform.
    let Water(root: ReadOnlySpan<byte>, h0x: ReadOnlyMemory<byte>,
              h0y: ReadOnlyMemory<byte>, wTerm: ReadOnlyMemory<byte>, image: GfxImageAsset) =
        if root.Length <> 0x48 then
            raise (InvalidDataException("IW3 PS3 water requires a 0x48-byte root."))
        let m = BinaryPrimitives.ReadInt32BigEndian(root.Slice 0x10)
        let n = BinaryPrimitives.ReadInt32BigEndian(root.Slice 0x14)
        let count64 = int64 m * int64 n
        if m <= 0 || n <= 0 || count64 > int64 Int32.MaxValue / 4L then
            raise (InvalidDataException("IW3 PS3 water has invalid spectrum dimensions."))
        let count = int count64
        let spectrum (bytes: ReadOnlyMemory<byte>) =
            if bytes.Length <> count * 4 then
                raise (InvalidDataException("IW3 PS3 water spectrum length differs from M*N float32 values."))
            Array.init count (fun i -> BinaryPrimitives.ReadSingleBigEndian(bytes.Span.Slice(i * 4)))
        MaterialWater(
            Writable = MaterialWaterWritable(BinaryPrimitives.ReadUInt32BigEndian root),
            M = m, N = n,
            Lx = BinaryPrimitives.ReadSingleBigEndian(root.Slice 0x18),
            Lz = BinaryPrimitives.ReadSingleBigEndian(root.Slice 0x1c),
            Gravity = BinaryPrimitives.ReadSingleBigEndian(root.Slice 0x20),
            WindVelocity = BinaryPrimitives.ReadSingleBigEndian(root.Slice 0x24),
            WindDirection = MaterialVec2(BinaryPrimitives.ReadSingleBigEndian(root.Slice 0x28),
                                         BinaryPrimitives.ReadSingleBigEndian(root.Slice 0x2c)),
            Amplitude = BinaryPrimitives.ReadSingleBigEndian(root.Slice 0x30),
            CodeConstant = MaterialVec4(BinaryPrimitives.ReadSingleBigEndian(root.Slice 0x34),
                                        BinaryPrimitives.ReadSingleBigEndian(root.Slice 0x38),
                                        BinaryPrimitives.ReadSingleBigEndian(root.Slice 0x3c),
                                        BinaryPrimitives.ReadSingleBigEndian(root.Slice 0x40)),
            H0X = spectrum h0x, H0Y = spectrum h0y, WTerm = spectrum wTerm, Image = image)

    /// COD4 material.gdf ordinals 13 and 15 are decal-middle-1 and -3.
    /// Native IW4 PS3 lit decals use keys 10 and 12 in the litTrans region.
    /// COD4 PS3 sort 31 also maps to litTrans: the native w/ and wc/
    /// ch_rubble_clump01_decal counterparts use DecalTop3 (16).
    let LitDecalSort(sourceSort: byte, sourceRegion: GfxCameraRegionType,
                     entries: IReadOnlyList<MaterialStateBitsEntry>) : Nullable<MaterialSortKey> =
        if (sourceSort = 13uy || sourceSort = 15uy) &&
           sourceRegion = GfxCameraRegionType.LitOpaque &&
           entries[int MaterialTechniqueType.Lit].StateBitsIndex <> Byte.MaxValue &&
           entries[int MaterialTechniqueType.Emissive].StateBitsIndex = Byte.MaxValue then
            Nullable (if sourceSort = 13uy then MaterialSortKey.DecalMiddle1
                      else MaterialSortKey.DecalMiddle3)
        elif sourceSort = 31uy && sourceRegion = GfxCameraRegionType.LitTrans &&
             entries[int MaterialTechniqueType.Lit].StateBitsIndex <> Byte.MaxValue &&
             entries[int MaterialTechniqueType.Emissive].StateBitsIndex = Byte.MaxValue then
            Nullable MaterialSortKey.DecalTop3
        else Nullable()

    /// Native PS3 IW4 materials/cloud_dust_mote.json has emissive
    /// particle_cloud at sort 34; its _sort sibling uses 50, so this route is
    /// deliberately limited to the matching COD4 material and source key.
    let ParticleCloudSort(sourceSort: byte, sourceRegion: GfxCameraRegionType,
                          setName: string, sourceName: string) : Nullable<MaterialSortKey> =
        match sourceSort, sourceRegion, setName, sourceName with
        | 4uy, GfxCameraRegionType.Emissive, "particle_cloud", "cloud_dust_mote" ->
            Nullable(LanguagePrimitives.EnumOfValue<byte, MaterialSortKey> 34uy)
        | _ -> Nullable()

    /// COD4 PS3 w_tools uses sort 43/None; native IW4 PS3 w/aa_default
    /// uses sort 47/None with the same unlit, local-light, and wireframe slots.
    let ToolMaterialSort(sourceSort: byte, sourceRegion: GfxCameraRegionType,
                         setName: string, entries: IReadOnlyList<MaterialStateBitsEntry>)
                         : Nullable<MaterialSortKey> =
        let expectedSlot index =
            index = int MaterialTechniqueType.Unlit ||
            index = int MaterialTechniqueType.LightSpot ||
            index = int MaterialTechniqueType.LightOmni ||
            index = int MaterialTechniqueType.WireframeSolid
        if sourceSort = 43uy && sourceRegion = GfxCameraRegionType.None &&
           setName = "w_tools" && entries.Count = int MaterialTechniqueType.Count &&
           (entries |> Seq.mapi (fun index entry ->
               (entry.StateBitsIndex <> Byte.MaxValue) = expectedSlot index) |> Seq.forall id) then
            Nullable MaterialSortKey.AdditiveBlend
        else Nullable()

    /// Exact native IW4 counterparts use the material-specific shadow route
    /// with a GE128 alpha-tested caster. Preserve each source material's low flags.
    let ShadowFlags(sourceName: string, flags: MaterialGameFlags,
                    entries: IReadOnlyList<MaterialStateBitsEntry>,
                    states: IReadOnlyList<GfxStateBits>) =
        let expected =
            match sourceName with
            | "m/mtl_lightdrygrass" -> Some 0x44uy
            | "m/me_fence_chainlink" -> Some 0x54uy
            | "m/mtl_bedframemetal"
            | "m/ch_tile_floor05b"
            | "m/mtl_ch_streetsigns"
            | "m/mtl_barrel_fire"
            | "m/mtl_bench_posts"
            | "m/mtl_clipboards"
            | "m/mtl_lamp_ornate_off"
            | "m/mtl_razor_wire"
            | "m/mtl_plastic_crates" -> Some 0x50uy
            | "m/mtl_hedgewall_all"
            | "m/mtl_hedgewall_lodrenders" -> Some 0x40uy
            | "m/mtl_tree_shadow_caster"
            | "mc/mtl_desertshrubs" -> Some 0x44uy
            | _ -> None

        match expected with
        | None -> flags
        | Some oldFlags ->
            let actual = byte flags
            if actual <> oldFlags then
                raise (InvalidDataException($"IW3 PS3 material '{sourceName}' shadow flags changed: expected 0x{oldFlags:X2}, got 0x{actual:X2}."))
            if isNull entries || entries.Count <= 2 then
                raise (InvalidDataException($"IW3 PS3 material '{sourceName}' has no shadow state entry at slot 2."))
            let stateIndex = int entries[2].StateBitsIndex
            if isNull states || stateIndex >= states.Count then
                raise (InvalidDataException($"IW3 PS3 material '{sourceName}' shadow slot 2 refers to missing state {stateIndex}."))
            let state = states[stateIndex]
            if isNull state || isNull state.LoadBits || state.LoadBits.Count < 1 then
                raise (InvalidDataException($"IW3 PS3 material '{sourceName}' shadow state {stateIndex} has no state bits."))
            let alphaMask = uint32 GfxStateBits0Flags.AlphaTestDisabled ||| GfxStateBitsEncoding.AlphaTestMask
            let ge128 = uint32 GfxAlphaTest.GreaterThanOrEqualTo128 <<< GfxStateBitsEncoding.AlphaTestShift
            if (state.LoadBits[0] &&& alphaMask) <> ge128 then
                raise (InvalidDataException($"IW3 PS3 material '{sourceName}' shadow state {stateIndex} is not GE128 alpha-tested."))
            LanguagePrimitives.EnumOfValue<byte, MaterialGameFlags> ((actual &&& 0x3fuy) ||| 0x80uy)

    let SourceCameraRegion(source: byte, name: string) =
        match source with
        | 0uy -> GfxCameraRegionType.LitOpaque
        | 1uy -> GfxCameraRegionType.LitTrans
        | 2uy -> GfxCameraRegionType.Emissive
        | 3uy -> GfxCameraRegionType.None
        | value -> raise (InvalidDataException($"IW3 PS3 material '{name}' has unsupported source camera region {value}."))

    /// Replace a technique set, its slot-to-state entries, and its state table together.
    let WithTechniqueSetStateEntriesAndStates(material: MaterialAsset, set: MaterialTechniqueSetAsset,
                                              entries: IReadOnlyList<MaterialStateBitsEntry>,
                                              states: IReadOnlyList<GfxStateBits>) =
        if isNull material then nullArg (nameof material)
        if isNull set then nullArg (nameof set)
        if isNull entries then nullArg (nameof entries)
        if isNull states then nullArg (nameof states)
        if entries.Count <> int MaterialTechniqueType.Count then
            invalidArg (nameof entries) "Material requires 37 state-bit entries."
        if states.Count > int Byte.MaxValue || states.Count < int material.StateBitsCount ||
           material.StateBits.Count <> int material.StateBitsCount then
            invalidArg (nameof states) "Material state-table size is invalid."
        MaterialAsset(
            Info = material.Info,
            StateBitsEntries = entries,
            TextureCount = material.TextureCount,
            ConstantCount = material.ConstantCount,
            StateBitsCount = byte states.Count,
            StateFlags = material.StateFlags,
            CameraRegion = material.CameraRegion,
            LayerCount = material.LayerCount,
            Pad43 = material.Pad43,
            InlineTechniqueSlotStateBits = material.InlineTechniqueSlotStateBits,
            Pad8E = material.Pad8E,
            RuntimeTechniqueSlotStateBitsPointer = material.RuntimeTechniqueSlotStateBitsPointer,
            RuntimeTechniqueSlotStateBits = material.RuntimeTechniqueSlotStateBits,
            TechniqueSetPointer = Unchecked.defaultof<_>,
            TechniqueSet = set,
            TextureTablePointer = material.TextureTablePointer,
            Textures = material.Textures,
            ConstantTablePointer = material.ConstantTablePointer,
            Constants = material.Constants,
            StateBitsPointer = (if Object.ReferenceEquals(states, material.StateBits) then material.StateBitsPointer else Unchecked.defaultof<_>),
            StateBits = states,
            SubMaterialNamesPointer = material.SubMaterialNamesPointer,
            SubMaterialNames = material.SubMaterialNames)

    /// Replace the technique set and its matching state table as one material.
    let WithTechniqueSetAndStates(material: MaterialAsset, set: MaterialTechniqueSetAsset,
                                  states: IReadOnlyList<GfxStateBits>) =
        if isNull material then nullArg (nameof material)
        if isNull states then nullArg (nameof states)
        if states.Count <> material.StateBits.Count || states.Count <> int material.StateBitsCount then
            invalidArg (nameof states) "Material state-table size changed."
        WithTechniqueSetStateEntriesAndStates(material, set, material.StateBitsEntries, states)

    /// Retain every other serialized field and owned child table.
    let WithTechniqueSet(material: MaterialAsset, set: MaterialTechniqueSetAsset) =
        if isNull material then nullArg (nameof material)
        WithTechniqueSetAndStates(material, set, material.StateBits)

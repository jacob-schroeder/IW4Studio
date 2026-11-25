namespace MapConverter.Game.IW3.PS3.Rules

open System
open System.Buffers.Binary
open System.IO
open System.Linq
open System.Security.Cryptography
open System.Text
open IW4.Game.Assets.Material
open IW4.Game.Assets.TechniqueSet
open IW4.Game.Math

module internal TechniqueRules =
    let private invalid message = raise (InvalidDataException message)
    let private hash (bytes: byte[]) = Convert.ToHexStringLower(SHA256.HashData bytes)

    let ExportedSourceGraphEqual
        (leftName: string, rightName: string, leftFiles: string array, rightFiles: string array,
         contentsEqual: Func<string, bool>) : bool =
        let isTechniqueFile (path: string) =
            path.EndsWith(".techset", StringComparison.Ordinal) ||
            path.EndsWith(".tech", StringComparison.Ordinal) ||
            path.EndsWith(".cso", StringComparison.Ordinal)
        let graphPaths files =
            files |> Array.filter isTechniqueFile
                  |> Array.sortWith (fun left right -> StringComparer.Ordinal.Compare(left, right))
        let left = graphPaths leftFiles
        let right = graphPaths rightFiles
        let setPath (name: string) = "techsets/" + name.Replace('\\', '/') + ".techset"
        if left.Length = 0 || not (Array.contains (setPath leftName) left) then
            invalid $"Extracted technique set '{leftName}' has no matching .techset file."
        if right.Length = 0 || not (Array.contains (setPath rightName) right) then
            invalid $"Extracted technique set '{rightName}' has no matching .techset file."
        left = right && Array.forall (fun path -> contentsEqual.Invoke(path)) left

    // IW4's 37 console target slots map to the 26 retained IW3 PS3 slots.
    let private sourceSlots =
        [| 0; 1; 2; -1; 4; 5; 5; 6; 6; 7; 7; 8; 8; 9; 9; 10; 10; 11; 11
           12; 12; 13; 13; 7; 7; 8; 8; 21; 22; 23; 24; 25; 26; 27; 28; 29; 32 |]
        |> Array.map (fun slot -> if slot < 0 || slot >= 14 && slot <= 20 then -1 elif slot >= 21 then slot - 7 else slot)

    let SourceSlotIndexForTarget(targetIndex: int) : Nullable<int> =
        if targetIndex < 0 || targetIndex >= sourceSlots.Length then
            raise (ArgumentOutOfRangeException(nameof targetIndex))
        let slot = sourceSlots[targetIndex]
        if slot < 0 then Nullable() else Nullable slot

    let CodeSamplerAlias(sourceIndex: int, shaderName: string, parameterName: string) : Nullable<MaterialTextureSource> =
        // COD4's authored distortion_zfeather technique binds this generic Cg
        // name to resolvedPostSun. The PS3 argument stores IW3 code-image 10;
        // IW4 uses ResolvedPostSun (9), not its code-image 10 (ResolvedScene).
        match sourceIndex, shaderName, parameterName with
        | 10, "distortion_zfeather.hlsl", "colorMapSampler" -> Nullable MaterialTextureSource.ResolvedPostSun
        // Pipeline's particle_cloud_outdoor source pass binds IW3 code-image
        // 17 as outdoorMapSampler. IW4's outdoor code-image is slot 14, backed
        // by the converted world's OutdoorImage.
        | 17, "particle_cloud_outdoor.hlsl", "outdoorMapSampler" -> Nullable MaterialTextureSource.Outdoor
        | _ -> Nullable()

    let private copyPass (source: MaterialPassAsset) (pixel: MaterialShaderAsset) (args: MaterialShaderArgumentAsset array) =
        MaterialPassAsset(
            Offset = source.Offset,
            VertexDeclPointer = source.VertexDeclPointer,
            VertexShaderPointer = source.VertexShaderPointer,
            PixelShaderPointer = Unchecked.defaultof<_>,
            PerPrimArgCount = source.PerPrimArgCount,
            PerObjArgCount = source.PerObjArgCount,
            StableArgCount = source.StableArgCount,
            CustomSamplerFlags = source.CustomSamplerFlags,
            PrecompiledVertexShader = source.PrecompiledVertexShader,
            ArgsPointer = Unchecked.defaultof<_>,
            VertexDeclaration = source.VertexDeclaration,
            VertexShader = source.VertexShader,
            PixelShader = pixel,
            Args = Array.AsReadOnly args)

    let private copyTechnique (source: MaterialTechniqueAsset) (passes: MaterialPassAsset array) =
        MaterialTechniqueAsset(
            Offset = source.Offset, NamePointer = source.NamePointer, Name = source.Name,
            Flags = source.Flags, PassCount = source.PassCount, Passes = Array.AsReadOnly passes)

    let private copySet (source: MaterialTechniqueSetAsset) (name: string) (slots: MaterialTechniqueSlot array) =
        MaterialTechniqueSetAsset(
            NamePointer = Unchecked.defaultof<_>, Name = name,
            WorldVertexFormat = source.WorldVertexFormat, TechniqueSlots = Array.AsReadOnly slots)

    let private requireSet (prefix: string) (material: MaterialAsset) (materialName: string) (setName: string) =
        if isNull material || isNull material.Info || material.Info.Name <> prefix + materialName then
            invalid $"Expected {materialName} material."
        let set = material.TechniqueSet
        if isNull set || set.Name <> prefix + setName || set.TechniqueSlots.Count <> int MaterialTechniqueType.Count then
            invalid $"Expected {setName} technique set."
        set

    let GrassShadow (prefix: string, material: MaterialAsset, nativePixel: byte[]) : MaterialTechniqueSetAsset =
        let set = requireSet prefix material "m/mtl_lightdrygrass" "m_ambient_t0c0"
        if material.StateBitsEntries.Count <= 2 || material.StateBitsEntries[2].StateBitsIndex <> 0uy ||
           material.StateBits.Count = 0 ||
           not (material.StateBits[0].LoadBits.SequenceEqual([| 1208338u; 61u |])) then
            invalid "Expected alpha-test GE128 grass depth state."
        if isNull nativePixel || nativePixel.Length <> 304 ||
           hash nativePixel <> "5c23446231406802a91d63b6e1d405d015bfdd249caff7d724a3253971d74bdc" then
            invalid "Pinned native grass shadow pixel program changed."
        let slots = set.TechniqueSlots.ToArray()
        let technique = slots[2].Technique
        if isNull technique || technique.Name <> prefix + "build_shadowmap_model_test_nc" || technique.Passes.Count <> 1 then
            invalid "Grass caster technique changed."
        let pass = technique.Passes[0]
        let pixel = pass.PixelShader
        let vertex = pass.VertexShader
        if isNull pixel || pixel.Kind <> MaterialShaderKind.Pixel || isNull pixel.Data ||
           hash pixel.Data <> "7374f3332aa9209de36533cc116b027240db8abc828acfad5053131d2878d013" ||
           pixel.LoadDefTail.Length <> 12 || (pixel.LoadDefTail |> Array.exists ((<>) 0uy)) then
            invalid "Source grass shadow pixel program changed."
        if isNull vertex || isNull vertex.Data ||
           hash vertex.Data <> "804d2f07964b0fbcbeb9d3e0225accc2c70080ef434e6df1a35d38a6ed07800d" then
            invalid "Grass caster vertex program changed."
        let diffs = [| for i in 0 .. nativePixel.Length - 1 do if nativePixel[i] <> pixel.Data[i] then yield i |]
        if diffs <> [| 0x113; 0x121 |] then invalid "Unexpected native grass pixel differences."
        let replacement =
            MaterialShaderAsset(
                Kind = pixel.Kind, NamePointer = Unchecked.defaultof<_>,
                Name = prefix + "vertcol_simple_atest_nc_native_shadow.hlsl",
                DataPointer = Unchecked.defaultof<_>, DataSize = uint32 nativePixel.Length,
                LoadDefTail = Array.copy pixel.LoadDefTail, Data = Array.copy nativePixel)
        let changedPass = copyPass pass replacement (pass.Args.ToArray())
        slots[2] <- MaterialTechniqueSlot(slots[2].Type, Unchecked.defaultof<_>,
                                          copyTechnique technique [| changedPass |])
        copySet set (set.Name + "__iw4_grass_shadow") slots

    let private shaderName slot =
        if slot = 9 || slot = 10 || slot = 23 || slot = 24 then "lp_amb_t0c0_sm3.hlsl"
        elif slot = 11 || slot = 12 || slot = 13 || slot = 14 || slot = 25 || slot = 26 then "lp_amb_sun_t0c0_sm3.hlsl"
        elif slot >= 15 && slot <= 18 then "lp_amb_spot_t0c0_sm3.hlsl"
        else "lp_amb_omni_t0c0_sm3.hlsl"

    let private verifyCanopyProgram slot (shader: MaterialShaderAsset) =
        let data = shader.Data
        if isNull data || shader.DataSize <> uint32 data.Length ||
           hash data <> "f7a6548e810a115c9900c16d4d980de7103dd1893b9e4e11b152048260059544" then
            invalid $"Slot {slot} original canopy pixel program changed."
        let span = data.AsSpan()
        let count = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(12))
        let offset = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(16))
        if count <= 4u || uint64 offset + uint64 count * 48UL > uint64 data.Length then
            invalid "Invalid canopy Cg parameter table."
        let nameOffset = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(int offset + 4 * 48 + 16))
        let name = Encoding.ASCII.GetBytes("sunDiffuse\000")
        if uint64 nameOffset + uint64 name.Length > uint64 data.Length ||
           not (span.Slice(int nameOffset).StartsWith(name.AsSpan())) then
            invalid "Canopy pixel parameter 4 is not sunDiffuse."

    let CanopySun (prefix: string, material: MaterialAsset, sun: Vec3) : MaterialTechniqueSetAsset =
        let set = requireSet prefix material "mc/mtl_red_pine_canopy" "mc_ambient_t0c0"
        if not (Single.IsFinite sun.X && Single.IsFinite sun.Y && Single.IsFinite sun.Z) then
            invalid "Nonfinite sun RGB."
        let literal = MaterialShaderLiteralConstant(sun.X, sun.Y, sun.Z, 1.0f)
        let slots = set.TechniqueSlots.ToArray()
        for slotIndex in 9 .. 26 do
            let slot = slots[slotIndex]
            let technique = slot.Technique
            if isNull technique then invalid $"Slot {slotIndex} has no technique."
            let passes = technique.Passes.ToArray()
            let expectedName = prefix + shaderName slotIndex
            let matches = [| for i in 0 .. passes.Length - 1 do
                                 if not (isNull passes[i].PixelShader) && passes[i].PixelShader.Name = expectedName then yield i |]
            if matches.Length <> 1 then invalid $"Slot {slotIndex} expected one {expectedName} pass."
            let passIndex = matches[0]
            let pass = passes[passIndex]
            if pass.Args.Count <> int pass.PerPrimArgCount + int pass.PerObjArgCount + int pass.StableArgCount then
                invalid $"Slot {slotIndex} argument counts disagree."
            let args = pass.Args.ToArray()
            let matchesArgs = [| for i in 0 .. args.Length - 1 do
                                    if args[i].Type = MaterialShaderArgumentType.CodePixelConst &&
                                       args[i].Dest = 4us && args[i].ArgumentRaw = 65537 then yield i |]
            if matchesArgs.Length <> 1 then invalid $"Slot {slotIndex} expected one LightDiffuse binding."
            let argIndex = matchesArgs[0]
            let stableStart = int pass.PerPrimArgCount + int pass.PerObjArgCount
            let expectedTypes =
                [| MaterialShaderArgumentType.MaterialPixelSampler; MaterialShaderArgumentType.CodeVertexConst
                   MaterialShaderArgumentType.CodePixelSampler; MaterialShaderArgumentType.CodePixelConst
                   MaterialShaderArgumentType.CodePixelConst |]
            if argIndex < stableStart ||
               (args[stableStart..] |> Array.map (fun arg -> arg.Type)) <> expectedTypes then
                invalid $"Slot {slotIndex} stable argument layout changed."
            verifyCanopyProgram slotIndex pass.PixelShader
            let original = args[argIndex]
            let changedArgs =
                Array.append
                    (args |> Array.mapi (fun i arg -> i, arg) |> Array.choose (fun (i, arg) -> if i = argIndex then None else Some arg))
                    [| MaterialShaderArgumentAsset(original.Offset, MaterialShaderArgumentType.LiteralPixelConst,
                                                   original.Dest, 0, Nullable literal) |]
            passes[passIndex] <- copyPass pass pass.PixelShader changedArgs
            slots[slotIndex] <- MaterialTechniqueSlot(slot.Type, Unchecked.defaultof<_>,
                                                      copyTechnique technique passes)
        copySet set (set.Name + "__iw4_canopy_sun") slots

    let private localLightSlots = [| 27; 28; 29 |]

    let private localLightFamily (prefix: string) (material: MaterialAsset) =
        if isNull material || isNull material.TechniqueSet then None
        else
            match material.TechniqueSet.Name with
            | name when name = prefix + "w_l_sm_r0c0n0s0" ->
                Some ("iw4_local_light_r0c0n0", 0x18128928u)
            | name when name = prefix + "wc_l_sm_r0c0n0s0" ->
                Some ("iw4_local_light_r0c0n0_vertex_color", 0x18128928u)
            | name when name = prefix + "l_sm_r0c0n0s0_b1c1n1" ||
                        name = prefix + "l_sm_r0c0n0s0_b1c1n1s1" ->
                Some ("iw4_local_light_r0c0n0b1c1n1", 0x18128922u)
            | _ -> None

    let NativeLocalLightDonor(prefix: string, material: MaterialAsset) : string =
        if isNull material || material.CameraRegion <> GfxCameraRegionType.LitOpaque then null
        else
            match localLightFamily prefix material with
            | Some (name, _) -> name
            | None -> null

    let NativeLocalLight
        (prefix: string, material: MaterialAsset, nativeSet: MaterialTechniqueSetAsset,
         nativeShaders: System.Collections.Generic.IReadOnlyList<MaterialShaderAsset>)
        : MaterialTechniqueSetAsset * GfxStateBits array * MaterialShaderAsset array =
        let materialName =
            if isNull material || isNull material.Info then "<unknown>" else material.Info.Name
        let fail message = invalid $"Material '{materialName}': {message}"
        let donorName, sourceStateWord =
            match localLightFamily prefix material with
            | Some family -> family
            | None -> fail "Unsupported local-light technique family."
        let sourceSet = material.TechniqueSet
        if material.CameraRegion <> GfxCameraRegionType.LitOpaque then
            fail "Local-light material is not LitOpaque."
        if isNull nativeSet || nativeSet.Name <> donorName ||
           sourceSet.TechniqueSlots.Count <> int MaterialTechniqueType.Count ||
           nativeSet.TechniqueSlots.Count <> int MaterialTechniqueType.Count ||
           sourceSet.WorldVertexFormat <> nativeSet.WorldVertexFormat then
            fail "Native local-light technique set or world vertex format differs."
        if isNull nativeShaders || material.StateBitsEntries.Count <> int MaterialTechniqueType.Count ||
           material.StateBits.Count <> int material.StateBitsCount then
            fail "Local-light shader or state table is incomplete."

        let stateIndex = int material.StateBitsEntries[27].StateBitsIndex
        if stateIndex >= material.StateBits.Count ||
           (localLightSlots |> Array.exists (fun i -> int material.StateBitsEntries[i].StateBitsIndex <> stateIndex)) ||
           ([| for i in 0 .. material.StateBitsEntries.Count - 1 do
                   if int material.StateBitsEntries[i].StateBitsIndex = stateIndex then yield i |] <> localLightSlots) then
            fail "Local-light state is missing, differs, or is shared by another slot."
        if isNull material.StateBits[stateIndex] then
            fail "Source local-light state is missing."
        let sourceWords = material.StateBits[stateIndex].LoadBits
        if isNull sourceWords || sourceWords.Count <> 2 ||
           sourceWords[0] <> sourceStateWord || sourceWords[1] <> 0xe0040048u then
            fail "Source local-light state words differ."

        let nativeShaderDefinitions = nativeShaders.ToArray()
        let shaderCache = System.Collections.Generic.Dictionary<MaterialShaderKind * string, MaterialShaderAsset>()
        let resolveShader (kind: MaterialShaderKind) (placeholder: MaterialShaderAsset) =
            if isNull placeholder || placeholder.Kind <> kind || isNull placeholder.Name then
                fail "Native local-light shader reference is incomplete."
            let key = kind, placeholder.Name
            match shaderCache.TryGetValue key with
            | true, shader -> shader
            | _ ->
                let matches =
                    nativeShaderDefinitions
                    |> Array.filter (fun shader -> not (isNull shader) && shader.Kind = kind && shader.Name = placeholder.Name)
                if matches.Length <> 1 || isNull matches[0].Data ||
                   matches[0].DataSize <> uint32 matches[0].Data.Length ||
                   isNull matches[0].LoadDefTail ||
                   matches[0].LoadDefTail.Length <> MaterialShaderAsset.GetLoadDefTailSize kind then
                    fail $"Native local-light shader '{placeholder.Name}' is missing or incomplete."
                let original = matches[0]
                let shader =
                    MaterialShaderAsset(
                        Kind = kind, NamePointer = Unchecked.defaultof<_>,
                        Name = prefix + "iw4_local_light/" + original.Name,
                        DataPointer = Unchecked.defaultof<_>, DataSize = original.DataSize,
                        LoadDefTail = Array.copy original.LoadDefTail, Data = Array.copy original.Data)
                shaderCache.Add(key, shader)
                shader

        let bindings (pass: MaterialPassAsset) =
            pass.Args
            |> Seq.map (fun arg ->
                let literal =
                    arg.Type = MaterialShaderArgumentType.LiteralVertexConst ||
                    arg.Type = MaterialShaderArgumentType.LiteralPixelConst
                if literal <> arg.LiteralConstant.HasValue then
                    fail "Local-light shader argument literal is incomplete."
                let raw = if literal then 0 else arg.ArgumentRaw
                arg.Type, arg.Dest, raw, arg.LiteralConstant)
            |> Seq.toArray
        let slots = sourceSet.TechniqueSlots.ToArray()
        for slotIndex in localLightSlots do
            let previous = slots[slotIndex].Technique
            let donor = nativeSet.TechniqueSlots[slotIndex].Technique
            if isNull previous || isNull donor || isNull donor.Name ||
               previous.PassCount <> 1us || donor.PassCount <> 1us ||
               previous.Passes.Count <> 1 || donor.Passes.Count <> 1 || previous.Flags <> donor.Flags then
                fail $"Slot {slotIndex} pass count or technique flags differ."
            let oldPass = previous.Passes[0]
            let nativePass = donor.Passes[0]
            let groups (pass: MaterialPassAsset) =
                pass.PerPrimArgCount, pass.PerObjArgCount, pass.StableArgCount,
                pass.CustomSamplerFlags, pass.PrecompiledVertexShader
            let expectedArgs (pass: MaterialPassAsset) =
                int pass.PerPrimArgCount + int pass.PerObjArgCount + int pass.StableArgCount
            if groups oldPass <> groups nativePass ||
               oldPass.Args.Count <> expectedArgs oldPass || nativePass.Args.Count <> expectedArgs nativePass ||
               bindings oldPass <> bindings nativePass then
                fail $"Slot {slotIndex} native shader arguments differ."
            let oldDecl = oldPass.VertexDeclaration
            let nativeDecl = nativePass.VertexDeclaration
            if isNull oldDecl || isNull nativeDecl ||
               oldDecl.Routing.Count <> MaterialVertexDeclarationAsset.RoutingCount ||
               nativeDecl.Routing.Count <> MaterialVertexDeclarationAsset.RoutingCount ||
               oldDecl.StreamCount <> nativeDecl.StreamCount ||
               oldDecl.HasOptionalSourceRaw <> nativeDecl.HasOptionalSourceRaw ||
               not (oldDecl.Routing.SequenceEqual(nativeDecl.Routing)) then
                fail $"Slot {slotIndex} native vertex declaration differs."
            let declaration =
                MaterialVertexDeclarationAsset(
                    StreamCount = nativeDecl.StreamCount,
                    HasOptionalSourceRaw = nativeDecl.HasOptionalSourceRaw,
                    Routing = Array.AsReadOnly(nativeDecl.Routing.ToArray()))
            let args =
                nativePass.Args
                |> Seq.map (fun arg ->
                    let raw = if arg.LiteralConstant.HasValue then 0 else arg.ArgumentRaw
                    MaterialShaderArgumentAsset(arg.Offset, arg.Type, arg.Dest, raw, arg.LiteralConstant))
                |> Seq.toArray
            let pass =
                MaterialPassAsset(
                    Offset = nativePass.Offset,
                    VertexDeclPointer = Unchecked.defaultof<_>,
                    VertexShaderPointer = Unchecked.defaultof<_>,
                    PixelShaderPointer = Unchecked.defaultof<_>,
                    PerPrimArgCount = nativePass.PerPrimArgCount,
                    PerObjArgCount = nativePass.PerObjArgCount,
                    StableArgCount = nativePass.StableArgCount,
                    CustomSamplerFlags = nativePass.CustomSamplerFlags,
                    PrecompiledVertexShader = nativePass.PrecompiledVertexShader,
                    ArgsPointer = Unchecked.defaultof<_>,
                    VertexDeclaration = declaration,
                    VertexShader = resolveShader MaterialShaderKind.Vertex nativePass.VertexShader,
                    PixelShader = resolveShader MaterialShaderKind.Pixel nativePass.PixelShader,
                    Args = Array.AsReadOnly args)
            let technique =
                MaterialTechniqueAsset(
                    Offset = donor.Offset, NamePointer = Unchecked.defaultof<_>,
                    Name = prefix + "iw4_local_light/" + donor.Name,
                    Flags = donor.Flags, PassCount = donor.PassCount,
                    Passes = Array.AsReadOnly [| pass |])
            slots[slotIndex] <- MaterialTechniqueSlot(slots[slotIndex].Type, Unchecked.defaultof<_>, technique)

        let states = material.StateBits.ToArray()
        states[stateIndex] <-
            GfxStateBits(
                LoadBitsPointer = Unchecked.defaultof<_>,
                LoadBits = Array.AsReadOnly [| 0x592a892au; 0xe0040048u |],
                CommandWordCount = 0u)
        copySet sourceSet (sourceSet.Name + "__iw4_local_light") slots,
        states,
        (shaderCache.Values |> Seq.toArray)

    let private sunShadowFamilies =
        [| "w_l_sm_r0c0n0s0"; "l_sm_r0c0n0s0_b1c1n1";
           "l_sm_r0c0n0s0_b1c1n1s1"; "l_sm_r0c0n0s0_b1c1n1s1_b2c2n2s2";
           "m_l_sm_r0c0n0s0"; "mc_l_sm_r0c0n0s0"; "mc_l_sm_t0c0n0s0";
           "wc_l_sm_r0c0n0s0" |]

    let private sunShadowFamily (prefix: string) (material: MaterialAsset) =
        if isNull material || isNull material.TechniqueSet then None
        else
            sunShadowFamilies
            |> Array.tryFindIndex (fun family ->
                material.TechniqueSet.Name = prefix + family ||
                material.TechniqueSet.Name = prefix + family + "__iw4_local_light")

    let NativeSunShadowDonor(prefix: string, material: MaterialAsset) : string =
        if isNull material || material.CameraRegion <> GfxCameraRegionType.LitOpaque then null
        else
            match sunShadowFamily prefix material with
            | Some index -> "iw4_sun_shadow_" + sunShadowFamilies[index]
            | None -> null

    let NativeSunShadow
        (prefix: string, material: MaterialAsset, nativeSet: MaterialTechniqueSetAsset,
         nativeShaders: System.Collections.Generic.IReadOnlyList<MaterialShaderAsset>)
        : MaterialTechniqueSetAsset * MaterialStateBitsEntry array * GfxStateBits array * MaterialShaderAsset array =
        let materialName =
            if isNull material || isNull material.Info then "<unknown>" else material.Info.Name
        let fail message = invalid $"Material '{materialName}': {message}"
        let familyIndex =
            match sunShadowFamily prefix material with
            | Some index when material.CameraRegion = GfxCameraRegionType.LitOpaque -> index
            | _ -> fail "Unsupported sun-shadow receiver family."
        let family = sunShadowFamilies[familyIndex]
        let modelReceiver = familyIndex >= 4 && familyIndex <= 6
        // wc is vertex-colored world geometry: retain its color stream and lightmap route.
        let vertexColor = family = "mc_l_sm_r0c0n0s0" || family = "mc_l_sm_t0c0n0s0" ||
                          family = "wc_l_sm_r0c0n0s0"
        let alphaTested = family = "mc_l_sm_t0c0n0s0"
        // The native mc alpha-tested donor aliases 13/14; its standalone dfog shader is not selected.
        let hasDistanceFog = not alphaTested
        let colorSuffix = if vertexColor then "" else "_nc"
        let stems =
            [| "r0c0n0s0"; "r0c0n0s0b1c1n1"; "r0c0n0s0b1c1n1s1";
               "r0c0n0s0b1c1n1s1b2c2n2s2"; "r0c0n0s0"; "r0c0n0s0"; "t0c0n0s0"; "r0c0n0s0" |]
        let stem = stems[familyIndex]
        let stableCounts = [| 12uy; 16uy; 18uy; 23uy; 14uy; 14uy; 14uy; 12uy |]
        let vertexStem =
            match familyIndex with
            | 0 | 4 | 5 | 6 | 7 -> "tc0n0"
            | 3 -> "tc0n0_tc1n1_tc2n2"
            | _ -> "tc0n0_tc1n1"
        let sourceSet = material.TechniqueSet
        if isNull nativeSet || nativeSet.Name <> "iw4_sun_shadow_" + family ||
           sourceSet.TechniqueSlots.Count <> int MaterialTechniqueType.Count ||
           nativeSet.TechniqueSlots.Count <> int MaterialTechniqueType.Count ||
           sourceSet.WorldVertexFormat <> nativeSet.WorldVertexFormat ||
           isNull nativeShaders || material.StateBitsEntries.Count <> int MaterialTechniqueType.Count ||
           material.StateBits.Count <> int material.StateBitsCount then
            fail "Native sun-shadow set, shaders, or material state table is incomplete."

        let entries = material.StateBitsEntries.ToArray()
        let sourceStateIndex = int entries[13].StateBitsIndex
        if sourceStateIndex >= material.StateBits.Count ||
           ([| 11; 12; 14; 25; 26 |] |> Array.exists (fun slot ->
               entries[slot].StateBitsIndex <> entries[13].StateBitsIndex)) then
            fail "Sun and sun-shadow slots do not share a valid source state."
        let sourceState = material.StateBits[sourceStateIndex]
        if isNull sourceState || isNull sourceState.LoadBits || sourceState.LoadBits.Count <> 2 then
            fail "Source sun-shadow state is incomplete."
        let sourceColorState = sourceState.LoadBits[0]
        let expectedColorState = if alphaTested then 0x1812b012u else 0x18128812u
        // Culling is authored per material. Preserve two-sided model surfaces while changing
        // only the receiver's output encoding; do not force the donor's back-face culling.
        let twoSidedModelState = (expectedColorState &&& ~~~0x0000c000u) ||| 0x00004000u
        if sourceState.LoadBits[1] <> 0x0000000du ||
           (sourceColorState <> expectedColorState &&
            not (modelReceiver && sourceColorState = twoSidedModelState)) then
            fail "Source sun-shadow state differs from the supported receiver state."
        if material.StateBits.Count = int Byte.MaxValue then
            fail "No state-table row remains for the native sun-shadow state."

        let nativeShaderDefinitions = nativeShaders.ToArray()
        let shaderCache = System.Collections.Generic.Dictionary<MaterialShaderKind * string, MaterialShaderAsset>()
        let resolveShader (kind: MaterialShaderKind) (placeholder: MaterialShaderAsset) =
            if isNull placeholder || placeholder.Kind <> kind || isNull placeholder.Name then
                fail "Native sun-shadow shader reference is incomplete."
            let key = kind, placeholder.Name
            match shaderCache.TryGetValue key with
            | true, shader -> shader
            | _ ->
                let matches =
                    nativeShaderDefinitions
                    |> Array.filter (fun shader -> not (isNull shader) && shader.Kind = kind && shader.Name = placeholder.Name)
                if matches.Length <> 1 || isNull matches[0].Data ||
                   matches[0].DataSize <> uint32 matches[0].Data.Length ||
                   isNull matches[0].LoadDefTail ||
                   matches[0].LoadDefTail.Length <> MaterialShaderAsset.GetLoadDefTailSize kind then
                    fail $"Native sun-shadow shader '{placeholder.Name}' is missing or incomplete."
                let original = matches[0]
                let shader =
                    MaterialShaderAsset(
                        Kind = kind, NamePointer = Unchecked.defaultof<_>,
                        Name = prefix + "iw4_sun_shadow/" + original.Name,
                        DataPointer = Unchecked.defaultof<_>, DataSize = original.DataSize,
                        LoadDefTail = Array.copy original.LoadDefTail, Data = Array.copy original.Data)
                shaderCache.Add(key, shader)
                shader

        let slots = sourceSet.TechniqueSlots.ToArray()
        // Shadow coverage can select the ordinary sun route even for the same
        // visible surface. Convert both pairs as full passes with matching output state.
        // Retain authored aliases for alternate/LOD sun slots; do not introduce a
        // different reduced-specular shader from an sf0 donor family.
        if slots[25].Technique <> slots[11].Technique ||
           slots[26].Technique <> slots[12].Technique then
            fail "Alternate sun slots differ from the supported source aliases."
        for shadowed in [| true; false |] do
            let ordinarySlot = if shadowed then 13 else 11
            let fogSlot = ordinarySlot + 1
            let lightingPrefix =
                (if modelReceiver then "lp_" else "lm_") + (if shadowed then "sm_sun_" else "sun_")
            let stableCount = stableCounts[familyIndex] - (if shadowed then 0uy else 3uy)
            let ordinary = nativeSet.TechniqueSlots[ordinarySlot].Technique
            let distanceFog = nativeSet.TechniqueSlots[fogSlot].Technique
            let previous = slots[ordinarySlot].Technique
            if isNull previous || isNull ordinary || isNull distanceFog ||
               slots[fogSlot].Technique <> previous ||
               (not hasDistanceFog && ordinary <> distanceFog) ||
               previous.Name <> prefix + lightingPrefix + stem + colorSuffix + "_sm3" ||
               previous.PassCount <> 1us || previous.Passes.Count <> 1 ||
               previous.Flags <> MaterialTechniqueFlags.DeclarationHasOptionalSource then
                fail "Source sun receiver slots or technique changed."
            let oldPass = previous.Passes[0]
            let oldArgs = oldPass.Args.ToArray()
            let oldDecl = oldPass.VertexDeclaration
            if isNull oldPass.VertexShader ||
               oldPass.VertexShader.Name <> prefix + lightingPrefix + "s_" + vertexStem + colorSuffix + "_sm3.hlsl" ||
               isNull oldPass.PixelShader ||
               oldPass.PixelShader.Name <> prefix + lightingPrefix + stem + colorSuffix + "_sm3.hlsl" ||
               isNull oldDecl || oldDecl.Routing.Count <> MaterialVertexDeclarationAsset.RoutingCount ||
               oldPass.PerPrimArgCount <> (if modelReceiver then 2uy else 1uy) || oldPass.PerObjArgCount <> (if shadowed then 2uy else 4uy) ||
               oldPass.StableArgCount <> stableCount ||
               int oldPass.CustomSamplerFlags <> (if modelReceiver then 1 else 7) ||
               int oldPass.PrecompiledVertexShader <> (if modelReceiver then 1 else 0) ||
               oldArgs.Length <> int oldPass.PerPrimArgCount + int oldPass.PerObjArgCount + int oldPass.StableArgCount then
                fail "Source sun receiver pass is incomplete."

            let donorPass (slotIndex: int) (technique: MaterialTechniqueAsset) (fog: bool) =
                let fogName = if fog then "_dfog" else ""
                let shaderFog = if fog then "dfog" else "fog"
                if isNull technique ||
                   technique.Name <> lightingPrefix + stem + colorSuffix + fogName + "_sm3" ||
                   technique.Flags <> previous.Flags || technique.PassCount <> 1us ||
                   technique.Passes.Count <> 1 then
                    fail $"Native sun receiver slot {slotIndex} technique changed."
                let pass = technique.Passes[0]
                if isNull pass.VertexShader ||
                   pass.VertexShader.Name <> lightingPrefix + shaderFog + "_s_" + vertexStem + colorSuffix + "_sm3.hlsl" ||
                   isNull pass.PixelShader ||
                   pass.PixelShader.Name <> lightingPrefix + shaderFog + "_" + stem + colorSuffix + "_sm3.hlsl" ||
                   isNull pass.VertexDeclaration ||
                   pass.VertexDeclaration.Routing.Count <> MaterialVertexDeclarationAsset.RoutingCount ||
                   pass.Args.Count <> int pass.PerPrimArgCount + int pass.PerObjArgCount + int pass.StableArgCount ||
                   pass.PerPrimArgCount <> oldPass.PerPrimArgCount ||
                   pass.PerObjArgCount <> (if shadowed then 2uy else 1uy) ||
                   pass.CustomSamplerFlags <> oldPass.CustomSamplerFlags ||
                   pass.PrecompiledVertexShader <> oldPass.PrecompiledVertexShader ||
                   pass.VertexDeclaration.StreamCount <> oldDecl.StreamCount ||
                   pass.VertexDeclaration.HasOptionalSourceRaw <> oldDecl.HasOptionalSourceRaw ||
                   not (pass.VertexDeclaration.Routing.SequenceEqual(oldDecl.Routing)) then
                    fail $"Native sun receiver slot {slotIndex} pass contract changed."
                pass
            let nativeOrdinary = donorPass ordinarySlot ordinary false
            let nativeFog = donorPass fogSlot distanceFog hasDistanceFog
            if nativeOrdinary.StableArgCount <> oldPass.StableArgCount ||
               nativeFog.StableArgCount <> oldPass.StableArgCount + (if hasDistanceFog then 5uy else 0uy) then
                fail "Native sun receiver argument groups changed."
            let ordinaryArgs = nativeOrdinary.Args.ToArray()
            // The converted IW3 unshadowed pass retains clip-space lookup constants
            // and a white sampler fallback. Native IW4 omits all three object bindings.
            let comparableArgs =
                if shadowed then oldArgs
                else
                    let removed = int oldPass.PerPrimArgCount + 1
                    let expectedRemoved =
                        [| MaterialShaderArgumentType.CodeVertexConst, 17us, 4063233
                           MaterialShaderArgumentType.CodeVertexConst, 18us, 4128769
                           MaterialShaderArgumentType.CodePixelSampler, (if modelReceiver then 3us else 4us), 1 |]
                    for i in 0 .. 2 do
                        let arg = oldArgs[removed + i]
                        let kind, dest, raw = expectedRemoved[i]
                        if arg.Type <> kind || arg.Dest <> dest || arg.ArgumentRaw <> raw ||
                           arg.LiteralConstant.HasValue then
                            fail "Source unshadowed sun object bindings changed."
                    Array.append oldArgs[..removed-1] oldArgs[removed+3..]
            if comparableArgs.Length <> ordinaryArgs.Length then
                fail "Source and native ordinary sun argument counts differ."
            for i in 0 .. comparableArgs.Length - 1 do
                let sourceArg = comparableArgs[i]
                let nativeArg = ordinaryArgs[i]
                let raw = if sourceArg.ArgumentRaw = 2490369 && sourceArg.Type = MaterialShaderArgumentType.CodePixelConst
                          then 2424833 else sourceArg.ArgumentRaw
                let pixelRegister =
                    sourceArg.Type = MaterialShaderArgumentType.CodePixelConst ||
                    sourceArg.Type = MaterialShaderArgumentType.MaterialPixelConst
                let destination =
                    if shadowed && pixelRegister then int sourceArg.Dest + 3
                    elif not shadowed && sourceArg.Type = MaterialShaderArgumentType.MaterialPixelConst &&
                         (sourceArg.ArgumentRaw = 2006164043 || sourceArg.ArgumentRaw = 2006164040 ||
                          sourceArg.ArgumentRaw = -1240712390) then int sourceArg.Dest - 1
                    else int sourceArg.Dest
                if sourceArg.LiteralConstant.HasValue || nativeArg.LiteralConstant.HasValue ||
                   sourceArg.Type <> nativeArg.Type || raw <> nativeArg.ArgumentRaw ||
                   destination <> int nativeArg.Dest then
                    fail $"Source sun receiver argument {i} differs from the supported native binding."
            if hasDistanceFog then
                let fogArgs = nativeFog.Args.ToArray()
                let codeVertex = ordinaryArgs |> Array.findIndex (fun arg -> arg.ArgumentRaw = 2359297)
                let fogColor = ordinaryArgs |> Array.findIndex (fun arg -> arg.ArgumentRaw = 2424833)
                let expectedFogBindings =
                    Array.concat [| ordinaryArgs[..codeVertex]
                                    [| MaterialShaderArgumentAsset(0, MaterialShaderArgumentType.CodeVertexConst, 0us, 2555905, Nullable())
                                       MaterialShaderArgumentAsset(0, MaterialShaderArgumentType.CodeVertexConst, 0us, 2752513, Nullable()) |]
                                    ordinaryArgs[codeVertex+1..fogColor]
                                    [| MaterialShaderArgumentAsset(0, MaterialShaderArgumentType.CodePixelConst, 0us, 2555905, Nullable())
                                       MaterialShaderArgumentAsset(0, MaterialShaderArgumentType.CodePixelConst, 0us, 2752513, Nullable())
                                       MaterialShaderArgumentAsset(0, MaterialShaderArgumentType.CodePixelConst, 0us, 2621441, Nullable()) |]
                                    ordinaryArgs[fogColor+1..] |]
                let expectedFogDest i =
                    if i <= codeVertex then ordinaryArgs[i].Dest
                    elif i = codeVertex + 1 then 32us
                    elif i = codeVertex + 2 then 33us
                    elif i <= fogColor + 2 then ordinaryArgs[i - 2].Dest
                    elif i <= fogColor + 5 then uint16 (int ordinaryArgs[fogColor].Dest + i - fogColor - 2)
                    else uint16 (int ordinaryArgs[i - 5].Dest + 3)
                if fogArgs.Length <> expectedFogBindings.Length ||
                   (Array.exists2 (fun (actual: MaterialShaderArgumentAsset) (expected: MaterialShaderArgumentAsset) ->
                       actual.Type <> expected.Type || actual.ArgumentRaw <> expected.ArgumentRaw ||
                       actual.LiteralConstant.HasValue) fogArgs expectedFogBindings) ||
                   (fogArgs |> Array.mapi (fun i arg -> arg.Dest <> expectedFogDest i) |> Array.exists id) then
                    fail "Native distance-fog sun receiver bindings changed."

            let replacementPasses =
                if hasDistanceFog then [| ordinarySlot, ordinary, nativeOrdinary; fogSlot, distanceFog, nativeFog |]
                else [| ordinarySlot, ordinary, nativeOrdinary |]
            for slotIndex, donor, nativePass in replacementPasses do
                let nativeDecl = nativePass.VertexDeclaration
                let declaration =
                    MaterialVertexDeclarationAsset(
                        StreamCount = nativeDecl.StreamCount,
                        HasOptionalSourceRaw = nativeDecl.HasOptionalSourceRaw,
                        Routing = Array.AsReadOnly(nativeDecl.Routing.ToArray()))
                let args =
                    nativePass.Args
                    |> Seq.map (fun arg -> MaterialShaderArgumentAsset(arg.Offset, arg.Type, arg.Dest,
                                                                       arg.ArgumentRaw, arg.LiteralConstant))
                    |> Seq.toArray
                let pass =
                    MaterialPassAsset(
                        Offset = nativePass.Offset,
                        VertexDeclPointer = Unchecked.defaultof<_>,
                        VertexShaderPointer = Unchecked.defaultof<_>,
                        PixelShaderPointer = Unchecked.defaultof<_>,
                        PerPrimArgCount = nativePass.PerPrimArgCount,
                        PerObjArgCount = nativePass.PerObjArgCount,
                        StableArgCount = nativePass.StableArgCount,
                        CustomSamplerFlags = nativePass.CustomSamplerFlags,
                        PrecompiledVertexShader = nativePass.PrecompiledVertexShader,
                        ArgsPointer = Unchecked.defaultof<_>,
                        VertexDeclaration = declaration,
                        VertexShader = resolveShader MaterialShaderKind.Vertex nativePass.VertexShader,
                        PixelShader = resolveShader MaterialShaderKind.Pixel nativePass.PixelShader,
                        Args = Array.AsReadOnly args)
                let technique =
                    MaterialTechniqueAsset(
                        Offset = donor.Offset, NamePointer = Unchecked.defaultof<_>,
                        Name = prefix + "iw4_sun_shadow/" + donor.Name,
                        Flags = donor.Flags, PassCount = donor.PassCount,
                        Passes = Array.AsReadOnly [| pass |])
                slots[slotIndex] <- MaterialTechniqueSlot(slots[slotIndex].Type, Unchecked.defaultof<_>, technique)

            if not hasDistanceFog then
                slots[fogSlot] <- MaterialTechniqueSlot(slots[fogSlot].Type, Unchecked.defaultof<_>, slots[ordinarySlot].Technique)
        slots[25] <- MaterialTechniqueSlot(slots[25].Type, Unchecked.defaultof<_>, slots[11].Technique)
        slots[26] <- MaterialTechniqueSlot(slots[26].Type, Unchecked.defaultof<_>, slots[12].Technique)

        let newStateIndex = byte material.StateBits.Count
        for slotIndex in [| 11; 12; 13; 14; 25; 26 |] do
            entries[slotIndex] <- MaterialStateBitsEntry newStateIndex
        // Native model and vertex-colored world receivers keep depth writes and authored alpha testing.
        // Other color passes retain their source programs, so preserve their shared state.
        let nativeState =
            GfxStateBits(
                LoadBitsPointer = Unchecked.defaultof<_>,
                LoadBits = Array.AsReadOnly [| sourceColorState ||| 0x40000000u;
                                             (if modelReceiver || vertexColor then 0x0000000du else 0x0000000cu) |],
                CommandWordCount = 0u)
        copySet sourceSet (sourceSet.Name + "__iw4_sun_shadow") slots,
        entries, Array.append (material.StateBits.ToArray()) [| nativeState |],
        (shaderCache.Values |> Seq.toArray)

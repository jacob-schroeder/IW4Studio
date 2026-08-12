using IW4.FastFiles.Loaders.Database;
using IW4.FastFiles.Loaders.Assets.Material;
using IW4.FastFiles.Loaders.Assets.XModel;
using IW4.Assets.Assets.Fx;
using IW4.Assets.Assets.Material;
using XModelAssetModel = IW4.Assets.Assets.XModel.XModelAsset;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;
using IW4.Runtime.IO;
using XString = IW4.FastFiles.Pointers.XPointer<string>;

namespace IW4.FastFiles.Loaders.Assets.Fx;

public sealed class FxEffectDefLoader
{
    private readonly MaterialLoader _materialLoader = new();
    private readonly XModelLoader _xmodelLoader = new();

    public FxEffectDefAsset LoadFromAssetPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        return LoadFromPointerCore(cursor, pointer, context, requireAsset: true)
            ?? throw new InvalidDataException("Top-level Fx pointer resolved to null.");
    }

    public FxEffectDefAsset? LoadFromPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        return LoadFromPointerCore(cursor, pointer, context, requireAsset: false);
    }

    private FxEffectDefAsset? LoadFromPointerCore(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context,
        bool requireAsset)
    {
        if (pointer.Type == PointerType.Null)
        {
            if (requireAsset)
                throw new InvalidDataException("Top-level Fx pointer is null.");

            return null;
        }

        if (pointer.Type == PointerType.Offset)
        {
            context.PointerReader.ValidateOffsetPointerRange<FxEffectDefAsset>(pointer, FxEffectDefAsset.SerializedSize, "FxEffectDef");
            FxEffectDefAsset? canonical = context.ResolveCanonicalAsset<FxEffectDefAsset>(pointer, XAssetType.Fx);
            if (canonical is null)
            {
                throw new InvalidDataException(
                    $"FxEffectDef pointer 0x{unchecked((uint)pointer.Raw):X8} does not resolve to a canonical Fx asset.");
            }

            context.PatchCanonicalAssetPointerCell(
                pointer,
                canonical,
                "Packed FxEffectDef pointer has no destination cell.",
                "Canonical FxEffectDef has no runtime address.");
            return canonical;
        }

        if (pointer.Type is not (PointerType.Inline or PointerType.Insert))
        {
            throw new InvalidDataException(
                $"FxEffectDef pointer 0x{unchecked((uint)pointer.Raw):X8} uses unsupported source sentinel {pointer.Type}.");
        }

        return LoadInlineOrInsert(cursor, pointer, context);
    }

    private FxEffectDefAsset LoadInlineOrInsert(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        ProviderRegistrationOccurrence providerRegistration = context.BeginProviderRegistration(pointer);

        context.Blocks.Push(XFileBlockType.TEMP);
        try
        {
            XBlockAddress rootAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
            FxEffectDefAsset effect = ReadFxEffectDef(cursor, context);
            if (effect.StagingAddress != rootAddress)
            {
                throw new InvalidDataException(
                    $"FxEffectDef pointer patched to {rootAddress}, but root loaded at {effect.StagingAddress}.");
            }

            FxEffectDefAsset canonical = context.DB_AddXAsset(
                XAssetType.Fx,
                effect.Name,
                effect,
                providerRegistration);

            return canonical;
        }
        finally
        {
            context.Blocks.Pop();
        }
    }

    private FxEffectDefAsset ReadFxEffectDef(
        FastFileCursor cursor,
        DbLoadExecutionContext context)
    {
        int sourceOffset = cursor.Offset;
        byte[] rootBytes = context.Blocks.Load(cursor, FxEffectDefAsset.SerializedSize, out XBlockAddress rootAddress);
        var rootCursor = new FastFileCursor(rootBytes, rootAddress);

        XString namePointer = ReadXStringPointer(rootCursor, context);
        int flags = rootCursor.ReadInt32();
        int totalSize = rootCursor.ReadInt32();
        int msecLoopingLife = rootCursor.ReadInt32();
        int elemDefCountLooping = rootCursor.ReadInt32();
        int elemDefCountOneShot = rootCursor.ReadInt32();
        int elemDefCountEmission = rootCursor.ReadInt32();
        XPointer<FxElemDef[]> elemDefsPointer = ReadPointer<FxElemDef[]>(rootCursor, context, XPointerResolutionMode.Direct);

        if (rootCursor.Offset != FxEffectDefAsset.SerializedSize)
            throw new InvalidDataException($"FxEffectDef consumed 0x{rootCursor.Offset:X} bytes instead of 0x{FxEffectDefAsset.SerializedSize:X}.");

        int elemDefCount = checked(elemDefCountLooping + elemDefCountOneShot + elemDefCountEmission);

        string? name;
        IReadOnlyList<FxElemDef> elemDefs;
        context.Blocks.Push(XFileBlockType.LARGE);
        try
        {
            name = ReadXString(cursor, namePointer, context);
            elemDefs = ReadFxElemDefArray(cursor, elemDefsPointer.Untyped, elemDefCount, context);
        }
        finally
        {
            context.Blocks.Pop();
        }

        return new FxEffectDefAsset
        {
            Offset = sourceOffset,
            RuntimeAddress = rootAddress,
            NamePointer = namePointer,
            Name = name,
            Flags = flags,
            TotalSize = totalSize,
            MsecLoopingLife = msecLoopingLife,
            ElemDefCountLooping = elemDefCountLooping,
            ElemDefCountOneShot = elemDefCountOneShot,
            ElemDefCountEmission = elemDefCountEmission,
            ElemDefsPointer = elemDefsPointer,
            ElemDefs = elemDefs
        };
    }


    private IReadOnlyList<FxElemDef> ReadFxElemDefArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        if (count < 0)
            throw new InvalidDataException($"Invalid negative FxElemDef count {count}.");

        if (pointer.Type == PointerType.Null || count == 0)
            return [];

        XBlockAddress elemAddress = PatchNonNullCurrentPointerCell(pointer, alignment: 4, context);
        byte[] elemBytes = context.Blocks.Load(cursor, checked(count * FxElemDef.SerializedSize));
        var elemCursor = new FastFileCursor(elemBytes, elemAddress);

        var roots = new FxElemDefRoot[count];
        for (int i = 0; i < roots.Length; i++)
            roots[i] = ReadFxElemDefRoot(elemCursor, context);

        var elems = new FxElemDef[count];
        for (int i = 0; i < elems.Length; i++)
            elems[i] = ReadFxElemDefChildren(cursor, roots[i], context);


        return elems;
    }

    private static FxElemDefRoot ReadFxElemDefRoot(FastFileCursor cursor, DbLoadExecutionContext context)
    {
        int offset = cursor.AddressAt(cursor.Offset)?.Offset ?? cursor.Offset;
        int start = cursor.Offset;
        int flags = cursor.ReadInt32();
        FxSpawnDef spawn = ReadFxSpawnDef(cursor);
        FxFloatRange spawnRange = ReadFxFloatRange(cursor);
        FxFloatRange fadeInRange = ReadFxFloatRange(cursor);
        FxFloatRange fadeOutRange = ReadFxFloatRange(cursor);
        float spawnFrustumCullRadius = cursor.ReadSingle();
        FxIntRange spawnDelayMsec = ReadFxIntRange(cursor);
        FxIntRange lifeSpanMsec = ReadFxIntRange(cursor);
        IReadOnlyList<FxFloatRange> spawnOrigin = ReadFxFloatRanges(cursor, 3);
        FxFloatRange spawnOffsetRadius = ReadFxFloatRange(cursor);
        FxFloatRange spawnOffsetHeight = ReadFxFloatRange(cursor);
        IReadOnlyList<FxFloatRange> spawnAngles = ReadFxFloatRanges(cursor, 3);
        IReadOnlyList<FxFloatRange> angularVelocity = ReadFxFloatRanges(cursor, 3);
        FxFloatRange initialRotation = ReadFxFloatRange(cursor);
        FxFloatRange gravity = ReadFxFloatRange(cursor);
        FxFloatRange reflectionFactor = ReadFxFloatRange(cursor);
        FxElemAtlas atlas = ReadFxElemAtlas(cursor);
        var elemType = (FxElemType)cursor.ReadByte();
        byte visualCount = cursor.ReadByte();
        byte velIntervalCount = cursor.ReadByte();
        byte visStateIntervalCount = cursor.ReadByte();
        XPointer<FxElemVelStateSample[]> velSamplesPointer = ReadPointer<FxElemVelStateSample[]>(cursor, context, XPointerResolutionMode.Direct);
        XPointer<FxElemVisStateSample[]> visSamplesPointer = ReadPointer<FxElemVisStateSample[]>(cursor, context, XPointerResolutionMode.Direct);
        FxElemDefVisualsRoot visuals = ReadFxElemDefVisualsRoot(
            cursor,
            context,
            capturePointer: visualCount > 1 || !IsNoChildVisual(elemType));
        Bounds collBounds = ReadBounds(cursor);
        FxEffectDefRef effectOnImpact = ReadFxEffectDefRefRoot(cursor, context);
        FxEffectDefRef effectOnDeath = ReadFxEffectDefRefRoot(cursor, context);
        FxEffectDefRef effectEmitted = ReadFxEffectDefRefRoot(cursor, context);
        FxFloatRange emitDist = ReadFxFloatRange(cursor);
        FxFloatRange emitDistVariance = ReadFxFloatRange(cursor);
        XPointer<FxElemExtendedDef> extendedPointer = ReadPointer<FxElemExtendedDef>(cursor, context, XPointerResolutionMode.Direct);
        byte sortOrder = cursor.ReadByte();
        byte lightingFrac = cursor.ReadByte();
        byte useItemClip = cursor.ReadByte();
        byte fadeInfo = cursor.ReadByte();

        if (cursor.Offset - start != FxElemDef.SerializedSize)
            throw new InvalidDataException($"FxElemDef consumed 0x{cursor.Offset - start:X} bytes instead of 0x{FxElemDef.SerializedSize:X}.");

        return new FxElemDefRoot(
            offset,
            flags,
            spawn,
            spawnRange,
            fadeInRange,
            fadeOutRange,
            spawnFrustumCullRadius,
            spawnDelayMsec,
            lifeSpanMsec,
            spawnOrigin,
            spawnOffsetRadius,
            spawnOffsetHeight,
            spawnAngles,
            angularVelocity,
            initialRotation,
            gravity,
            reflectionFactor,
            atlas,
            elemType,
            visualCount,
            velIntervalCount,
            visStateIntervalCount,
            velSamplesPointer,
            visSamplesPointer,
            visuals,
            collBounds,
            effectOnImpact,
            effectOnDeath,
            effectEmitted,
            emitDist,
            emitDistVariance,
            extendedPointer,
            sortOrder,
            lightingFrac,
            useItemClip,
            fadeInfo);
    }

    private FxElemDef ReadFxElemDefChildren(
        FastFileCursor cursor,
        FxElemDefRoot root,
        DbLoadExecutionContext context)
    {
        IReadOnlyList<FxElemVelStateSample> velSamples = ReadFxElemVelStateSamples(
            cursor,
            root.VelSamplesPointer.Untyped,
            root.VelIntervalCount + 1,
            context);
        IReadOnlyList<FxElemVisStateSample> visSamples = ReadFxElemVisStateSamples(
            cursor,
            root.VisSamplesPointer.Untyped,
            root.VisStateIntervalCount + 1,
            context);
        FxVisualPayload visuals = ReadFxElemDefVisuals(cursor, root.Visuals, root.ElemType, root.VisualCount, context);
        FxEffectDefRef effectOnImpact = ReadFxEffectDefRef(cursor, root.EffectOnImpact, context);
        FxEffectDefRef effectOnDeath = ReadFxEffectDefRef(cursor, root.EffectOnDeath, context);
        FxEffectDefRef effectEmitted = ReadFxEffectDefRef(cursor, root.EffectEmitted, context);
        FxElemExtendedDef? extended = ReadFxElemExtended(cursor, root.ExtendedPointer.Untyped, root.ElemType, context);


        return new FxElemDef
        {
            Offset = root.Offset,
            Flags = root.Flags,
            Spawn = root.Spawn,
            SpawnRange = root.SpawnRange,
            FadeInRange = root.FadeInRange,
            FadeOutRange = root.FadeOutRange,
            SpawnFrustumCullRadius = root.SpawnFrustumCullRadius,
            SpawnDelayMsec = root.SpawnDelayMsec,
            LifeSpanMsec = root.LifeSpanMsec,
            SpawnOrigin = root.SpawnOrigin,
            SpawnOffsetRadius = root.SpawnOffsetRadius,
            SpawnOffsetHeight = root.SpawnOffsetHeight,
            SpawnAngles = root.SpawnAngles,
            AngularVelocity = root.AngularVelocity,
            InitialRotation = root.InitialRotation,
            Gravity = root.Gravity,
            ReflectionFactor = root.ReflectionFactor,
            Atlas = root.Atlas,
            ElemType = root.ElemType,
            VisualCount = root.VisualCount,
            VelIntervalCount = root.VelIntervalCount,
            VisStateIntervalCount = root.VisStateIntervalCount,
            VelSamplesPointer = root.VelSamplesPointer,
            VelSamples = velSamples,
            VisSamplesPointer = root.VisSamplesPointer,
            VisSamples = visSamples,
            Visuals = visuals.InlineVisual ?? new FxElemDefVisuals { Offset = root.Visuals.Offset },
            VisualArrayPointer = visuals.VisualArrayPointer,
            VisualArray = visuals.VisualArray,
            MarkVisualArrayPointer = visuals.MarkVisualArrayPointer,
            MarkVisualArray = visuals.MarkVisualArray,
            CollBounds = root.CollBounds,
            EffectOnImpact = effectOnImpact,
            EffectOnDeath = effectOnDeath,
            EffectEmitted = effectEmitted,
            EmitDist = root.EmitDist,
            EmitDistVariance = root.EmitDistVariance,
            ExtendedPointer = root.ExtendedPointer,
            Extended = extended,
            SortOrder = root.SortOrder,
            LightingFrac = root.LightingFrac,
            UseItemClip = root.UseItemClip,
            FadeInfo = root.FadeInfo
        };
    }

    private static IReadOnlyList<FxElemVelStateSample> ReadFxElemVelStateSamples(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        if (pointer.Type == PointerType.Null || count <= 0)
            return [];

        XBlockAddress address = PatchNonNullCurrentPointerCell(pointer, alignment: 4, context);
        byte[] bytes = context.Blocks.Load(cursor, checked(count * 0x60));
        var sampleCursor = new FastFileCursor(bytes, address);
        var samples = new FxElemVelStateSample[count];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = new FxElemVelStateSample(ReadFxElemVelStateInFrame(sampleCursor), ReadFxElemVelStateInFrame(sampleCursor));
        return samples;
    }

    private static IReadOnlyList<FxElemVisStateSample> ReadFxElemVisStateSamples(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        if (pointer.Type == PointerType.Null || count <= 0)
            return [];

        XBlockAddress address = PatchNonNullCurrentPointerCell(pointer, alignment: 4, context);
        byte[] bytes = context.Blocks.Load(cursor, checked(count * 0x30));
        var sampleCursor = new FastFileCursor(bytes, address);
        var samples = new FxElemVisStateSample[count];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = new FxElemVisStateSample(ReadFxElemVisualState(sampleCursor), ReadFxElemVisualState(sampleCursor));
        return samples;
    }

    private FxVisualPayload ReadFxElemDefVisuals(
        FastFileCursor cursor,
        FxElemDefVisualsRoot inlineVisual,
        FxElemType elemType,
        byte visualCount,
        DbLoadExecutionContext context)
    {
        if (elemType == FxElemType.Decal)
        {
            XPointer<FxElemMarkVisuals[]> markPointer = ReinterpretPointer<FxElemMarkVisuals[]>(inlineVisual.Raw, XPointerResolutionMode.Direct);
            return new FxVisualPayload(null, null, [], markPointer, ReadFxElemMarkVisualArray(cursor, markPointer.Untyped, visualCount, context));
        }

        if (visualCount > 1)
        {
            XPointer<FxElemDefVisuals[]> visualPointer = ReinterpretPointer<FxElemDefVisuals[]>(inlineVisual.Raw, XPointerResolutionMode.Direct);
            return new FxVisualPayload(null, visualPointer, ReadFxElemVisualArray(cursor, visualPointer.Untyped, elemType, visualCount, context), null, []);
        }

        return new FxVisualPayload(ReadFxElemVisual(cursor, inlineVisual, elemType, context), null, [], null, []);
    }

    private IReadOnlyList<FxElemDefVisuals> ReadFxElemVisualArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        FxElemType elemType,
        int visualCount,
        DbLoadExecutionContext context)
    {
        if (pointer.Type == PointerType.Null || visualCount <= 0)
            return [];

        XBlockAddress visualAddress = PatchNonNullCurrentPointerCell(pointer, alignment: 4, context);
        byte[] visualBytes = context.Blocks.Load(cursor, checked(visualCount * FxElemDefVisuals.SerializedSize));
        var visualCursor = new FastFileCursor(visualBytes, visualAddress);
        var visuals = new FxElemDefVisuals[visualCount];
        for (int i = 0; i < visuals.Length; i++)
        {
            FxElemDefVisualsRoot visual = ReadFxElemDefVisualsRoot(
                visualCursor,
                context,
                capturePointer: !IsNoChildVisual(elemType));
            visuals[i] = ReadFxElemVisual(cursor, visual, elemType, context);
        }

        return visuals;
    }

    private IReadOnlyList<FxElemMarkVisuals> ReadFxElemMarkVisualArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int visualCount,
        DbLoadExecutionContext context)
    {
        if (pointer.Type == PointerType.Null || visualCount <= 0)
            return [];

        XBlockAddress markAddress = PatchNonNullCurrentPointerCell(pointer, alignment: 4, context);
        byte[] markBytes = context.Blocks.Load(cursor, checked(visualCount * FxElemMarkVisuals.SerializedSize));
        var markCursor = new FastFileCursor(markBytes, markAddress);
        var marks = new FxElemMarkVisuals[visualCount];
        for (int i = 0; i < marks.Length; i++)
        {
            int offset = markCursor.AddressAt(markCursor.Offset)?.Offset ?? markCursor.Offset;
            XPointer<MaterialAsset> material0Pointer = ReadPointer<MaterialAsset>(markCursor, context, XPointerResolutionMode.AliasCell);
            XPointer<MaterialAsset> material1Pointer = ReadPointer<MaterialAsset>(markCursor, context, XPointerResolutionMode.AliasCell);
            MaterialAsset? material0 = ReadMaterialPointer(
                cursor,
                material0Pointer.Untyped,
                context);
            MaterialAsset? material1 = ReadMaterialPointer(
                cursor,
                material1Pointer.Untyped,
                context);
            marks[i] = new FxElemMarkVisuals
            {
                Offset = offset,
                Material0Pointer = material0Pointer,
                Material0 = material0,
                Material1Pointer = material1Pointer,
                Material1 = material1
            };
        }

        return marks;
    }

    private FxElemDefVisuals ReadFxElemVisual(
        FastFileCursor cursor,
        FxElemDefVisualsRoot visual,
        FxElemType elemType,
        DbLoadExecutionContext context)
    {
        switch (elemType)
        {
            case FxElemType.Model:
            {
                XPointer<XModelAssetModel> modelPointer = ReinterpretPointer<XModelAssetModel>(visual.Raw, XPointerResolutionMode.AliasCell);
                XModelAssetModel? model =
                    ReadXModelPointer(cursor, modelPointer.Untyped, context);
                return new FxElemDefVisuals
                {
                    Offset = visual.Offset,
                    Visual = new FxModelVisual
                    {
                        ModelPointer = modelPointer,
                        Model = model
                    }
                };
            }

            case FxElemType.OmniLight:
            case FxElemType.SpotLight:
                return new FxElemDefVisuals
                {
                    Offset = visual.Offset,
                    Visual = new FxNoChildVisual { Reserved = visual.Raw.Raw }
                };

            case FxElemType.Sound:
            {
                XString soundPointer = ReinterpretPointer<string>(visual.Raw, XPointerResolutionMode.Direct);
                string? soundName = ReadXString(cursor, soundPointer, context);
                return new FxElemDefVisuals
                {
                    Offset = visual.Offset,
                    Visual = new FxSoundVisual
                    {
                        SoundNamePointer = soundPointer,
                        SoundName = soundName
                    }
                };
            }

            case FxElemType.Runner:
            {
                var effectRef = ReadFxEffectDefRef(cursor, new FxEffectDefRef { NamePointer = ReinterpretPointer<string>(visual.Raw, XPointerResolutionMode.Direct) }, context);
                return new FxElemDefVisuals
                {
                    Offset = visual.Offset,
                    Visual = new FxEffectVisual { EffectDef = effectRef }
                };
            }

            default:
            {
                XPointer<MaterialAsset> materialPointer = ReinterpretPointer<MaterialAsset>(visual.Raw, XPointerResolutionMode.AliasCell);
                MaterialAsset? material = ReadMaterialPointer(
                    cursor,
                    materialPointer.Untyped,
                    context);
                return new FxElemDefVisuals
                {
                    Offset = visual.Offset,
                    Visual = new FxMaterialVisual
                    {
                        MaterialPointer = materialPointer,
                        Material = material
                    }
                };
            }
        }
    }

    private static FxEffectDefRef ReadFxEffectDefRef(
        FastFileCursor cursor,
        FxEffectDefRef effectRef,
        DbLoadExecutionContext context)
    {
        return new FxEffectDefRef
        {
            NamePointer = effectRef.NamePointer,
            Name = ReadXString(cursor, effectRef.NamePointer, context)
        };
    }

    private static FxElemExtendedDef? ReadFxElemExtended(
        FastFileCursor cursor,
        XPointerReference pointer,
        FxElemType elemType,
        DbLoadExecutionContext context)
    {
        if (pointer.Type == PointerType.Null)
            return null;

        return elemType switch
        {
            FxElemType.Trail => new FxElemExtendedDef
            {
                Kind = FxElemExtendedDefKind.Trail,
                TrailDef = ReadFxTrailDef(cursor, pointer, context)
            },
            FxElemType.SparkFountain => new FxElemExtendedDef
            {
                Kind = FxElemExtendedDefKind.SparkFountain,
                SparkFountainDef = ReadFxSparkFountainDef(cursor, pointer, context)
            },
            _ => new FxElemExtendedDef
            {
                Kind = FxElemExtendedDefKind.DefaultBytePayload,
                DefaultBytePayload = ReadFxExtendedDefaultByte(cursor, pointer, context)
            }
        };
    }

    private static FxTrailDef ReadFxTrailDef(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        XBlockAddress trailAddress = PatchNonNullCurrentPointerCell(pointer, alignment: 4, context);
        byte[] trailBytes = context.Blocks.Load(cursor, FxTrailDef.SerializedSize);
        var trailCursor = new FastFileCursor(trailBytes, trailAddress);

        int scrollTimeMsec = trailCursor.ReadInt32();
        int repeatDist = trailCursor.ReadInt32();
        float invSplitDist = trailCursor.ReadSingle();
        float invSplitArcDist = trailCursor.ReadSingle();
        float invSplitTime = trailCursor.ReadSingle();
        int vertCount = trailCursor.ReadInt32();
        XPointer<FxTrailVertex[]> vertsPointer = ReadPointer<FxTrailVertex[]>(trailCursor, context, XPointerResolutionMode.Direct);
        int indCount = trailCursor.ReadInt32();
        XPointer<ushort[]> indsPointer = ReadPointer<ushort[]>(trailCursor, context, XPointerResolutionMode.Direct);

        if (trailCursor.Offset != FxTrailDef.SerializedSize)
            throw new InvalidDataException($"FxTrailDef consumed 0x{trailCursor.Offset:X} bytes instead of 0x{FxTrailDef.SerializedSize:X}.");

        return new FxTrailDef
        {
            ScrollTimeMsec = scrollTimeMsec,
            RepeatDist = repeatDist,
            InvSplitDist = invSplitDist,
            InvSplitArcDist = invSplitArcDist,
            InvSplitTime = invSplitTime,
            VertCount = vertCount,
            VertsPointer = vertsPointer,
            Verts = ReadFxTrailVerts(cursor, vertsPointer.Untyped, vertCount, context),
            IndCount = indCount,
            IndsPointer = indsPointer,
            Inds = ReadUInt16Payload(cursor, indsPointer.Untyped, indCount, alignment: 2, context)
        };
    }

    private static FxSparkFountainDef ReadFxSparkFountainDef(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        PatchNonNullCurrentPointerCell(pointer, alignment: 4, context);
        byte[] bytes = context.Blocks.Load(cursor, FxSparkFountainDef.SerializedSize);
        var c = new FastFileCursor(bytes);
        return new FxSparkFountainDef(
            c.ReadSingle(),
            c.ReadSingle(),
            c.ReadSingle(),
            c.ReadSingle(),
            c.ReadSingle(),
            c.ReadInt32(),
            c.ReadSingle(),
            c.ReadSingle(),
            c.ReadSingle(),
            c.ReadSingle(),
            c.ReadSingle(),
            c.ReadSingle(),
            c.ReadSingle());
    }

    private static byte ReadFxExtendedDefaultByte(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        PatchNonNullCurrentPointerCell(pointer, alignment: 1, context);
        return context.Blocks.Load(cursor, 1)[0];
    }

    private static IReadOnlyList<FxTrailVertex> ReadFxTrailVerts(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        if (pointer.Type == PointerType.Null || count <= 0)
            return [];

        XBlockAddress address = PatchNonNullCurrentPointerCell(pointer, alignment: 4, context);
        byte[] bytes = context.Blocks.Load(cursor, checked(count * FxTrailDef.VertexSerializedSize));
        var c = new FastFileCursor(bytes, address);
        var verts = new FxTrailVertex[count];
        for (int i = 0; i < verts.Length; i++)
            verts[i] = new FxTrailVertex(c.ReadSingle(), c.ReadSingle(), c.ReadSingle(), c.ReadSingle(), c.ReadSingle());
        return verts;
    }

    private static IReadOnlyList<ushort> ReadUInt16Payload(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        int alignment,
        DbLoadExecutionContext context)
    {
        if (pointer.Type == PointerType.Null || count <= 0)
            return [];

        XBlockAddress address = PatchNonNullCurrentPointerCell(pointer, alignment, context);
        byte[] bytes = context.Blocks.Load(cursor, checked(count * sizeof(ushort)));
        var c = new FastFileCursor(bytes, address);
        var values = new ushort[count];
        for (int i = 0; i < values.Length; i++)
            values[i] = c.ReadUInt16();
        return values;
    }

    private MaterialAsset? ReadMaterialPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        return _materialLoader.LoadFromPointer(
            cursor,
            pointer,
            context);
    }

    private XModelAssetModel? ReadXModelPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        return _xmodelLoader.LoadFromPointer(
            cursor,
            pointer,
            context);
    }

    private static XBlockAddress PatchNonNullCurrentPointerCell(
        XPointerReference pointer,
        int alignment,
        DbLoadExecutionContext context)
    {
        if (pointer.Type == PointerType.Null)
            throw new InvalidDataException("Cannot patch a null Fx pointer cell to the current stream position.");

        if (pointer.CellAddress is not { } cellAddress)
            throw new InvalidDataException($"Pointer 0x{pointer.Raw:X8} has no destination cell address to patch.");

        if (alignment > 1)
            context.Blocks.AlignCurrent(alignment);

        XBlockAddress targetAddress = context.Blocks.CurrentAddress;
        context.Blocks.WriteInt32(cellAddress, XPointerCodec.Encode(targetAddress));
        return targetAddress;
    }

    private static string? ReadXString(
        FastFileCursor cursor,
        XString pointer,
        DbLoadExecutionContext context)
    {
        return context.PointerReader.LoadXString(cursor, pointer);
    }

    private static XPointer<T> ReadPointer<T>(
        FastFileCursor cursor,
        DbLoadExecutionContext context,
        XPointerResolutionMode mode) => context.PointerReader.ReadDeferredPointer<T>(cursor, mode);

    private static XPointer<T> ReinterpretPointer<T>(
        XPointer<object> pointer,
        XPointerResolutionMode mode)
    {
        return new XPointer<T>(pointer.Raw, mode, pointer.CellAddress);
    }

    private static XString ReadXStringPointer(FastFileCursor cursor, DbLoadExecutionContext context) =>
        ReadPointer<string>(cursor, context, XPointerResolutionMode.Direct);

    private static FxElemDefVisualsRoot ReadFxElemDefVisualsRoot(
        FastFileCursor cursor,
        DbLoadExecutionContext context,
        bool capturePointer)
    {
        int cellOffset = cursor.Offset;
        int offset = cursor.AddressAt(cellOffset)?.Offset ?? cellOffset;
        XPointer<object> raw = capturePointer
            ? ReadPointer<object>(cursor, context, XPointerResolutionMode.Direct)
            : context.PointerReader.FromRaw<object>(
                cursor.ReadInt32(),
                XPointerResolutionMode.Direct,
                cursor.AddressAt(cellOffset));
        return new FxElemDefVisualsRoot(offset, raw);
    }

    private static bool IsNoChildVisual(FxElemType elemType) =>
        elemType is FxElemType.OmniLight or FxElemType.SpotLight;

    private static FxEffectDefRef ReadFxEffectDefRefRoot(FastFileCursor cursor, DbLoadExecutionContext context)
    {
        return new FxEffectDefRef
        {
            NamePointer = ReadXStringPointer(cursor, context)
        };
    }

    private static FxIntRange ReadFxIntRange(FastFileCursor cursor)
    {
        return new FxIntRange(cursor.ReadInt32(), cursor.ReadInt32());
    }

    private static FxSpawnDef ReadFxSpawnDef(FastFileCursor cursor)
    {
        return new FxSpawnDef(cursor.ReadInt32(), cursor.ReadInt32());
    }

    private static FxFloatRange ReadFxFloatRange(FastFileCursor cursor)
    {
        return new FxFloatRange(cursor.ReadSingle(), cursor.ReadSingle());
    }

    private static IReadOnlyList<FxFloatRange> ReadFxFloatRanges(
        FastFileCursor cursor,
        int count)
    {
        var ranges = new FxFloatRange[count];
        for (int i = 0; i < ranges.Length; i++)
            ranges[i] = ReadFxFloatRange(cursor);
        return ranges;
    }

    private static FxElemAtlas ReadFxElemAtlas(FastFileCursor cursor)
    {
        return new FxElemAtlas(
            cursor.ReadByte(),
            cursor.ReadByte(),
            cursor.ReadByte(),
            cursor.ReadByte(),
            cursor.ReadByte(),
            cursor.ReadByte(),
            unchecked((short)cursor.ReadUInt16()));
    }

    private static Bounds ReadBounds(FastFileCursor cursor)
    {
        return new Bounds(ReadVec3(cursor), ReadVec3(cursor));
    }

    private static Vec3 ReadVec3(FastFileCursor cursor)
    {
        return new Vec3(cursor.ReadSingle(), cursor.ReadSingle(), cursor.ReadSingle());
    }

    private static FxElemVelStateInFrame ReadFxElemVelStateInFrame(FastFileCursor cursor)
    {
        return new FxElemVelStateInFrame(
            new FxElemVec3Range(ReadVec3(cursor), ReadVec3(cursor)),
            new FxElemVec3Range(ReadVec3(cursor), ReadVec3(cursor)));
    }

    private static FxElemVisualState ReadFxElemVisualState(FastFileCursor cursor)
    {
        return new FxElemVisualState(
            new FxElemColor(cursor.ReadByte(), cursor.ReadByte(), cursor.ReadByte(), cursor.ReadByte()),
            cursor.ReadSingle(),
            cursor.ReadSingle(),
            cursor.ReadSingle(),
            cursor.ReadSingle(),
            cursor.ReadSingle());
    }


}

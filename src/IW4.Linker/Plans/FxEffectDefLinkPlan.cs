using IW4.Assets.Assets;
using IW4.Assets.Assets.Fx;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;

namespace IW4.Linker.Plans;

/// <summary>
/// Frozen FxEffectDef graph. Structural tables are unique presence storage;
/// Material and XModel visual arms are provider dependencies, while runner
/// and effect-reference arms retain the native XString traversal.
/// </summary>
internal sealed class FxEffectDefLinkPlan : AssetLinkPlan
{
    private FxEffectDefLinkPlan(
        AssetKey key,
        string originalSerializedName,
        FxEffectDefAsset definition,
        LinkStorageTarget? elements,
        LinkAssetFreezeScope freeze)
        : base(
            key,
            originalSerializedName,
            freeze.FreezeProviderName(originalSerializedName, 0, "Asset.Name"))
    {
        var writer = new LinkTemplateWriter(FxEffectDefAsset.SerializedSize);
        writer.Skip(sizeof(int));
        writer.WriteInt32(definition.Flags);
        writer.WriteInt32(definition.TotalSize);
        writer.WriteInt32(definition.MsecLoopingLife);
        writer.WriteInt32(definition.ElemDefCountLooping);
        writer.WriteInt32(definition.ElemDefCountOneShot);
        writer.WriteInt32(definition.ElemDefCountEmission);
        writer.Skip(sizeof(int));
        Root = LinkStorageSymbol.SourceBytes(
            XFileBlockType.TEMP,
            writer.Complete(),
            alignment: 4,
            root => elements is null
                ? [NameOperation(root, 0)]
                :
                [
                    NameOperation(root, 0),
                    DirectOperation(root, 0x1c, elements.Value, "Fx.ElemDefs")
                ]);
    }

    internal override LinkStorageSymbol Root { get; }

    public static AssetLinkPlan Freeze(
        AssetKey key,
        string originalSerializedName,
        FxEffectDefAsset definition,
        LinkAssetFreezeScope freeze)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(freeze);
        IReadOnlyList<FxElemDef> elements = definition.ElemDefs ??
            throw new InvalidDataException("Fx.ElemDefs cannot be null.");
        int declaredCount;
        try
        {
            declaredCount = checked(
                definition.ElemDefCountLooping +
                definition.ElemDefCountOneShot +
                definition.ElemDefCountEmission);
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException(
                "Fx element count sum exceeds Int32.",
                exception);
        }
        if (declaredCount < 0 || declaredCount != elements.Count)
        {
            throw new InvalidDataException(
                $"Fx declares {declaredCount} element rows, but retains {elements.Count}.");
        }
        if (originalSerializedName.StartsWith(','))
        {
            if (definition.Flags != 0 ||
                definition.TotalSize != 0 ||
                definition.MsecLoopingLife != 0 ||
                definition.ElemDefCountLooping != 0 ||
                definition.ElemDefCountOneShot != 0 ||
                definition.ElemDefCountEmission != 0 ||
                elements.Count != 0 ||
                definition.ElemDefsPointer.Raw != 0)
            {
                throw new InvalidDataException(
                    "A comma-prefixed Fx provider must have a zeroed reference body.");
            }
            return ExternalAssetLinkPlan.Create(
                key,
                XAssetType.Fx,
                originalSerializedName,
                freeze);
        }
        LinkStorageTarget? table =
            elements.Count == 0 && definition.ElemDefsPointer.Type == PointerType.Null
                ? null
                : FreezeElementTable(
                    elements,
                    definition.ElemDefsPointer.Untyped,
                    freeze);
        return new FxEffectDefLinkPlan(
            key,
            originalSerializedName,
            definition,
            table,
            freeze);
    }

    private static LinkStorageTarget FreezeElementTable(
        IReadOnlyList<FxElemDef> elements,
        XPointerReference pointer,
        LinkAssetFreezeScope freeze)
    {
        FrozenElement[] frozen = elements
            .Select((element, index) => FrozenElement.Freeze(
                element ?? throw new InvalidDataException(
                    $"Fx.ElemDefs[{index}] cannot be null."),
                index,
                freeze))
            .ToArray();
        var writer = new LinkTemplateWriter(
            checked(frozen.Length * FxElemDef.SerializedSize));
        foreach (FrozenElement element in frozen)
            writer.WriteBytes(element.Template);
        return freeze.FreezeStorage(
            pointer,
            writer.Complete(),
            XFileBlockType.LARGE,
            alignment: 4,
            (table, addend) =>
            {
                var operations = new List<LinkOperation>();
                for (int index = 0; index < frozen.Length; index++)
                {
                    frozen[index].AppendOperations(
                        table,
                        checked(addend + index * FxElemDef.SerializedSize),
                        operations);
                }
                return operations;
            },
            "Fx.ElemDefs");
    }

    private static LinkStorageTarget? FreezeVelocitySamples(
        IReadOnlyList<FxElemVelStateSample> samples,
        XPointerReference pointer,
        string fieldPath,
        LinkAssetFreezeScope freeze)
    {
        if (samples.Count == 0 && pointer.Type == PointerType.Null)
            return null;
        var writer = new LinkTemplateWriter(checked(samples.Count * 0x60));
        for (int index = 0; index < samples.Count; index++)
        {
            FxElemVelStateSample sample = samples[index] ??
                throw new InvalidDataException($"{fieldPath}[{index}] cannot be null.");
            WriteVelocityFrame(
                writer,
                sample.Local ?? throw new InvalidDataException(
                    $"{fieldPath}[{index}].Local cannot be null."),
                $"{fieldPath}[{index}].Local");
            WriteVelocityFrame(
                writer,
                sample.World ?? throw new InvalidDataException(
                    $"{fieldPath}[{index}].World cannot be null."),
                $"{fieldPath}[{index}].World");
        }
        return freeze.FreezeStorage(
            pointer,
            writer.Complete(),
            XFileBlockType.LARGE,
            alignment: 4,
            operations: null,
            fieldPath);
    }

    private static LinkStorageTarget? FreezeVisualSamples(
        IReadOnlyList<FxElemVisStateSample> samples,
        XPointerReference pointer,
        string fieldPath,
        LinkAssetFreezeScope freeze)
    {
        if (samples.Count == 0 && pointer.Type == PointerType.Null)
            return null;
        var writer = new LinkTemplateWriter(checked(samples.Count * 0x30));
        for (int index = 0; index < samples.Count; index++)
        {
            FxElemVisStateSample sample = samples[index] ??
                throw new InvalidDataException($"{fieldPath}[{index}] cannot be null.");
            WriteVisualState(
                writer,
                sample.Base ?? throw new InvalidDataException(
                    $"{fieldPath}[{index}].Base cannot be null."));
            WriteVisualState(
                writer,
                sample.Amplitude ?? throw new InvalidDataException(
                    $"{fieldPath}[{index}].Amplitude cannot be null."));
        }
        return freeze.FreezeStorage(
            pointer,
            writer.Complete(),
            XFileBlockType.LARGE,
            alignment: 4,
            operations: null,
            fieldPath);
    }

    private static LinkStorageTarget FreezeVisualTable(
        IReadOnlyList<FxElemDefVisuals> visuals,
        FxElemType elemType,
        XPointerReference pointer,
        string fieldPath,
        LinkAssetFreezeScope freeze)
    {
        FrozenVisual[] frozen = visuals
            .Select((visual, index) => FrozenVisual.Freeze(
                visual ?? throw new InvalidDataException(
                    $"{fieldPath}[{index}] cannot be null."),
                elemType,
                $"{fieldPath}[{index}]",
                freeze))
            .ToArray();
        var writer = new LinkTemplateWriter(
            checked(frozen.Length * FxElemDefVisuals.SerializedSize));
        foreach (FrozenVisual visual in frozen)
            writer.WriteInt32(visual.TemplateWord);
        return freeze.FreezeStorage(
            pointer,
            writer.Complete(),
            XFileBlockType.LARGE,
            alignment: 4,
            (table, addend) =>
            {
                var operations = new List<LinkOperation>();
                for (int index = 0; index < frozen.Length; index++)
                {
                    frozen[index].AppendOperation(
                        table,
                        checked(addend + index * FxElemDefVisuals.SerializedSize),
                        operations);
                }
                return operations;
            },
            fieldPath);
    }

    private static LinkStorageTarget? FreezeMarkTable(
        IReadOnlyList<FxElemMarkVisuals> marks,
        XPointerReference pointer,
        string fieldPath,
        LinkAssetFreezeScope freeze)
    {
        if (marks.Count == 0 && pointer.Type == PointerType.Null)
            return null;
        FrozenMark[] frozen = marks
            .Select((mark, index) => FrozenMark.Freeze(
                mark ?? throw new InvalidDataException(
                    $"{fieldPath}[{index}] cannot be null."),
                $"{fieldPath}[{index}]"))
            .ToArray();
        var writer = new LinkTemplateWriter(
            checked(frozen.Length * FxElemMarkVisuals.SerializedSize));
        writer.Skip(checked(frozen.Length * FxElemMarkVisuals.SerializedSize));
        return freeze.FreezeStorage(
            pointer,
            writer.Complete(),
            XFileBlockType.LARGE,
            alignment: 4,
            (table, addend) =>
            {
                var operations = new List<LinkOperation>();
                for (int index = 0; index < frozen.Length; index++)
                {
                    frozen[index].AppendOperations(
                        table,
                        checked(addend + index * FxElemMarkVisuals.SerializedSize),
                        operations);
                }
                return operations;
            },
            fieldPath);
    }

    private static LinkStorageTarget FreezeExtended(
        FxElemExtendedDef extended,
        FxElemType elemType,
        XPointerReference pointer,
        string fieldPath,
        LinkAssetFreezeScope freeze)
    {
        FxElemExtendedDefKind expected = elemType switch
        {
            FxElemType.Trail => FxElemExtendedDefKind.Trail,
            FxElemType.SparkFountain => FxElemExtendedDefKind.SparkFountain,
            _ => FxElemExtendedDefKind.DefaultBytePayload
        };
        if (extended.Kind != expected)
        {
            throw new InvalidDataException(
                $"{fieldPath} requires {expected} for element type {elemType}.");
        }
        return expected switch
        {
            FxElemExtendedDefKind.Trail => FreezeTrail(
                extended.TrailDef ?? throw new InvalidDataException(
                    $"{fieldPath}.TrailDef cannot be null."),
                extended,
                pointer,
                fieldPath,
                freeze),
            FxElemExtendedDefKind.SparkFountain => FreezeSpark(
                extended.SparkFountainDef ?? throw new InvalidDataException(
                    $"{fieldPath}.SparkFountainDef cannot be null."),
                extended,
                pointer,
                fieldPath,
                freeze),
            FxElemExtendedDefKind.DefaultBytePayload => FreezeDefaultByte(
                extended.DefaultBytePayload ?? throw new InvalidDataException(
                    $"{fieldPath}.DefaultBytePayload cannot be null."),
                extended,
                pointer,
                fieldPath,
                freeze),
            _ => throw new InvalidDataException($"{fieldPath} has no concrete payload kind.")
        };
    }

    private static LinkStorageTarget FreezeTrail(
        FxTrailDef trail,
        FxElemExtendedDef owner,
        XPointerReference pointer,
        string fieldPath,
        LinkAssetFreezeScope freeze)
    {
        if (owner.SparkFountainDef is not null || owner.DefaultBytePayload is not null)
            throw new InvalidDataException($"{fieldPath} retains more than one payload arm.");
        IReadOnlyList<FxTrailVertex> vertices = trail.Verts ??
            throw new InvalidDataException($"{fieldPath}.TrailDef.Verts cannot be null.");
        IReadOnlyList<ushort> indices = trail.Inds ??
            throw new InvalidDataException($"{fieldPath}.TrailDef.Inds cannot be null.");
        if (trail.VertCount < 0 || trail.VertCount != vertices.Count ||
            trail.IndCount < 0 || trail.IndCount != indices.Count)
        {
            throw new InvalidDataException(
                $"{fieldPath}.TrailDef counts must equal their semantic arrays.");
        }
        if (indices.Any(index => index >= vertices.Count))
            throw new InvalidDataException($"{fieldPath}.TrailDef contains an out-of-range index.");
        LinkStorageTarget? vertexStorage = FreezeTrailVertices(
            vertices,
            trail.VertsPointer.Untyped,
            $"{fieldPath}.TrailDef.Verts",
            freeze);
        LinkStorageTarget? indexStorage = FreezeUInt16s(
            indices,
            trail.IndsPointer.Untyped,
            $"{fieldPath}.TrailDef.Inds",
            freeze);
        var writer = new LinkTemplateWriter(FxTrailDef.SerializedSize);
        writer.WriteInt32(trail.ScrollTimeMsec);
        writer.WriteInt32(trail.RepeatDist);
        writer.WriteSingle(trail.InvSplitDist);
        writer.WriteSingle(trail.InvSplitArcDist);
        writer.WriteSingle(trail.InvSplitTime);
        writer.WriteInt32(trail.VertCount);
        writer.Skip(sizeof(int));
        writer.WriteInt32(trail.IndCount);
        writer.Skip(sizeof(int));
        return freeze.FreezeStorage(
            pointer,
            writer.Complete(),
            XFileBlockType.LARGE,
            alignment: 4,
            (storage, addend) =>
            {
                var operations = new List<LinkOperation>();
                if (vertexStorage is not null)
                {
                    operations.Add(DirectOperation(
                        storage,
                        checked(addend + 0x18),
                        vertexStorage.Value,
                        $"{fieldPath}.TrailDef.Verts"));
                }
                if (indexStorage is not null)
                {
                    operations.Add(DirectOperation(
                        storage,
                        checked(addend + 0x20),
                        indexStorage.Value,
                        $"{fieldPath}.TrailDef.Inds"));
                }
                return operations;
            },
            fieldPath);
    }

    private static LinkStorageTarget? FreezeTrailVertices(
        IReadOnlyList<FxTrailVertex> vertices,
        XPointerReference pointer,
        string fieldPath,
        LinkAssetFreezeScope freeze)
    {
        if (vertices.Count == 0 && pointer.Type == PointerType.Null)
            return null;
        var writer = new LinkTemplateWriter(
            checked(vertices.Count * FxTrailDef.VertexSerializedSize));
        for (int index = 0; index < vertices.Count; index++)
        {
            FxTrailVertex vertex = vertices[index] ??
                throw new InvalidDataException($"{fieldPath}[{index}] cannot be null.");
            writer.WriteSingle(vertex.Pos0);
            writer.WriteSingle(vertex.Pos1);
            writer.WriteSingle(vertex.Normal0);
            writer.WriteSingle(vertex.Normal1);
            writer.WriteSingle(vertex.TexCoord);
        }
        return freeze.FreezeStorage(
            pointer,
            writer.Complete(),
            XFileBlockType.LARGE,
            alignment: 4,
            operations: null,
            fieldPath);
    }

    private static LinkStorageTarget? FreezeUInt16s(
        IReadOnlyList<ushort> values,
        XPointerReference pointer,
        string fieldPath,
        LinkAssetFreezeScope freeze)
    {
        if (values.Count == 0 && pointer.Type == PointerType.Null)
            return null;
        var writer = new LinkTemplateWriter(checked(values.Count * sizeof(ushort)));
        foreach (ushort value in values)
            writer.WriteUInt16(value);
        return freeze.FreezeStorage(
            pointer,
            writer.Complete(),
            XFileBlockType.LARGE,
            alignment: 2,
            operations: null,
            fieldPath);
    }

    private static LinkStorageTarget FreezeSpark(
        FxSparkFountainDef spark,
        FxElemExtendedDef owner,
        XPointerReference pointer,
        string fieldPath,
        LinkAssetFreezeScope freeze)
    {
        if (owner.TrailDef is not null || owner.DefaultBytePayload is not null)
            throw new InvalidDataException($"{fieldPath} retains more than one payload arm.");
        var writer = new LinkTemplateWriter(FxSparkFountainDef.SerializedSize);
        writer.WriteSingle(spark.Gravity);
        writer.WriteSingle(spark.BounceFrac);
        writer.WriteSingle(spark.BounceRand);
        writer.WriteSingle(spark.SparkSpacing);
        writer.WriteSingle(spark.SparkLength);
        writer.WriteInt32(spark.SparkCount);
        writer.WriteSingle(spark.LoopTime);
        writer.WriteSingle(spark.VelMin);
        writer.WriteSingle(spark.VelMax);
        writer.WriteSingle(spark.VelConeFrac);
        writer.WriteSingle(spark.RestSpeed);
        writer.WriteSingle(spark.BoostTime);
        writer.WriteSingle(spark.BoostFactor);
        return freeze.FreezeStorage(
            pointer,
            writer.Complete(),
            XFileBlockType.LARGE,
            alignment: 4,
            operations: null,
            fieldPath);
    }

    private static LinkStorageTarget FreezeDefaultByte(
        byte value,
        FxElemExtendedDef owner,
        XPointerReference pointer,
        string fieldPath,
        LinkAssetFreezeScope freeze)
    {
        if (owner.TrailDef is not null || owner.SparkFountainDef is not null)
            throw new InvalidDataException($"{fieldPath} retains more than one payload arm.");
        return freeze.FreezeStorage(
            pointer,
            [value],
            XFileBlockType.LARGE,
            alignment: 1,
            operations: null,
            fieldPath);
    }

    private static LinkStorageSymbol? FreezeOptionalXString(
        LinkAssetFreezeScope freeze,
        string? value,
        XPointerReference pointer,
        string fieldPath)
    {
        if (value is null)
        {
            if (pointer.Type != PointerType.Null)
            {
                throw new NotSupportedException(
                    $"{fieldPath} retains a non-null XString pointer without semantic text.");
            }
            return null;
        }
        return freeze.FreezeRequiredXString(value, pointer, fieldPath);
    }

    private static FrozenFxReference FreezeFxReference(
        FxEffectDefRef reference,
        string fieldPath,
        LinkAssetFreezeScope freeze)
    {
        LinkStorageSymbol? text = FreezeOptionalXString(
            freeze,
            reference.Name,
            reference.NamePointer.Untyped,
            fieldPath);
        if (reference.Name is null)
            return new FrozenFxReference(text, null);

        AssetKey key;
        try
        {
            key = AssetKey.FromWireName(
                CanonicalAssetFamily.FromSerializedType(XAssetType.Fx),
                reference.Name);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException(
                $"{fieldPath} has an invalid Fx provider name.",
                exception);
        }
        return new FrozenFxReference(
            text,
            new AssetDependency(key, XAssetType.Fx, fieldPath));
    }


    private readonly record struct FrozenFxReference(
        LinkStorageSymbol? Text,
        AssetDependency? Dependency);

    private static void WriteVelocityFrame(
        LinkTemplateWriter writer,
        FxElemVelStateInFrame frame,
        string fieldPath)
    {
        WriteVec3Range(
            writer,
            frame.Velocity ?? throw new InvalidDataException(
                $"{fieldPath}.Velocity cannot be null."));
        WriteVec3Range(
            writer,
            frame.TotalDelta ?? throw new InvalidDataException(
                $"{fieldPath}.TotalDelta cannot be null."));
    }

    private static void WriteVec3Range(
        LinkTemplateWriter writer,
        FxElemVec3Range range)
    {
        WriteVec3(writer, range.Base);
        WriteVec3(writer, range.Amplitude);
    }

    private static void WriteVisualState(
        LinkTemplateWriter writer,
        FxElemVisualState state)
    {
        FxElemColor color = state.Color ??
            throw new InvalidDataException("Fx visual-state color cannot be null.");
        writer.WriteByte(color.R);
        writer.WriteByte(color.G);
        writer.WriteByte(color.B);
        writer.WriteByte(color.A);
        writer.WriteSingle(state.RotationDelta);
        writer.WriteSingle(state.RotationTotal);
        writer.WriteSingle(state.Size0);
        writer.WriteSingle(state.Size1);
        writer.WriteSingle(state.Scale);
    }

    private static void WriteRange(LinkTemplateWriter writer, FxFloatRange range)
    {
        writer.WriteSingle(range.Base);
        writer.WriteSingle(range.Amplitude);
    }

    private static void WriteIntRange(LinkTemplateWriter writer, FxIntRange range)
    {
        writer.WriteInt32(range.Base);
        writer.WriteInt32(range.Amplitude);
    }

    private static void WriteVec3(LinkTemplateWriter writer, Vec3 value)
    {
        writer.WriteSingle(value.X);
        writer.WriteSingle(value.Y);
        writer.WriteSingle(value.Z);
    }


    private sealed class FrozenElement
    {
        private FrozenElement(
            byte[] template,
            LinkStorageTarget? velocitySamples,
            LinkStorageTarget? visualSamples,
            LinkStorageTarget? visualTable,
            FrozenVisual? inlineVisual,
            FrozenFxReference effectOnImpact,
            FrozenFxReference effectOnDeath,
            FrozenFxReference effectEmitted,
            LinkStorageTarget? extended,
            int index)
        {
            Template = template;
            VelocitySamples = velocitySamples;
            VisualSamples = visualSamples;
            VisualTable = visualTable;
            InlineVisual = inlineVisual;
            EffectOnImpact = effectOnImpact;
            EffectOnDeath = effectOnDeath;
            EffectEmitted = effectEmitted;
            Extended = extended;
            Index = index;
        }

        public byte[] Template { get; }
        private LinkStorageTarget? VelocitySamples { get; }
        private LinkStorageTarget? VisualSamples { get; }
        private LinkStorageTarget? VisualTable { get; }
        private FrozenVisual? InlineVisual { get; }
        private FrozenFxReference EffectOnImpact { get; }
        private FrozenFxReference EffectOnDeath { get; }
        private FrozenFxReference EffectEmitted { get; }
        private LinkStorageTarget? Extended { get; }
        private int Index { get; }

        public static FrozenElement Freeze(
            FxElemDef element,
            int index,
            LinkAssetFreezeScope freeze)
        {
            string path = $"Fx.ElemDefs[{index}]";
            if (!Enum.IsDefined(element.ElemType))
            {
                throw new InvalidDataException(
                    $"{path}.ElemType has unsupported value {(byte)element.ElemType}.");
            }
            FxFloatRange[] spawnOrigin = FreezeRanges(
                element.SpawnOrigin,
                3,
                $"{path}.SpawnOrigin");
            FxFloatRange[] spawnAngles = FreezeRanges(
                element.SpawnAngles,
                3,
                $"{path}.SpawnAngles");
            FxFloatRange[] angularVelocity = FreezeRanges(
                element.AngularVelocity,
                3,
                $"{path}.AngularVelocity");
            IReadOnlyList<FxElemVelStateSample> velocities = element.VelSamples ??
                throw new InvalidDataException($"{path}.VelSamples cannot be null.");
            IReadOnlyList<FxElemVisStateSample> visualSamples = element.VisSamples ??
                throw new InvalidDataException($"{path}.VisSamples cannot be null.");
            ValidateSampleCount(
                velocities.Count,
                element.VelIntervalCount,
                $"{path}.VelSamples");
            ValidateSampleCount(
                visualSamples.Count,
                element.VisStateIntervalCount,
                $"{path}.VisSamples");
            LinkStorageTarget? velocityStorage = FreezeVelocitySamples(
                velocities,
                element.VelSamplesPointer.Untyped,
                $"{path}.VelSamples",
                freeze);
            LinkStorageTarget? visualSampleStorage = FreezeVisualSamples(
                visualSamples,
                element.VisSamplesPointer.Untyped,
                $"{path}.VisSamples",
                freeze);

            IReadOnlyList<FxElemDefVisuals> visualArray = element.VisualArray ??
                throw new InvalidDataException($"{path}.VisualArray cannot be null.");
            IReadOnlyList<FxElemMarkVisuals> markArray = element.MarkVisualArray ??
                throw new InvalidDataException($"{path}.MarkVisualArray cannot be null.");
            LinkStorageTarget? visualTable = null;
            FrozenVisual? inlineVisual = null;
            if (element.ElemType == FxElemType.Decal)
            {
                if (markArray.Count != element.VisualCount || visualArray.Count != 0 ||
                    element.Visuals?.Visual is not null ||
                    (element.VisualArrayPointer ?? default).Type != PointerType.Null)
                {
                    throw new InvalidDataException(
                        $"{path} Decal visuals must contain VisualCount marks and no regular visual arm.");
                }
                visualTable = FreezeMarkTable(
                    markArray,
                    (element.MarkVisualArrayPointer ?? default).Untyped,
                    $"{path}.MarkVisualArray",
                    freeze);
            }
            else if (element.VisualCount > 1)
            {
                if (visualArray.Count != element.VisualCount || markArray.Count != 0 ||
                    element.Visuals?.Visual is not null ||
                    (element.MarkVisualArrayPointer ?? default).Type != PointerType.Null)
                {
                    throw new InvalidDataException(
                        $"{path} must contain VisualCount array visual arms only.");
                }
                visualTable = FreezeVisualTable(
                    visualArray,
                    element.ElemType,
                    (element.VisualArrayPointer ?? default).Untyped,
                    $"{path}.VisualArray",
                    freeze);
            }
            else
            {
                if (visualArray.Count != 0 || markArray.Count != 0 ||
                    (element.VisualArrayPointer ?? default).Type != PointerType.Null ||
                    (element.MarkVisualArrayPointer ?? default).Type != PointerType.Null)
                {
                    throw new InvalidDataException(
                        $"{path} must contain one inline visual arm only.");
                }
                FxElemDefVisuals visuals = element.Visuals ??
                    throw new InvalidDataException($"{path}.Visuals cannot be null.");
                inlineVisual = FrozenVisual.Freeze(
                    visuals,
                    element.ElemType,
                    $"{path}.Visuals",
                    freeze);
            }

            FxEffectDefRef impact = element.EffectOnImpact ??
                throw new InvalidDataException($"{path}.EffectOnImpact cannot be null.");
            FxEffectDefRef death = element.EffectOnDeath ??
                throw new InvalidDataException($"{path}.EffectOnDeath cannot be null.");
            FxEffectDefRef emitted = element.EffectEmitted ??
                throw new InvalidDataException($"{path}.EffectEmitted cannot be null.");
            FrozenFxReference impactName = FreezeFxReference(
                impact,
                $"{path}.EffectOnImpact",
                freeze);
            FrozenFxReference deathName = FreezeFxReference(
                death,
                $"{path}.EffectOnDeath",
                freeze);
            FrozenFxReference emittedName = FreezeFxReference(
                emitted,
                $"{path}.EffectEmitted",
                freeze);
            LinkStorageTarget? extended;
            if (element.Extended is null)
            {
                if (element.ExtendedPointer.Type != PointerType.Null)
                {
                    throw new NotSupportedException(
                        $"{path}.Extended retains direct storage without semantic data.");
                }
                extended = null;
            }
            else
            {
                extended = FreezeExtended(
                    element.Extended,
                    element.ElemType,
                    element.ExtendedPointer.Untyped,
                    $"{path}.Extended",
                    freeze);
            }

            FxSpawnDef spawn = element.Spawn ??
                throw new InvalidDataException($"{path}.Spawn cannot be null.");
            var writer = new LinkTemplateWriter(FxElemDef.SerializedSize);
            writer.WriteInt32(element.Flags);
            writer.WriteInt32(spawn.LoopingIntervalMsec);
            writer.WriteInt32(spawn.Count);
            WriteRange(writer, Required(element.SpawnRange, $"{path}.SpawnRange"));
            WriteRange(writer, Required(element.FadeInRange, $"{path}.FadeInRange"));
            WriteRange(writer, Required(element.FadeOutRange, $"{path}.FadeOutRange"));
            writer.WriteSingle(element.SpawnFrustumCullRadius);
            WriteIntRange(writer, Required(element.SpawnDelayMsec, $"{path}.SpawnDelayMsec"));
            WriteIntRange(writer, Required(element.LifeSpanMsec, $"{path}.LifeSpanMsec"));
            foreach (FxFloatRange range in spawnOrigin)
                WriteRange(writer, range);
            WriteRange(writer, Required(element.SpawnOffsetRadius, $"{path}.SpawnOffsetRadius"));
            WriteRange(writer, Required(element.SpawnOffsetHeight, $"{path}.SpawnOffsetHeight"));
            foreach (FxFloatRange range in spawnAngles)
                WriteRange(writer, range);
            foreach (FxFloatRange range in angularVelocity)
                WriteRange(writer, range);
            WriteRange(writer, Required(element.InitialRotation, $"{path}.InitialRotation"));
            WriteRange(writer, Required(element.Gravity, $"{path}.Gravity"));
            WriteRange(writer, Required(element.ReflectionFactor, $"{path}.ReflectionFactor"));
            FxElemAtlas atlas = element.Atlas ??
                throw new InvalidDataException($"{path}.Atlas cannot be null.");
            writer.WriteByte(atlas.Behavior);
            writer.WriteByte(atlas.Index);
            writer.WriteByte(atlas.Fps);
            writer.WriteByte(atlas.LoopCount);
            writer.WriteByte(atlas.ColIndexBits);
            writer.WriteByte(atlas.RowIndexBits);
            writer.WriteUInt16(unchecked((ushort)atlas.EntryCount));
            writer.WriteByte((byte)element.ElemType);
            writer.WriteByte(element.VisualCount);
            writer.WriteByte(element.VelIntervalCount);
            writer.WriteByte(element.VisStateIntervalCount);
            writer.Skip(sizeof(int));
            writer.Skip(sizeof(int));
            writer.WriteInt32(inlineVisual?.TemplateWord ?? 0);
            Bounds bounds = element.CollBounds ??
                throw new InvalidDataException($"{path}.CollBounds cannot be null.");
            WriteVec3(writer, bounds.MidPoint);
            WriteVec3(writer, bounds.HalfSize);
            writer.Skip(sizeof(int) * 3);
            WriteRange(writer, Required(element.EmitDist, $"{path}.EmitDist"));
            WriteRange(writer, Required(element.EmitDistVariance, $"{path}.EmitDistVariance"));
            writer.Skip(sizeof(int));
            writer.WriteByte(element.SortOrder);
            writer.WriteByte(element.LightingFrac);
            writer.WriteByte(element.UseItemClip);
            writer.WriteByte(element.FadeInfo);
            return new FrozenElement(
                writer.Complete(),
                velocityStorage,
                visualSampleStorage,
                visualTable,
                inlineVisual,
                impactName,
                deathName,
                emittedName,
                extended,
                index);
        }

        public void AppendOperations(
            LinkStorageSymbol table,
            int baseOffset,
            ICollection<LinkOperation> operations)
        {
            string path = $"Fx.ElemDefs[{Index}]";
            if (VelocitySamples is { } velocitySamples)
            {
                operations.Add(DirectOperation(
                    table,
                    checked(baseOffset + 0xb4),
                    velocitySamples,
                    $"{path}.VelSamples"));
            }
            if (VisualSamples is { } visualSamples)
            {
                operations.Add(DirectOperation(
                    table,
                    checked(baseOffset + 0xb8),
                    visualSamples,
                    $"{path}.VisSamples"));
            }
            if (VisualTable is { } visualTable)
            {
                operations.Add(DirectOperation(
                    table,
                    checked(baseOffset + 0xbc),
                    visualTable,
                    $"{path}.Visuals"));
            }
            else
            {
                InlineVisual?.AppendOperation(
                    table,
                    checked(baseOffset + 0xbc),
                    operations);
            }
            AddFxReference(EffectOnImpact, 0xd8, "EffectOnImpact");
            AddFxReference(EffectOnDeath, 0xdc, "EffectOnDeath");
            AddFxReference(EffectEmitted, 0xe0, "EffectEmitted");
            if (Extended is { } extended)
            {
                operations.Add(DirectOperation(
                    table,
                    checked(baseOffset + 0xf4),
                    extended,
                    $"{path}.Extended"));
            }

            void AddFxReference(
                FrozenFxReference value,
                int relativeOffset,
                string field)
            {
                if (value.Text is not null)
                {
                    operations.Add(XStringOperation(
                        table,
                        checked(baseOffset + relativeOffset),
                        value.Text,
                        $"{path}.{field}"));
                }
                if (value.Dependency is { } dependency)
                    operations.Add(new DependencyOnlyLinkOperation(dependency));
            }
        }

        private static void ValidateSampleCount(
            int sampleCount,
            byte intervalCount,
            string fieldPath)
        {
            bool valid = sampleCount == 0
                ? intervalCount == 0
                : sampleCount == intervalCount + 1;
            if (!valid)
            {
                throw new InvalidDataException(
                    $"{fieldPath} must be absent with zero intervals or contain interval count plus one rows.");
            }
        }

        private static FxFloatRange[] FreezeRanges(
            IReadOnlyList<FxFloatRange> source,
            int expectedCount,
            string fieldPath)
        {
            ArgumentNullException.ThrowIfNull(source);
            if (source.Count != expectedCount)
                throw new InvalidDataException($"{fieldPath} requires exactly {expectedCount} ranges.");
            return source
                .Select((range, index) => range ?? throw new InvalidDataException(
                    $"{fieldPath}[{index}] cannot be null."))
                .ToArray();
        }

        private static T Required<T>(T? value, string fieldPath)
            where T : class => value ?? throw new InvalidDataException(
                $"{fieldPath} cannot be null.");
    }

    private sealed class FrozenVisual
    {
        private FrozenVisual(
            int templateWord,
            AssetDependency? dependency,
            LinkStorageSymbol? text,
            AssetDependency? closureDependency,
            string fieldPath)
        {
            TemplateWord = templateWord;
            Dependency = dependency;
            Text = text;
            ClosureDependency = closureDependency;
            FieldPath = fieldPath;
        }

        public int TemplateWord { get; }
        private AssetDependency? Dependency { get; }
        private LinkStorageSymbol? Text { get; }
        private AssetDependency? ClosureDependency { get; }
        private string FieldPath { get; }

        public static FrozenVisual Freeze(
            FxElemDefVisuals visuals,
            FxElemType elemType,
            string fieldPath,
            LinkAssetFreezeScope freeze)
        {
            FxElemVisual visual = visuals.Visual ??
                throw new InvalidDataException($"{fieldPath}.Visual cannot be null.");
            return elemType switch
            {
                FxElemType.Model when visual is FxModelVisual model => new FrozenVisual(
                    0,
                    FreezeProviderDependency(
                        model.ModelPointer.Untyped,
                        model.Model,
                        XAssetType.XModel,
                        $"{fieldPath}.Model"),
                    null,
                    null,
                    $"{fieldPath}.Model"),
                FxElemType.OmniLight or FxElemType.SpotLight
                    when visual is FxNoChildVisual noChild => new FrozenVisual(
                        noChild.Reserved,
                        null,
                        null,
                        null,
                        fieldPath),
                FxElemType.Sound when visual is FxSoundVisual sound => new FrozenVisual(
                    0,
                    null,
                    FreezeOptionalXString(
                        freeze,
                        sound.SoundName,
                        sound.SoundNamePointer.Untyped,
                        $"{fieldPath}.SoundName"),
                    null,
                    $"{fieldPath}.SoundName"),
                FxElemType.Runner when visual is FxEffectVisual effect =>
                    FreezeRunner(effect, fieldPath, freeze),
                FxElemType.Model => throw WrongArm(fieldPath, nameof(FxModelVisual)),
                FxElemType.OmniLight or FxElemType.SpotLight =>
                    throw WrongArm(fieldPath, nameof(FxNoChildVisual)),
                FxElemType.Sound => throw WrongArm(fieldPath, nameof(FxSoundVisual)),
                FxElemType.Runner => throw WrongArm(fieldPath, nameof(FxEffectVisual)),
                _ when visual is FxMaterialVisual material => new FrozenVisual(
                    0,
                    FreezeProviderDependency(
                        material.MaterialPointer.Untyped,
                        material.Material,
                        XAssetType.Material,
                        $"{fieldPath}.Material"),
                    null,
                    null,
                    $"{fieldPath}.Material"),
                _ => throw WrongArm(fieldPath, nameof(FxMaterialVisual))
            };
        }

        public void AppendOperation(
            LinkStorageSymbol owner,
            int offset,
            ICollection<LinkOperation> operations)
        {
            if (Dependency is { } dependency)
                operations.Add(ProviderOperation(owner, offset, dependency));
            else if (Text is not null)
                operations.Add(XStringOperation(owner, offset, Text, FieldPath));
            if (ClosureDependency is { } closure)
                operations.Add(new DependencyOnlyLinkOperation(closure));
        }

        private static FrozenVisual FreezeRunner(
            FxEffectVisual effect,
            string fieldPath,
            LinkAssetFreezeScope freeze)
        {
            FxEffectDefRef reference = effect.EffectDef ??
                throw new InvalidDataException($"{fieldPath}.EffectDef cannot be null.");
            FrozenFxReference frozen = FreezeFxReference(
                reference,
                $"{fieldPath}.EffectDef",
                freeze);
            return new FrozenVisual(
                0,
                null,
                frozen.Text,
                frozen.Dependency,
                $"{fieldPath}.EffectDef");
        }

        private static InvalidDataException WrongArm(
            string fieldPath,
            string expected) => new(
                $"{fieldPath} requires a {expected} visual arm.");
    }

    private sealed class FrozenMark
    {
        private FrozenMark(
            AssetDependency? material0,
            AssetDependency? material1)
        {
            Material0 = material0;
            Material1 = material1;
        }

        private AssetDependency? Material0 { get; }
        private AssetDependency? Material1 { get; }

        public static FrozenMark Freeze(FxElemMarkVisuals mark, string fieldPath) =>
            new(
                FreezeProviderDependency(
                    mark.Material0Pointer.Untyped,
                    mark.Material0,
                    XAssetType.Material,
                    $"{fieldPath}.Material0"),
                FreezeProviderDependency(
                    mark.Material1Pointer.Untyped,
                    mark.Material1,
                    XAssetType.Material,
                    $"{fieldPath}.Material1"));

        public void AppendOperations(
            LinkStorageSymbol table,
            int baseOffset,
            ICollection<LinkOperation> operations)
        {
            if (Material0 is { } material0)
                operations.Add(ProviderOperation(table, baseOffset, material0));
            if (Material1 is { } material1)
            {
                operations.Add(ProviderOperation(
                    table,
                    checked(baseOffset + sizeof(int)),
                    material1));
            }
        }
    }
}

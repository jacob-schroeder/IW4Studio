using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Emission;

namespace IW4.FastFiles.Emitters.Assets;

/// <summary>Emitter for the FxEffectDef graph. Visual and
/// extended payload discriminants are represented explicitly; external model
/// and material roots are never promoted into the owning effect.</summary>
public sealed class FxEffectDefBodyEmitter : IXAssetBodyEmitter
{
    private const byte Trail = 3, SparkFountain = 6, Model = 7, OmniLight = 8, SpotLight = 9, Sound = 10, Decal = 11, Runner = 12;
    public XAssetType AssetType => XAssetType.Fx;

    public IReadOnlyList<EmissionError> Validate(IXAssetBuildData buildData, int? rowIndex = null)
    {
        var errors = AssetBodyEmitterHelpers.ValidateIdentity(buildData, AssetType, rowIndex);
        if (buildData is not IFxEffectDefBuildData data) { errors.Add(Error("body", "Fx build data does not implement IFxEffectDefBuildData.", rowIndex)); return errors; }
        CheckString(data.Name, "name", errors, rowIndex);
        int count;
        try { count = checked(data.ElemDefCountLooping + data.ElemDefCountOneShot + data.ElemDefCountEmission); }
        catch (OverflowException) { errors.Add(Error("elementCounts", "Element count sum overflows Int32.", rowIndex)); return errors; }
        if (count != data.Elements.Count) errors.Add(Error("elements", "Element array length must equal the three serialized count fields.", rowIndex));
        for (int i = 0; i < data.Elements.Count; i++) CheckElement(data.Elements[i], i, errors, rowIndex);
        return errors;
    }

    public AssetBodyEmission Plan(IXAssetBuildData buildData, EmissionPlan plan, int? rowIndex = null)
    {
        ArgumentNullException.ThrowIfNull(plan); AssetBodyEmitterHelpers.RequireNoDiagnostics(Validate(buildData, rowIndex));
        IFxEffectDefBuildData data = (IFxEffectDefBuildData)buildData;
        var all = new List<EmissionBlockSegment>(); var source = new List<EmissionBlockSegment>();
        plan.Push(XFileBlockType.TEMP); EmissionAddress root = plan.Allocate(0x20, 4); plan.Push(XFileBlockType.LARGE);
        int beforeName = all.Count; PlannedString? name = AssetBodyEmitterHelpers.PlanString(data.Name, plan, all, plan.StringAliases); int afterName = all.Count;
        EmissionBlockSegment? table = null; var elementSources = new List<EmissionBlockSegment>();
        if (data.Elements.Count != 0)
        {
            EmissionAddress tableAddress = plan.Allocate(checked(data.Elements.Count * 0xfc), 4);
            ElementPlan[] elements = data.Elements
                .Select((value, index) => PlanElement(
                    value,
                    new EmissionAddress(
                        tableAddress.Block,
                        checked(tableAddress.Offset + index * 0xfc)),
                    plan,
                    all))
                .ToArray();
            table = new EmissionBlockSegment(tableAddress, BuildElementTable(elements)); all.Add(table);
            foreach (ElementPlan element in elements) elementSources.AddRange(element.Source);
        }
        plan.Pop(XFileBlockType.LARGE); plan.Pop(XFileBlockType.TEMP);
        var writer = new XSourceWriter(); writer.WriteInt32(AssetBodyEmitterHelpers.SourcePointer(name)); writer.WriteInt32(data.Flags); writer.WriteInt32(data.TotalSize); writer.WriteInt32(data.MsecLoopingLife); writer.WriteInt32(data.ElemDefCountLooping); writer.WriteInt32(data.ElemDefCountOneShot); writer.WriteInt32(data.ElemDefCountEmission); writer.WriteInt32(Pointer(table));
        var rootSegment = new EmissionBlockSegment(root, writer.ToArray()); all.Add(rootSegment); source.Add(rootSegment); source.AddRange(all.Skip(beforeName).Take(afterName - beforeName)); Add(source, table); source.AddRange(elementSources);
        return new AssetBodyEmission(AssetType, root, all, source);
    }

    private static ElementPlan PlanElement(
        FxElementBuildData data,
        EmissionAddress elementRoot,
        EmissionPlan plan,
        List<EmissionBlockSegment> all)
    {
        EmissionBlockSegment? vel = PlanVelocity(data.VelocitySamples, plan, all);
        EmissionBlockSegment? vis = PlanVisualSamples(data.VisualSamples, plan, all);
        PayloadPlan visuals = PlanVisualPayload(
            data,
            new EmissionAddress(
                elementRoot.Block,
                checked(elementRoot.Offset + 0xbc)),
            plan,
            all);
        StringPlan impact = PlanLink(data.EffectOnImpactReference, plan, all);
        StringPlan death = PlanLink(data.EffectOnDeathReference, plan, all);
        StringPlan emitted = PlanLink(data.EffectEmittedReference, plan, all);
        ExtendedPlan? extended = data.Extended is null ? null : PlanExtended(data.Extended, plan, all);
        var writer = new XSourceWriter(); WriteElementRoot(writer, data, vel, vis, visuals.Raw, impact.Raw, death.Raw, emitted.Raw, extended is null ? 0 : -1);
        var source = new List<EmissionBlockSegment>();
        Add(source, vel);
        Add(source, vis);
        source.AddRange(visuals.Source);
        source.AddRange(impact.Source);
        source.AddRange(death.Source);
        source.AddRange(emitted.Source);
        source.AddRange(extended?.Source ?? []);
        return new ElementPlan(writer.ToArray(), source);
    }

    private static byte[] BuildElementTable(IReadOnlyList<ElementPlan> values)
    {
        var writer = new XSourceWriter(); foreach (ElementPlan value in values) writer.WriteBytes(value.Root); return writer.ToArray();
    }

    private static void WriteElementRoot(XSourceWriter writer, FxElementBuildData d, EmissionBlockSegment? vel, EmissionBlockSegment? vis, int visualRaw, int impactRaw, int deathRaw, int emittedRaw, int extendedRaw)
    {
        writer.WriteInt32(d.Flags); writer.WriteInt32(d.Spawn.LoopingIntervalMsec); writer.WriteInt32(d.Spawn.Count); Range(writer, d.SpawnRange); Range(writer, d.FadeInRange); Range(writer, d.FadeOutRange); writer.WriteSingle(d.SpawnFrustumCullRadius); IntRange(writer, d.SpawnDelayMsec); IntRange(writer, d.LifeSpanMsec); foreach (FxFloatRangeBuildData value in d.SpawnOrigin) Range(writer, value); Range(writer, d.SpawnOffsetRadius); Range(writer, d.SpawnOffsetHeight); foreach (FxFloatRangeBuildData value in d.SpawnAngles) Range(writer, value); foreach (FxFloatRangeBuildData value in d.AngularVelocity) Range(writer, value); Range(writer, d.InitialRotation); Range(writer, d.Gravity); Range(writer, d.ReflectionFactor);
        writer.WriteByte(d.Atlas.Behavior); writer.WriteByte(d.Atlas.Index); writer.WriteByte(d.Atlas.Fps); writer.WriteByte(d.Atlas.LoopCount); writer.WriteByte(d.Atlas.ColIndexBits); writer.WriteByte(d.Atlas.RowIndexBits); writer.WriteInt16(d.Atlas.EntryCount);
        writer.WriteByte(d.ElemType); writer.WriteByte(d.VisualCount); writer.WriteByte(d.VelIntervalCount); writer.WriteByte(d.VisStateIntervalCount); writer.WriteInt32(Pointer(vel)); writer.WriteInt32(Pointer(vis)); writer.WriteInt32(visualRaw); Vec(writer, d.CollBounds.MidPoint); Vec(writer, d.CollBounds.HalfSize); writer.WriteInt32(impactRaw); writer.WriteInt32(deathRaw); writer.WriteInt32(emittedRaw); Range(writer, d.EmitDist); Range(writer, d.EmitDistVariance); writer.WriteInt32(extendedRaw); writer.WriteByte(d.SortOrder); writer.WriteByte(d.LightingFrac); writer.WriteByte(d.UseItemClip); writer.WriteByte(d.FadeInfo);
        if (writer.Position != 0xfc) throw new InvalidDataException($"FxElemDef serializer wrote 0x{writer.Position:X} bytes instead of 0xFC.");
    }

    private static EmissionBlockSegment? PlanVelocity(IReadOnlyList<FxVelocitySampleBuildData> values, EmissionPlan plan, List<EmissionBlockSegment> all)
    {
        if (values.Count == 0)
            return null;
        var writer = new XSourceWriter(); foreach (FxVelocitySampleBuildData value in values) { VelocityFrame(writer, value.Local); VelocityFrame(writer, value.World); } var segment = new EmissionBlockSegment(plan.Allocate(checked(values.Count * 0x60), 4), writer.ToArray()); all.Add(segment); return segment;
    }
    private static EmissionBlockSegment? PlanVisualSamples(IReadOnlyList<FxVisualStateSampleBuildData> values, EmissionPlan plan, List<EmissionBlockSegment> all)
    {
        if (values.Count == 0)
            return null;
        var writer = new XSourceWriter(); foreach (FxVisualStateSampleBuildData value in values) { VisualState(writer, value.Base); VisualState(writer, value.Amplitude); } var segment = new EmissionBlockSegment(plan.Allocate(checked(values.Count * 0x30), 4), writer.ToArray()); all.Add(segment); return segment;
    }
    private static PayloadPlan PlanVisualPayload(
        FxElementBuildData data,
        EmissionAddress inlineOwnerCell,
        EmissionPlan plan,
        List<EmissionBlockSegment> all)
    {
        if (data.ElemType == Decal)
        {
            if (data.MarkVisuals.Count == 0) return new PayloadPlan(0, []);
            EmissionAddress address = plan.Allocate(checked(data.MarkVisuals.Count * 8), 4); var children = new List<EmissionBlockSegment>(); var writer = new XSourceWriter();
            for (int index = 0; index < data.MarkVisuals.Count; index++)
            {
                FxMarkVisualBuildData value = data.MarkVisuals[index];
                EmissionAddress material0Cell = new(
                    address.Block,
                    checked(address.Offset + index * 8));
                EmissionAddress material1Cell = new(
                    address.Block,
                    checked(address.Offset + index * 8 + 4));
                VisualPlan material0 = PlanMaterial(
                    value.Material0Link,
                    value.Material0Reference,
                    material0Cell,
                    plan,
                    all,
                    "Fx.MarkVisual.Material0");
                VisualPlan material1 = PlanMaterial(
                    value.Material1Link,
                    value.Material1Reference,
                    material1Cell,
                    plan,
                    all,
                    "Fx.MarkVisual.Material1");
                writer.WriteInt32(material0.Raw);
                writer.WriteInt32(material1.Raw);
                children.AddRange(material0.Source);
                children.AddRange(material1.Source);
            }
            var table = new EmissionBlockSegment(address, writer.ToArray()); all.Add(table); return new PayloadPlan(-1, [table, .. children]);
        }
        if (data.VisualCount > 1)
        {
            EmissionAddress address = plan.Allocate(checked(data.Visuals.Count * 4), 4); var children = new List<EmissionBlockSegment>(); var writer = new XSourceWriter();
            for (int index = 0; index < data.Visuals.Count; index++)
            {
                VisualPlan child = PlanVisual(
                    data.Visuals[index],
                    new EmissionAddress(
                        address.Block,
                        checked(address.Offset + index * 4)),
                    plan,
                    all);
                writer.WriteInt32(child.Raw);
                children.AddRange(child.Source);
            }
            var table = new EmissionBlockSegment(address, writer.ToArray()); all.Add(table); return new PayloadPlan(-1, [table, .. children]);
        }
        VisualPlan inline = PlanVisual(
            data.Visuals[0],
            inlineOwnerCell,
            plan,
            all);
        return new PayloadPlan(inline.Raw, inline.Source);
    }
    private static VisualPlan PlanVisual(
        FxVisualBuildData data,
        EmissionAddress ownerCell,
        EmissionPlan plan,
        List<EmissionBlockSegment> all) => data.Kind switch
    {
        FxVisualBuildKind.Material => PlanMaterial(
            data.MaterialLink,
            data.MaterialReference,
            ownerCell,
            plan,
            all,
            "Fx.Visual.Material"),
        FxVisualBuildKind.Model => PlanModel(
            data.ModelLink,
            data.ModelReference,
            ownerCell,
            plan,
            all),
        FxVisualBuildKind.Sound => LinkVisual(data.SoundReference, plan, all),
        FxVisualBuildKind.Effect => LinkVisual(data.EffectReference, plan, all),
        FxVisualBuildKind.NoChild => new(data.Reserved, []),
        _ => throw new InvalidDataException("Unsupported Fx visual arm.")
    };
    private static VisualPlan PlanMaterial(
        NestedXAssetBuildLink? link,
        SymbolicXAssetReference? reference,
        EmissionAddress ownerCell,
        EmissionPlan plan,
        List<EmissionBlockSegment> all,
        string owner)
    {
        if (link is { } nested)
        {
            NestedXAssetPlan child = NestedXAssetEmission.Plan(
                nested,
                plan,
                all,
                ownerCell,
                owner);
            return new VisualPlan(child.PointerRaw, child.Source);
        }
        return reference is null
            ? new VisualPlan(0, [])
            : new VisualPlan(
                -1,
                PlanExternal(
                    reference,
                    XAssetType.Material,
                    0xa8,
                    plan,
                    all).SourceSegments);
    }
    private static VisualPlan PlanModel(
        NestedXAssetBuildLink? link,
        SymbolicXAssetReference? reference,
        EmissionAddress ownerCell,
        EmissionPlan plan,
        List<EmissionBlockSegment> all)
    {
        if (link is { } nested)
        {
            NestedXAssetPlan child = NestedXAssetEmission.Plan(
                nested,
                plan,
                all,
                ownerCell,
                "Fx.Visual.XModel");
            return new VisualPlan(child.PointerRaw, child.Source);
        }
        return reference is null
            ? new VisualPlan(0, [])
            : new VisualPlan(
                -1,
                PlanExternal(
                    reference,
                    XAssetType.XModel,
                    0x120,
                    plan,
                    all).SourceSegments);
    }
    private static VisualPlan LinkVisual(SymbolicXAssetReference? value, EmissionPlan plan, List<EmissionBlockSegment> all) { StringPlan planned = PlanLink(value, plan, all); return new(planned.Raw, planned.Source); }
    private static StringPlan PlanLink(SymbolicXAssetReference? reference, EmissionPlan plan, List<EmissionBlockSegment> all)
    {
        PlannedString? value = AssetBodyEmitterHelpers.PlanString(reference?.OriginalSerializedName, plan, all, plan.StringAliases);
        return new StringPlan(AssetBodyEmitterHelpers.SourcePointer(value), value is { IsExistingMaterialization: false, Address: var address } ? [all.Single(segment => segment.Address == address)] : []);
    }
    private static ExtendedPlan PlanExtended(FxExtendedBuildData data, EmissionPlan plan, List<EmissionBlockSegment> all)
    {
        return data.Kind switch
        {
            FxExtendedBuildKind.Trail => PlanTrail(data.Trail!, plan, all),
            FxExtendedBuildKind.SparkFountain => PlanSpark(data.SparkFountain!, plan, all),
            FxExtendedBuildKind.DefaultBytePayload => PlanDefault(data.DefaultBytePayload, plan, all),
            _ => throw new InvalidDataException("A non-null Fx extended payload must have a concrete kind.")
        };
    }
    private static ExtendedPlan PlanTrail(FxTrailBuildData data, EmissionPlan plan, List<EmissionBlockSegment> all)
    {
        EmissionAddress root = plan.Allocate(0x24, 4); EmissionBlockSegment? vertices = null, indices = null;
        if (data.Vertices.Count != 0) { var writer = new XSourceWriter(); foreach (FxTrailVertexBuildData value in data.Vertices) { writer.WriteSingle(value.Pos0); writer.WriteSingle(value.Pos1); writer.WriteSingle(value.Normal0); writer.WriteSingle(value.Normal1); writer.WriteSingle(value.TexCoord); } vertices = new EmissionBlockSegment(plan.Allocate(checked(data.Vertices.Count * 0x14), 4), writer.ToArray()); all.Add(vertices); }
        if (data.Indices.Count != 0) { var writer = new XSourceWriter(); foreach (ushort value in data.Indices) writer.WriteUInt16(value); indices = new EmissionBlockSegment(plan.Allocate(checked(data.Indices.Count * 2), 2), writer.ToArray()); all.Add(indices); }
        var writerRoot = new XSourceWriter(); writerRoot.WriteInt32(data.ScrollTimeMsec); writerRoot.WriteInt32(data.RepeatDist); writerRoot.WriteSingle(data.InvSplitDist); writerRoot.WriteSingle(data.InvSplitArcDist); writerRoot.WriteSingle(data.InvSplitTime); writerRoot.WriteInt32(data.Vertices.Count); writerRoot.WriteInt32(Pointer(vertices)); writerRoot.WriteInt32(data.Indices.Count); writerRoot.WriteInt32(Pointer(indices)); var segment = new EmissionBlockSegment(root, writerRoot.ToArray()); all.Add(segment); return new ExtendedPlan([segment, .. (vertices is null ? [] : new[] { vertices }), .. (indices is null ? [] : new[] { indices })]);
    }
    private static ExtendedPlan PlanSpark(FxSparkFountainBuildData d, EmissionPlan plan, List<EmissionBlockSegment> all) { var writer = new XSourceWriter(); writer.WriteSingle(d.Gravity); writer.WriteSingle(d.BounceFrac); writer.WriteSingle(d.BounceRand); writer.WriteSingle(d.SparkSpacing); writer.WriteSingle(d.SparkLength); writer.WriteInt32(d.SparkCount); writer.WriteSingle(d.LoopTime); writer.WriteSingle(d.VelMin); writer.WriteSingle(d.VelMax); writer.WriteSingle(d.VelConeFrac); writer.WriteSingle(d.RestSpeed); writer.WriteSingle(d.BoostTime); writer.WriteSingle(d.BoostFactor); var segment = new EmissionBlockSegment(plan.Allocate(0x34, 4), writer.ToArray()); all.Add(segment); return new ExtendedPlan([segment]); }
    private static ExtendedPlan PlanDefault(byte value, EmissionPlan plan, List<EmissionBlockSegment> all) { var segment = new EmissionBlockSegment(plan.Allocate(1), [value]); all.Add(segment); return new ExtendedPlan([segment]); }
    private static AssetBodyEmission PlanExternal(SymbolicXAssetReference reference, XAssetType type, int size, EmissionPlan plan, List<EmissionBlockSegment> all)
    {
        plan.Push(XFileBlockType.TEMP); EmissionAddress root = plan.Allocate(size, 4); plan.Push(XFileBlockType.LARGE); PlannedString? name = AssetBodyEmitterHelpers.PlanString(reference.OriginalSerializedName, plan, all, plan.StringAliases); plan.Pop(XFileBlockType.LARGE); plan.Pop(XFileBlockType.TEMP); var writer = new XSourceWriter(); writer.WriteInt32(AssetBodyEmitterHelpers.SourcePointer(name)); writer.Reserve(size - 4); var segment = new EmissionBlockSegment(root, writer.ToArray()); all.Add(segment); List<EmissionBlockSegment> source = [segment]; if (name is { IsExistingMaterialization: false, Address: var address }) source.Add(all.Single(value => value.Address == address)); return new AssetBodyEmission(type, root, [segment, .. source.Skip(1)], source);
    }

    private static void CheckElement(FxElementBuildData d, int index, List<EmissionError> errors, int? rowIndex)
    {
        string path = $"elements[{index}]";
        if (d.ElemType > Runner)
            errors.Add(Error($"{path}.elemType", "Unknown Fx element discriminator.", rowIndex));
        if (d.SpawnOrigin.Count != 3 ||
            d.SpawnAngles.Count != 3 ||
            d.AngularVelocity.Count != 3)
        {
            errors.Add(Error(path, "Fx element range vectors require exactly three components.", rowIndex));
        }
        if (!ValidSamples(d.VelocitySamples.Count, d.VelIntervalCount) ||
            !ValidSamples(d.VisualSamples.Count, d.VisStateIntervalCount))
        {
            errors.Add(Error(path, "A sample table is either null with zero intervals or contains interval count plus one rows.", rowIndex));
        }
        int expectedVisuals = d.VisualCount > 1 ? d.VisualCount : 1;
        if (d.ElemType == Decal)
        {
            if (d.MarkVisuals.Count != d.VisualCount || d.Visuals.Count != 0)
            {
                errors.Add(Error(
                    $"{path}.visuals",
                    "Decal elements require exactly VisualCount mark visuals and no regular visual arms.",
                    rowIndex));
            }
            foreach (FxMarkVisualBuildData mark in d.MarkVisuals)
            {
                CheckNestedOrExternal(
                    mark.Material0Link,
                    mark.Material0Reference,
                    XAssetType.Material,
                    $"{path}.mark.material0",
                    errors,
                    rowIndex);
                CheckNestedOrExternal(
                    mark.Material1Link,
                    mark.Material1Reference,
                    XAssetType.Material,
                    $"{path}.mark.material1",
                    errors,
                    rowIndex);
            }
        }
        else
        {
            if (d.Visuals.Count != expectedVisuals || d.MarkVisuals.Count != 0)
            {
                errors.Add(Error(
                    $"{path}.visuals",
                    "Non-decal elements require max(VisualCount, 1) regular visual arms.",
                    rowIndex));
            }
            foreach (FxVisualBuildData visual in d.Visuals)
                CheckVisual(visual, d.ElemType, path, errors, rowIndex);
        }
        CheckLink(d.EffectOnImpactReference, XAssetType.Fx, $"{path}.effectOnImpact", errors, rowIndex); CheckLink(d.EffectOnDeathReference, XAssetType.Fx, $"{path}.effectOnDeath", errors, rowIndex); CheckLink(d.EffectEmittedReference, XAssetType.Fx, $"{path}.effectEmitted", errors, rowIndex); CheckExtended(d, path, errors, rowIndex); if (!AllFinite(d)) errors.Add(Error(path, "All authored Fx floating-point values must be finite.", rowIndex));
    }
    private static void CheckVisual(FxVisualBuildData visual, byte elemType, string path, List<EmissionError> errors, int? rowIndex)
    {
        FxVisualBuildKind expected = elemType switch
        {
            Model => FxVisualBuildKind.Model,
            OmniLight or SpotLight => FxVisualBuildKind.NoChild,
            Sound => FxVisualBuildKind.Sound,
            Runner => FxVisualBuildKind.Effect,
            _ => FxVisualBuildKind.Material
        };
        if (visual.Kind != expected)
            errors.Add(Error($"{path}.visual", $"Element discriminator requires visual arm {expected}.", rowIndex));
        if (visual.Kind == FxVisualBuildKind.Material)
        {
            CheckNestedOrExternal(
                visual.MaterialLink,
                visual.MaterialReference,
                XAssetType.Material,
                $"{path}.visual.material",
                errors,
                rowIndex);
        }
        if (visual.Kind == FxVisualBuildKind.Model)
        {
            CheckNestedOrExternal(
                visual.ModelLink,
                visual.ModelReference,
                XAssetType.XModel,
                $"{path}.visual.model",
                errors,
                rowIndex);
        }
        if (visual.Kind == FxVisualBuildKind.Sound)
            CheckLink(visual.SoundReference, XAssetType.Sound, $"{path}.visual.sound", errors, rowIndex);
        if (visual.Kind == FxVisualBuildKind.Effect)
            CheckLink(visual.EffectReference, XAssetType.Fx, $"{path}.visual.effect", errors, rowIndex);
    }
    private static void CheckNestedOrExternal(
        NestedXAssetBuildLink? link,
        SymbolicXAssetReference? reference,
        XAssetType expected,
        string path,
        List<EmissionError> errors,
        int? rowIndex)
    {
        if (link is not null)
        {
            errors.AddRange(NestedXAssetEmission.Validate(
                link,
                expected,
                path,
                rowIndex,
                XAssetType.Fx));
            return;
        }
        CheckExternal(reference, expected, path, errors, rowIndex);
    }
    private static void CheckExtended(FxElementBuildData d, string path, List<EmissionError> errors, int? rowIndex) { FxExtendedBuildData? value = d.Extended; if (value is null) return; FxExtendedBuildKind expected = d.ElemType == Trail ? FxExtendedBuildKind.Trail : d.ElemType == SparkFountain ? FxExtendedBuildKind.SparkFountain : FxExtendedBuildKind.DefaultBytePayload; if (value.Kind != expected) errors.Add(Error($"{path}.extended", $"Element discriminator requires extended arm {expected}.", rowIndex)); if (value.Kind == FxExtendedBuildKind.Trail && value.Trail is null) errors.Add(Error($"{path}.extended.trail", "Trail data is required.", rowIndex)); if (value.Kind == FxExtendedBuildKind.SparkFountain && value.SparkFountain is null) errors.Add(Error($"{path}.extended.spark", "Spark-fountain data is required.", rowIndex)); if (value.Trail is { } trail && (trail.Vertices.Count > int.MaxValue / 0x14 || trail.Indices.Count > int.MaxValue / 2 || trail.Indices.Any(i => i >= trail.Vertices.Count))) errors.Add(Error($"{path}.extended.trail", "Trail indices must address vertices and serialized sizes must fit Int32.", rowIndex)); }
    private static bool AllFinite(FxElementBuildData d) => Floats(d).All(float.IsFinite);
    private static bool ValidSamples(int sampleCount, byte intervalCount) =>
        sampleCount == 0 ? intervalCount == 0 : sampleCount == intervalCount + 1;
    private static IEnumerable<float> Floats(FxElementBuildData d) { yield return d.SpawnFrustumCullRadius; foreach (FxFloatRangeBuildData r in d.SpawnOrigin.Concat(d.SpawnAngles).Concat(d.AngularVelocity).Append(d.SpawnRange).Append(d.FadeInRange).Append(d.FadeOutRange).Append(d.SpawnOffsetRadius).Append(d.SpawnOffsetHeight).Append(d.InitialRotation).Append(d.Gravity).Append(d.ReflectionFactor).Append(d.EmitDist).Append(d.EmitDistVariance)) { yield return r.Base; yield return r.Amplitude; } foreach (FxVelocitySampleBuildData sample in d.VelocitySamples) foreach (FxVelocityInFrameBuildData frame in new[] { sample.Local, sample.World }) foreach (FxVec3BuildData v in new[] { frame.VelocityBase, frame.VelocityAmplitude, frame.TotalDeltaBase, frame.TotalDeltaAmplitude }) { yield return v.X; yield return v.Y; yield return v.Z; } foreach (FxVisualStateSampleBuildData sample in d.VisualSamples) foreach (FxVisualStateBuildData state in new[] { sample.Base, sample.Amplitude }) { yield return state.RotationDelta; yield return state.RotationTotal; yield return state.Size0; yield return state.Size1; yield return state.Scale; } yield return d.CollBounds.MidPoint.X; yield return d.CollBounds.MidPoint.Y; yield return d.CollBounds.MidPoint.Z; yield return d.CollBounds.HalfSize.X; yield return d.CollBounds.HalfSize.Y; yield return d.CollBounds.HalfSize.Z; if (d.Extended?.Trail is { } trail) { yield return trail.InvSplitDist; yield return trail.InvSplitArcDist; yield return trail.InvSplitTime; foreach (FxTrailVertexBuildData v in trail.Vertices) { yield return v.Pos0; yield return v.Pos1; yield return v.Normal0; yield return v.Normal1; yield return v.TexCoord; } } if (d.Extended?.SparkFountain is { } spark) foreach (float value in new[] { spark.Gravity, spark.BounceFrac, spark.BounceRand, spark.SparkSpacing, spark.SparkLength, spark.LoopTime, spark.VelMin, spark.VelMax, spark.VelConeFrac, spark.RestSpeed, spark.BoostTime, spark.BoostFactor }) yield return value; }
    private static void CheckExternal(SymbolicXAssetReference? value, XAssetType expected, string path, List<EmissionError> errors, int? rowIndex) { if (value is not null && (value.AssetType != expected || !value.IsExternalReference || !AssetBodyEmitterHelpers.IsLatin1CString(value.OriginalSerializedName))) errors.Add(Error(path, $"Reference must be a comma-prefixed external {expected} identity.", rowIndex)); }
    private static void CheckLink(SymbolicXAssetReference? value, XAssetType expected, string path, List<EmissionError> errors, int? rowIndex) { if (value is not null && (value.AssetType != expected || !AssetBodyEmitterHelpers.IsLatin1CString(value.OriginalSerializedName))) errors.Add(Error(path, $"Reference must be a Latin-1 {expected} identity.", rowIndex)); }
    private static void CheckString(string? value, string path, List<EmissionError> errors, int? rowIndex) { if (value is not null && !AssetBodyEmitterHelpers.IsLatin1CString(value)) errors.Add(Error(path, "Value must be a Latin-1 C string.", rowIndex)); }
    private static void VelocityFrame(XSourceWriter writer, FxVelocityInFrameBuildData value) { Vec(writer, value.VelocityBase); Vec(writer, value.VelocityAmplitude); Vec(writer, value.TotalDeltaBase); Vec(writer, value.TotalDeltaAmplitude); }
    private static void VisualState(XSourceWriter writer, FxVisualStateBuildData value) { writer.WriteByte(value.Color.R); writer.WriteByte(value.Color.G); writer.WriteByte(value.Color.B); writer.WriteByte(value.Color.A); writer.WriteSingle(value.RotationDelta); writer.WriteSingle(value.RotationTotal); writer.WriteSingle(value.Size0); writer.WriteSingle(value.Size1); writer.WriteSingle(value.Scale); }
    private static void Vec(XSourceWriter writer, FxVec3BuildData value) { writer.WriteSingle(value.X); writer.WriteSingle(value.Y); writer.WriteSingle(value.Z); } private static void Range(XSourceWriter writer, FxFloatRangeBuildData value) { writer.WriteSingle(value.Base); writer.WriteSingle(value.Amplitude); } private static void IntRange(XSourceWriter writer, FxIntRangeBuildData value) { writer.WriteInt32(value.Base); writer.WriteInt32(value.Amplitude); } private static int Pointer(EmissionBlockSegment? value) => value is null ? 0 : -1; private static void Add(List<EmissionBlockSegment> values, EmissionBlockSegment? value) { if (value is not null) values.Add(value); } private static EmissionError Error(string path, string message, int? rowIndex) => new(path, message, rowIndex, XAssetType.Fx);
    private sealed record ElementPlan(byte[] Root, IReadOnlyList<EmissionBlockSegment> Source); private sealed record PayloadPlan(int Raw, IReadOnlyList<EmissionBlockSegment> Source); private sealed record VisualPlan(int Raw, IReadOnlyList<EmissionBlockSegment> Source); private sealed record StringPlan(int Raw, IReadOnlyList<EmissionBlockSegment> Source); private sealed record ExtendedPlan(IReadOnlyList<EmissionBlockSegment> Source);
}

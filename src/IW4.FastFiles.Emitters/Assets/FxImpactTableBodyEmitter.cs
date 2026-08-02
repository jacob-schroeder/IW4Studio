using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Emission;

namespace IW4.FastFiles.Emitters.Assets;

/// <summary>Fixed 15-entry ImpactFx matrix emitter. Each non-null effect is
/// emitted as an external Fx root, retaining the engine's pointer semantics.</summary>
public sealed class FxImpactTableBodyEmitter : IXAssetBodyEmitter
{
    public XAssetType AssetType => XAssetType.ImpactFx;
    public IReadOnlyList<EmissionError> Validate(IXAssetBuildData buildData, int? rowIndex = null)
    {
        var errors = AssetBodyEmitterHelpers.ValidateIdentity(buildData, AssetType, rowIndex);
        if (buildData is not IFxImpactTableBuildData data) { errors.Add(Error("body", "ImpactFx build data does not implement IFxImpactTableBuildData.", rowIndex)); return errors; }
        if (data.Name is not null && !AssetBodyEmitterHelpers.IsLatin1CString(data.Name)) errors.Add(Error("name", "Name must be a Latin-1 C string.", rowIndex));
        if (data.Entries.Count != 15)
            errors.Add(Error("entries", "ImpactFx requires exactly 15 fixed table entries.", rowIndex));
        for (int entryIndex = 0; entryIndex < data.Entries.Count; entryIndex++)
        {
            FxImpactEntryBuildData entry = data.Entries[entryIndex];
            if (entry.SurfaceEffects.Count != 31 ||
                entry.FleshEffects.Count != 4 ||
                entry.SurfaceEffectLinks.Count != 31 ||
                entry.FleshEffectLinks.Count != 4)
            {
                errors.Add(Error(
                    $"entries[{entryIndex}]",
                    "Each entry requires 31 surface and 4 flesh Fx slots and matching provenance slots.",
                    rowIndex));
                continue;
            }
            for (int index = 0; index < entry.SurfaceEffects.Count; index++)
            {
                CheckSlot(
                    entry.SurfaceEffects[index],
                    entry.SurfaceEffectLinks[index],
                    $"entries[{entryIndex}].surfaceEffects[{index}]",
                    errors,
                    rowIndex);
            }
            for (int index = 0; index < entry.FleshEffects.Count; index++)
            {
                CheckSlot(
                    entry.FleshEffects[index],
                    entry.FleshEffectLinks[index],
                    $"entries[{entryIndex}].fleshEffects[{index}]",
                    errors,
                    rowIndex);
            }
        }
        return errors;
    }
    public AssetBodyEmission Plan(IXAssetBuildData buildData, EmissionPlan plan, int? rowIndex = null)
    {
        ArgumentNullException.ThrowIfNull(plan); AssetBodyEmitterHelpers.RequireNoDiagnostics(Validate(buildData, rowIndex)); IFxImpactTableBuildData data = (IFxImpactTableBuildData)buildData; var all = new List<EmissionBlockSegment>(); var source = new List<EmissionBlockSegment>();
        plan.Push(XFileBlockType.TEMP); EmissionAddress root = plan.Allocate(8, 4); plan.Push(XFileBlockType.LARGE); int beforeName = all.Count; PlannedString? name = AssetBodyEmitterHelpers.PlanString(data.Name, plan, all, plan.StringAliases); int afterName = all.Count;
        EmissionAddress entriesAddress = plan.Allocate(15 * 0x8c, 4);
        SymbolicXAssetReference?[] refs = data.Entries
            .SelectMany(entry => entry.SurfaceEffects.Concat(entry.FleshEffects))
            .ToArray();
        NestedXAssetBuildLink?[] links = data.Entries
            .SelectMany(entry => entry.SurfaceEffectLinks.Concat(entry.FleshEffectLinks))
            .ToArray();
        var plannedRefs = new ExternalPlan?[refs.Length];
        var refSources = new List<EmissionBlockSegment>();
        for (int index = 0; index < refs.Length; index++)
        {
            if (refs[index] is null && links[index] is null)
                continue;
            EmissionAddress ownerCell = new(
                entriesAddress.Block,
                checked(entriesAddress.Offset + index * sizeof(int)));
            plannedRefs[index] = PlanFx(
                links[index],
                refs[index],
                ownerCell,
                plan,
                all);
            refSources.AddRange(plannedRefs[index]!.SourceSegments);
        }
        var entryWriter = new XSourceWriter();
        foreach (ExternalPlan? reference in plannedRefs)
            entryWriter.WriteInt32(reference?.PointerRaw ?? 0);
        var entries = new EmissionBlockSegment(entriesAddress, entryWriter.ToArray());
        all.Add(entries);
        plan.Pop(XFileBlockType.LARGE); plan.Pop(XFileBlockType.TEMP);
        var writer = new XSourceWriter(); writer.WriteInt32(AssetBodyEmitterHelpers.SourcePointer(name)); writer.WriteInt32(-1); var rootSegment = new EmissionBlockSegment(root, writer.ToArray()); all.Add(rootSegment); source.Add(rootSegment); source.AddRange(all.Skip(beforeName).Take(afterName - beforeName)); source.Add(entries); source.AddRange(refSources); return new AssetBodyEmission(AssetType, root, all, source);
    }
    private static ExternalPlan PlanFx(
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
                "ImpactFx.Effect");
            return new ExternalPlan(child.PointerRaw, child.Source);
        }
        return PlanFxExternal(
            reference ?? throw new InvalidDataException(
                "ImpactFx slot has neither nested provenance nor a symbolic reference."),
            ownerCell,
            plan,
            all);
    }
    private static ExternalPlan PlanFxExternal(
        SymbolicXAssetReference reference,
        EmissionAddress ownerCell,
        EmissionPlan plan,
        List<EmissionBlockSegment> all)
    {
        string aliasKey = AssetBodyEmitterHelpers.XAssetAliasKey(
            XAssetType.Fx,
            reference.OriginalSerializedName);
        if (plan.PersistentXAssetAliasCells.TryGetValue(
                aliasKey,
                out EmissionAddress existingCell))
        {
            return new ExternalPlan(existingCell.ToPackedPointer(), []);
        }

        plan.Push(XFileBlockType.TEMP); EmissionAddress root = plan.Allocate(0x20, 4); plan.Push(XFileBlockType.LARGE); PlannedString? name = AssetBodyEmitterHelpers.PlanString(reference.OriginalSerializedName, plan, all, plan.StringAliases); plan.Pop(XFileBlockType.LARGE); plan.Pop(XFileBlockType.TEMP); var writer = new XSourceWriter(); writer.WriteInt32(AssetBodyEmitterHelpers.SourcePointer(name)); writer.Reserve(0x1c); var rootSegment = new EmissionBlockSegment(root, writer.ToArray()); all.Add(rootSegment); List<EmissionBlockSegment> source = [rootSegment]; if (name is { IsExistingMaterialization: false, Address: var address }) source.Add(all.Single(segment => segment.Address == address));
        if (ownerCell.Block != XFileBlockType.TEMP)
            plan.PersistentXAssetAliasCells.TryAdd(aliasKey, ownerCell);
        return new ExternalPlan(-1, source);
    }
    private static void CheckSlot(
        SymbolicXAssetReference? reference,
        NestedXAssetBuildLink? link,
        string path,
        List<EmissionError> errors,
        int? rowIndex)
    {
        if (link is null)
        {
            if (reference is not null &&
                (reference.AssetType != XAssetType.Fx ||
                 !reference.IsExternalReference ||
                 !AssetBodyEmitterHelpers.IsLatin1CString(
                     reference.OriginalSerializedName)))
            {
                errors.Add(Error(
                    path,
                    "Effect slot must be a comma-prefixed external Fx identity.",
                    rowIndex));
            }
            return;
        }

        errors.AddRange(NestedXAssetEmission.Validate(
            link,
            XAssetType.Fx,
            path,
            rowIndex,
            XAssetType.ImpactFx));
        if (reference is null)
        {
            errors.Add(Error(
                path,
                "An imported effect link requires its symbolic dependency reference.",
                rowIndex));
            return;
        }
        if (reference.AssetType != XAssetType.Fx ||
            !reference.IsExternalReference ||
            !AssetBodyEmitterHelpers.IsLatin1CString(
                reference.OriginalSerializedName) ||
            AssetBodyEmitterHelpers.XAssetAliasKey(
                reference.AssetType,
                reference.OriginalSerializedName) != link.AliasKey)
        {
            errors.Add(Error(
                path,
                "Imported effect link and external Fx identity must agree.",
                rowIndex));
        }
    }
    private static EmissionError Error(string path, string message, int? rowIndex) => new(path, message, rowIndex, XAssetType.ImpactFx);
    private sealed record ExternalPlan(
        int PointerRaw,
        IReadOnlyList<EmissionBlockSegment> SourceSegments);
}

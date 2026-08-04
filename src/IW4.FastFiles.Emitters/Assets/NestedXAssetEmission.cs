using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Emission;

namespace IW4.FastFiles.Emitters.Assets;

internal sealed record NestedXAssetPlan(
    int PointerRaw,
    IReadOnlyList<EmissionBlockSegment> Source,
    AssetBodyEmission? IncomingDefinition);

/// <summary>
/// Plans an imported nested XAsset pointer without blindly replaying an
/// imported address. Compatibility replay is limited to an unchanged owner
/// cell. Packed aliases otherwise resolve through an identity-bearing
/// persistent pointer cell; inline definitions are emitted through the
/// registered body emitter and register their owner cell for later alias
/// conversion.
/// </summary>
internal static class NestedXAssetEmission
{
    private static readonly AssetBodyEmitterRegistry Emitters =
        AssetBodyEmitterRegistry.CreateDefault();

    public static NestedXAssetPlan Plan(
        NestedXAssetBuildLink link,
        EmissionPlan plan,
        List<EmissionBlockSegment> all,
        EmissionAddress ownerCell,
        string owner = "NestedXAsset")
    {
        ArgumentNullException.ThrowIfNull(link);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(all);

        if (!AssetBodyEmitterHelpers.IsLatin1CString(
                link.Reference.OriginalSerializedName))
        {
            throw new InvalidDataException(
                $"Nested {link.Reference.AssetType} identity is not a Latin-1 C string.");
        }

        if (link.SourceForm == NestedXAssetPointerSourceForm.PackedAlias)
        {
            if (plan.PersistentXAssetAliasCells.TryGetValue(
                    link.AliasKey,
                    out EmissionAddress aliasCell))
            {
                return new NestedXAssetPlan(
                    aliasCell.ToPackedPointer(),
                    [],
                    null);
            }

            if (plan.PreserveImportedXAssetPointerValues &&
                link.ImportedOwnerCellRaw == ownerCell.ToPackedPointer() &&
                link.ImportedPackedRaw is { } importedRaw)
            {
                if (IW4.FastFiles.Pointers.XPointerCodec.GetType(
                        importedRaw) !=
                    IW4.FastFiles.Pointers.PointerType.Offset)
                {
                    throw new InvalidDataException(
                        $"Imported packed nested {link.Reference.AssetType} " +
                        $"reference '{link.Reference.OriginalSerializedName}' " +
                        $"retains non-packed raw 0x{unchecked((uint)importedRaw):X8}.");
                }
                return new NestedXAssetPlan(importedRaw, [], null);
            }

            // Packed aliases may target a cell owned by a dependency zone
            // (common_mp/common), which has no source-independent address in
            // this link. Materialize one comma-prefixed reference provider at
            // the first current-zone occurrence, then publish its persistent
            // owner cell so later occurrences can remain packed.
            return PlanExternalFallback(
                link,
                plan,
                all,
                ownerCell,
                owner);
        }

        IXAssetBuildData definition = link.IncomingDefinition
            ?? throw new InvalidDataException(
                $"{link.SourceForm} nested {link.Reference.AssetType} reference " +
                $"'{link.Reference.OriginalSerializedName}' has no incoming definition.");
        if (definition.AssetType != link.Reference.AssetType)
        {
            throw new InvalidDataException(
                $"Nested reference type {link.Reference.AssetType} does not match " +
                $"its incoming definition type {definition.AssetType}.");
        }

        EmissionAddress? insertCell = null;
        if (link.SourceForm == NestedXAssetPointerSourceForm.Insert)
        {
            insertCell = plan.AllocateInsertPointerCell(
                owner,
                $"insert:{link.Reference.AssetType}:{link.Reference.OriginalSerializedName}");
        }

        IXAssetBodyEmitter emitter = Emitters.Require(definition.AssetType);
        AssetBodyEmission emission = emitter.Plan(definition, plan);
        all.AddRange(emission.Segments);

        EmissionAddress aliasOwner = insertCell is { Block: not XFileBlockType.TEMP } persistentInsert
            ? persistentInsert
            : ownerCell;
        if (aliasOwner.Block != XFileBlockType.TEMP)
            plan.PersistentXAssetAliasCells.TryAdd(link.AliasKey, aliasOwner);

        return new NestedXAssetPlan(
            link.SourceForm == NestedXAssetPointerSourceForm.Insert ? -2 : -1,
            emission.SourceSegments,
            emission);
    }

    private static NestedXAssetPlan PlanExternalFallback(
        NestedXAssetBuildLink link,
        EmissionPlan plan,
        List<EmissionBlockSegment> all,
        EmissionAddress ownerCell,
        string owner)
    {
        (int rootSize, int nameOffset) = ExternalLayout(
            link.Reference.AssetType);

        EmissionAddress? insertCell = null;
        int pointerRaw = -1;
        if (ownerCell.Block == XFileBlockType.TEMP)
        {
            insertCell = plan.AllocateInsertPointerCell(
                owner,
                $"dependency-insert:{link.Reference.AssetType}:{link.Reference.OriginalSerializedName}");
            pointerRaw = -2;
        }

        plan.Push(XFileBlockType.TEMP, $"{owner} dependency reference");
        EmissionAddress root = plan.Allocate(
            rootSize,
            sizeof(int),
            $"{owner}.dependency-root:{link.Reference.AssetType}");
        plan.Push(XFileBlockType.LARGE, $"{owner} dependency name");
        string wireName = link.Reference.OriginalSerializedName.StartsWith(
            ",",
            StringComparison.Ordinal)
            ? link.Reference.OriginalSerializedName
            : $",{link.Reference.OriginalSerializedName}";
        int beforeName = all.Count;
        PlannedString? name = AssetBodyEmitterHelpers.PlanString(
            wireName,
            plan,
            all,
            plan.StringAliases);
        EmissionBlockSegment[] nameSource = all.Skip(beforeName).ToArray();
        plan.Pop(XFileBlockType.LARGE);
        plan.Pop(XFileBlockType.TEMP);

        var writer = new XSourceWriter();
        writer.Reserve(nameOffset);
        writer.WriteInt32(AssetBodyEmitterHelpers.SourcePointer(name));
        writer.Reserve(rootSize - nameOffset - sizeof(int));
        var rootSegment = new EmissionBlockSegment(root, writer.ToArray());
        all.Add(rootSegment);

        EmissionAddress aliasOwner = insertCell is { Block: not XFileBlockType.TEMP } persistentInsert
            ? persistentInsert
            : ownerCell;
        if (aliasOwner.Block != XFileBlockType.TEMP)
            plan.PersistentXAssetAliasCells.TryAdd(link.AliasKey, aliasOwner);

        return new NestedXAssetPlan(
            pointerRaw,
            [rootSegment, .. nameSource],
            new AssetBodyEmission(
                link.Reference.AssetType,
                root,
                [rootSegment, .. nameSource],
                [rootSegment, .. nameSource]));
    }

    private static (int RootSize, int NameOffset) ExternalLayout(
        XAssetType type) => type switch
    {
        XAssetType.PhysPreset => (0x2c, 0),
        XAssetType.PhysCollmap => (0x48, 0),
        XAssetType.XModel => (0x120, 0),
        XAssetType.XModelSurfs => (0x24, 0),
        XAssetType.Material => (0xa8, 0),
        XAssetType.PixelShader => (0x18, 0),
        XAssetType.VertexShader => (0x0c, 0),
        XAssetType.Techset => (0x9c, 0),
        XAssetType.Image => (0x50, 0x4c),
        XAssetType.Sound => (0x0c, 0),
        XAssetType.SndCurve => (0x88, 0),
        XAssetType.LoadedSound => (0x1c, 0),
        XAssetType.Fx => (0x20, 0),
        XAssetType.MapEnts => (0x2c, 0),
        XAssetType.Menu =>
            (IW4.Assets.Assets.Menu.MenuDefAsset.SerializedSize, 0),
        _ => throw new InvalidDataException(
            $"No external reference layout is registered for nested {type}.")
    };

    public static IReadOnlyList<EmissionError> Validate(
        NestedXAssetBuildLink? link,
        XAssetType expected,
        string path,
        int? rowIndex,
        XAssetType ownerType)
    {
        if (link is null)
            return [];

        var diagnostics = new List<EmissionError>();
        if (link.Reference.AssetType != expected)
        {
            diagnostics.Add(new(
                path,
                $"Nested reference declares {link.Reference.AssetType}; expected {expected}.",
                rowIndex,
                ownerType));
        }
        if (!AssetBodyEmitterHelpers.IsLatin1CString(
                link.Reference.OriginalSerializedName))
        {
            diagnostics.Add(new(
                path,
                "Nested reference identity must be a Latin-1 C string.",
                rowIndex,
                ownerType));
        }
        bool inline = link.SourceForm is
            NestedXAssetPointerSourceForm.Inline or
            NestedXAssetPointerSourceForm.Insert;
        if (inline != (link.IncomingDefinition is not null))
        {
            diagnostics.Add(new(
                path,
                "Inline/insert nested pointers require one incoming definition; packed aliases must not carry one.",
                rowIndex,
                ownerType));
        }
        else if (link.IncomingDefinition is { } definition &&
                 definition.AssetType != expected)
        {
            diagnostics.Add(new(
                path,
                $"Incoming definition declares {definition.AssetType}; expected {expected}.",
                rowIndex,
                ownerType));
        }
        if (link.ImportedPackedRaw is not null &&
            link.SourceForm != NestedXAssetPointerSourceForm.PackedAlias)
        {
            diagnostics.Add(new(
                path,
                "Only packed nested pointers may retain an imported packed raw value.",
                rowIndex,
                ownerType));
        }
        else if (link.ImportedPackedRaw is { } importedRaw &&
                 IW4.FastFiles.Pointers.XPointerCodec.GetType(
                     importedRaw) !=
                 IW4.FastFiles.Pointers.PointerType.Offset)
        {
            diagnostics.Add(new(
                path,
                "Imported packed raw value is not an offset pointer.",
                rowIndex,
                ownerType));
        }
        if (link.ImportedOwnerCellRaw is { } ownerCellRaw &&
            IW4.FastFiles.Pointers.XPointerCodec.GetType(
                ownerCellRaw) !=
            IW4.FastFiles.Pointers.PointerType.Offset)
        {
            diagnostics.Add(new(
                path,
                "Imported nested-pointer owner-cell provenance is not an offset pointer.",
                rowIndex,
                ownerType));
        }

        return diagnostics;
    }
}

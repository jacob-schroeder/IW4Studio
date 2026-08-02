using IW4.Assets.Assets.GameMap;
using IW4.Assets.Assets.Weapon;
using IW4.FastFiles.Strings;
using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Assets;

namespace IW4.FastFiles.Emitters.Linking;

/// <summary>
/// Describes one serialized scr_string_t field discovered by an explicit
/// build-data collector. Semantic references can be rebound by value. Opaque
/// references retain only an imported local index and therefore cannot be
/// emitted into a canonical zone when nonzero.
/// </summary>
public sealed record ZoneScriptStringUse(
    string FieldPath,
    ushort RawLocalIndex,
    string? SemanticValue,
    ZoneScriptStringRepresentation Representation)
{
    public static ZoneScriptStringUse Semantic(
        string fieldPath,
        ScriptStringReference reference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldPath);
        ArgumentNullException.ThrowIfNull(reference);
        return new(
            fieldPath,
            reference.RawLocalIndex,
            reference.Text,
            ZoneScriptStringRepresentation.SemanticReference);
    }

    public static ZoneScriptStringUse Opaque(
        string fieldPath,
        ushort rawLocalIndex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldPath);
        return new(
            fieldPath,
            rawLocalIndex,
            SemanticValue: null,
            ZoneScriptStringRepresentation.OpaqueImportedIndex);
    }
}

public enum ZoneScriptStringRepresentation
{
    SemanticReference,
    OpaqueImportedIndex
}

/// <summary>
/// Closed, deterministic registry for compiler-visible script-string fields.
/// Reflection is deliberately excluded: adding a script-bearing field requires
/// a named collector and an explicit decision about whether semantic text is
/// available.
/// </summary>
public sealed class ZoneScriptStringCollectorRegistry
{
    private readonly Dictionary<
        XAssetType,
        Func<IXAssetBuildData, IReadOnlyList<ZoneScriptStringUse>>> _collectors = [];

    public static ZoneScriptStringCollectorRegistry CreateDefault()
    {
        var registry = new ZoneScriptStringCollectorRegistry();
        registry.Register(XAssetType.Weapon, CollectWeapon);
        registry.Register(XAssetType.GameMapSp, CollectGameMapSp);
        registry.Register(XAssetType.GameMapMp, CollectGameMapMp);
        registry.Register(XAssetType.Vehicle, CollectVehicle);
        registry.Register(XAssetType.XAnim, CollectXAnim);
        registry.Register(XAssetType.XModel, CollectXModel);
        return registry;
    }

    public void Register(
        XAssetType assetType,
        Func<IXAssetBuildData, IReadOnlyList<ZoneScriptStringUse>> collector)
    {
        if (!Enum.IsDefined(assetType))
            throw new ArgumentOutOfRangeException(nameof(assetType));
        ArgumentNullException.ThrowIfNull(collector);
        if (!_collectors.TryAdd(assetType, collector))
        {
            throw new InvalidDataException(
                $"A script-string collector is already registered for '{assetType}'.");
        }
    }

    public IReadOnlyList<ZoneScriptStringUse> Collect(IXAssetBuildData buildData)
    {
        ArgumentNullException.ThrowIfNull(buildData);
        if (!_collectors.TryGetValue(buildData.AssetType, out var collector))
            return [];

        ZoneScriptStringUse[] uses = collector(buildData).ToArray();
        for (int index = 0; index < uses.Length; index++)
        {
            ZoneScriptStringUse use = uses[index]
                ?? throw new InvalidDataException(
                    $"The '{buildData.AssetType}' script-string collector returned a null use at index {index}.");
            if (string.IsNullOrWhiteSpace(use.FieldPath))
            {
                throw new InvalidDataException(
                    $"The '{buildData.AssetType}' script-string collector returned an empty field path.");
            }
            if (!Enum.IsDefined(use.Representation))
            {
                throw new InvalidDataException(
                    $"The '{buildData.AssetType}' script-string collector returned an unknown representation.");
            }
        }
        return Array.AsReadOnly(uses);
    }

    private static IReadOnlyList<ZoneScriptStringUse> CollectWeapon(
        IXAssetBuildData buildData)
    {
        IWeaponBuildData data = Require<IWeaponBuildData>(buildData, XAssetType.Weapon);
        var uses = new List<ZoneScriptStringUse>();
        AddSemantic(uses, data.Variant.HideTags, "variant.hideTags");
        AddSemantic(
            uses,
            data.Definition.NoteTrackMaps.SoundMapKeys,
            "definition.noteTrackMaps.soundMapKeys");
        AddSemantic(
            uses,
            data.Definition.NoteTrackMaps.SoundMapValues,
            "definition.noteTrackMaps.soundMapValues");
        AddSemantic(
            uses,
            data.Definition.NoteTrackMaps.RumbleMapKeys,
            "definition.noteTrackMaps.rumbleMapKeys");
        AddSemantic(
            uses,
            data.Definition.NoteTrackMaps.RumbleMapValues,
            "definition.noteTrackMaps.rumbleMapValues");
        return uses;
    }

    private static IReadOnlyList<ZoneScriptStringUse> CollectGameMapSp(
        IXAssetBuildData buildData)
    {
        IGameWorldSpBuildData data = Require<IGameWorldSpBuildData>(
            buildData,
            XAssetType.GameMapSp);
        var uses = new List<ZoneScriptStringUse>();
        for (int index = 0; index < data.Path.Nodes.Count; index++)
        {
            PathNodeConstant value = data.Path.Nodes[index].Constant;
            string prefix = $"path.nodes[{index}].constant";
            uses.Add(ZoneScriptStringUse.Semantic($"{prefix}.targetName", value.TargetName));
            uses.Add(ZoneScriptStringUse.Semantic($"{prefix}.scriptLinkName", value.ScriptLinkName));
            uses.Add(ZoneScriptStringUse.Semantic($"{prefix}.scriptNoteworthy", value.ScriptNoteworthy));
            uses.Add(ZoneScriptStringUse.Semantic($"{prefix}.target", value.Target));
            uses.Add(ZoneScriptStringUse.Semantic($"{prefix}.animScript", value.AnimScript));
        }
        if (data.GlassData is { } glass)
        {
            for (int index = 0; index < glass.GlassNames.Count; index++)
            {
                uses.Add(ZoneScriptStringUse.Opaque(
                    $"glassData.names[{index}].scriptString",
                    glass.GlassNames[index].Name));
            }
        }
        return uses;
    }

    private static IReadOnlyList<ZoneScriptStringUse> CollectGameMapMp(
        IXAssetBuildData buildData)
    {
        IGameWorldMpBuildData data = Require<IGameWorldMpBuildData>(
            buildData,
            XAssetType.GameMapMp);
        if (data.GlassData is not { } glass)
            return [];

        return Array.AsReadOnly(glass.Names
            .Select((value, index) => ZoneScriptStringUse.Opaque(
                $"glassData.names[{index}].scriptString",
                value.ScriptString))
            .ToArray());
    }

    private static IReadOnlyList<ZoneScriptStringUse> CollectVehicle(
        IXAssetBuildData buildData)
    {
        IVehicleBuildData data = Require<IVehicleBuildData>(
            buildData,
            XAssetType.Vehicle);
        return Array.AsReadOnly(data.TrophyTags
            .Select((value, index) => ZoneScriptStringUse.Opaque(
                $"trophyTags[{index}]",
                value))
            .ToArray());
    }

    private static IReadOnlyList<ZoneScriptStringUse> CollectXAnim(
        IXAssetBuildData buildData)
    {
        IXAnimBuildData data = Require<IXAnimBuildData>(
            buildData,
            XAssetType.XAnim);
        var uses = new List<ZoneScriptStringUse>(
            data.Names.Count + data.Notify.Count);
        for (int index = 0; index < data.Names.Count; index++)
            uses.Add(ZoneScriptStringUse.Opaque($"names[{index}]", data.Names[index]));
        for (int index = 0; index < data.Notify.Count; index++)
        {
            uses.Add(ZoneScriptStringUse.Opaque(
                $"notify[{index}].name",
                data.Notify[index].Name));
        }
        return uses;
    }

    private static IReadOnlyList<ZoneScriptStringUse> CollectXModel(
        IXAssetBuildData buildData)
    {
        IXModelBuildData data = Require<IXModelBuildData>(
            buildData,
            XAssetType.XModel);
        return Array.AsReadOnly(data.BoneNames
            .Select((value, index) => ZoneScriptStringUse.Opaque(
                $"boneNames[{index}]",
                value))
            .ToArray());
    }

    private static void AddSemantic(
        ICollection<ZoneScriptStringUse> uses,
        IReadOnlyList<ScriptStringReference> values,
        string fieldPath)
    {
        for (int index = 0; index < values.Count; index++)
        {
            uses.Add(ZoneScriptStringUse.Semantic(
                $"{fieldPath}[{index}]",
                values[index]));
        }
    }

    private static T Require<T>(
        IXAssetBuildData buildData,
        XAssetType assetType)
        where T : class, IXAssetBuildData
    {
        if (buildData.AssetType != assetType || buildData is not T typed)
        {
            throw new InvalidDataException(
                $"The '{assetType}' script-string collector requires build data implementing '{typeof(T).Name}'.");
        }
        return typed;
    }
}

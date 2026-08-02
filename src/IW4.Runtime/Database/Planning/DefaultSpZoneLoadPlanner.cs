using IW4.FastFiles.Zone;

namespace IW4.Runtime.Database.Planning;

/// <summary>
/// Builds the dependency lifecycle used by default.elf for campaign and
/// Special Ops zones.
/// </summary>
public sealed class DefaultSpZoneLoadPlanner
{
    private const string CodeZone = "code_post_gfx";
    private const string PatchZone = "patch";
    private const string UiZone = "ui";
    private const string CommonZone = "common";

    private static readonly HashSet<string> MapZones = new(StringComparer.OrdinalIgnoreCase)
    {
        // Campaign and standalone single-player zones shipped for PS3.
        "af_caves",
        "af_chase",
        "airport",
        "arcadia",
        "boneyard",
        "cliffhanger",
        "co_hunted",
        "contingency",
        "dc_whitehouse",
        "dcburning",
        "dcemp",
        "ending",
        "estate",
        "favela",
        "favela_escape",
        "gulag",
        "invasion",
        "iw4_credits",
        "oilrig",
        "roadkill",
        "trainer",

        // Special Ops base and scenario zones shipped for PS3.
        "so_ac130_co_hunted",
        "so_assault_oilrig",
        "so_bridge",
        "so_chopper_invasion",
        "so_crossing_so_bridge",
        "so_defense_invasion",
        "so_defuse_favela_escape",
        "so_demo_so_bridge",
        "so_download_arcadia",
        "so_escape_airport",
        "so_forest_contingency",
        "so_ghillies",
        "so_hidden_so_ghillies",
        "so_intel_boneyard",
        "so_juggernauts_favela",
        "so_killspree_favela",
        "so_killspree_invasion",
        "so_killspree_trainer",
        "so_rooftop_contingency",
        "so_sabotage_cliffhanger",
        "so_showers_gulag",
        "so_snowrace1_cliffhanger",
        "so_snowrace2_cliffhanger",
        "so_takeover_estate",
        "so_takeover_oilrig"
    };

    private readonly DbZoneCatalog _catalog;

    public DefaultSpZoneLoadPlanner(DbZoneCatalog catalog)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    /// <summary>
    /// Returns true for the default.elf startup zones and the official
    /// campaign or Special Ops map zones.
    /// </summary>
    public static bool SupportsTarget(string targetNameOrPath)
    {
        if (string.IsNullOrWhiteSpace(targetNameOrPath))
            return false;

        string targetName = DbZoneCatalog.NormalizeZoneName(targetNameOrPath);
        return IsStartupZone(targetName) || MapZones.Contains(targetName);
    }

    public DbZoneLoadPlan Build(
        string targetNameOrPath,
        DbZonePlanScope scope = DbZonePlanScope.ThroughTarget)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetNameOrPath);
        string targetName = DbZoneCatalog.NormalizeZoneName(targetNameOrPath);
        string? targetOverride = ResolveTargetOverride(targetNameOrPath);
        return BuildCore(targetName, targetOverride, scope);
    }

    /// <summary>
    /// Builds the plan for one logical target while loading that target from
    /// an explicit candidate path. Dependencies continue to come from the
    /// planner's catalog.
    /// </summary>
    public DbZoneLoadPlan BuildWithTargetOverride(
        string targetZoneName,
        string targetFastFilePath,
        DbZonePlanScope scope = DbZonePlanScope.ThroughTarget)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetZoneName);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFastFilePath);
        return BuildCore(
            DbZoneCatalog.NormalizeZoneName(targetZoneName),
            Path.GetFullPath(targetFastFilePath),
            scope);
    }

    private DbZoneLoadPlan BuildCore(
        string targetName,
        string? targetOverride,
        DbZonePlanScope scope)
    {
        ValidateSupportedTarget(targetName);

        if (scope == DbZonePlanScope.StructuralSingleZone)
        {
            DbZonePlanRequest request = CreateLoadRequest(
                targetName,
                NativeAllocFlags(targetName),
                XZoneFlags.None,
                targetName,
                targetOverride,
                targetSeen: false,
                out _);
            return new DbZoneLoadPlan(
                targetName,
                scope,
                [new DbLoadXAssetsBatch("StructuralSingleZone", true, [request])]);
        }

        var batches = new List<DbLoadXAssetsBatch>();
        bool targetSeen = false;

        batches.Add(new DbLoadXAssetsBatch(
            "StartupCore",
            Synchronous: true,
            Requests:
            [
                CreateLoadRequest(
                    CodeZone,
                    XZoneFlags.DB_ZONE_COMMON,
                    XZoneFlags.None,
                    targetName,
                    targetOverride,
                    targetSeen,
                    out targetSeen),
                CreateLoadRequest(
                    PatchZone,
                    XZoneFlags.DB_ZONE_COMMON,
                    XZoneFlags.None,
                    targetName,
                    targetOverride,
                    targetSeen,
                    out targetSeen)
            ]));

        bool needsUiCommon = !IsCoreZone(targetName) || scope == DbZonePlanScope.StableRuntime;
        if (needsUiCommon)
        {
            batches.Add(new DbLoadXAssetsBatch(
                "StartupUiCommon",
                Synchronous: false,
                Requests:
                [
                    CreateLoadRequest(
                        UiZone,
                        XZoneFlags.DB_ZONE_UI,
                        XZoneFlags.None,
                        targetName,
                        targetOverride,
                        targetSeen,
                        out targetSeen),
                    CreateLoadRequest(
                        CommonZone,
                        XZoneFlags.DB_ZONE_COMMON,
                        XZoneFlags.None,
                        targetName,
                        targetOverride,
                        targetSeen,
                        out targetSeen)
                ]));
        }

        if (MapZones.Contains(targetName))
        {
            batches.Add(new DbLoadXAssetsBatch(
                "LevelRetireUiAndGame",
                Synchronous: false,
                Requests:
                [
                    CreateFreeRequest(
                        XZoneFlags.DB_ZONE_UI | XZoneFlags.DB_ZONE_GAME,
                        targetSeen)
                ]));

            bool isCompositeSpecOps = TryGetSpecOpsBaseZone(targetName, out string? baseZone);
            batches.Add(new DbLoadXAssetsBatch(
                isCompositeSpecOps ? "LevelAddon" : "LevelGame",
                Synchronous: false,
                Requests:
                [
                    CreateLoadRequest(
                        targetName,
                        XZoneFlags.DB_ZONE_GAME,
                        XZoneFlags.None,
                        targetName,
                        targetOverride,
                        targetSeen,
                        out targetSeen)
                ]));

            if (isCompositeSpecOps)
            {
                batches.Add(new DbLoadXAssetsBatch(
                    "LevelBase",
                    Synchronous: false,
                    Requests:
                    [
                        CreateLoadRequest(
                            baseZone!,
                            XZoneFlags.DB_ZONE_GAME,
                            XZoneFlags.None,
                            targetName,
                            targetOverride,
                            targetSeen,
                            out targetSeen)
                    ],
                    SynchronizeBefore: true));
            }
        }

        return new DbZoneLoadPlan(targetName, scope, batches);
    }

    private DbZonePlanRequest CreateLoadRequest(
        string zoneName,
        XZoneFlags allocFlags,
        XZoneFlags freeFlags,
        string targetName,
        string? targetOverride,
        bool targetSeen,
        out bool updatedTargetSeen)
    {
        bool isTarget = string.Equals(zoneName, targetName, StringComparison.OrdinalIgnoreCase);
        DbZonePlanPosition position = isTarget
            ? DbZonePlanPosition.Target
            : targetSeen
                ? DbZonePlanPosition.AfterTarget
                : DbZonePlanPosition.BeforeTarget;
        updatedTargetSeen = targetSeen || isTarget;

        string path;
        bool exists;
        if (isTarget && targetOverride is not null)
        {
            path = targetOverride;
            exists = File.Exists(path);
        }
        else if (_catalog.TryGet(zoneName, out DbZoneCatalogEntry? catalogEntry))
        {
            path = catalogEntry.Path;
            exists = true;
        }
        else
        {
            path = _catalog.ExpectedPath(zoneName);
            exists = false;
        }

        bool missingIsNonFatal =
            !isTarget &&
            string.Equals(zoneName, PatchZone, StringComparison.OrdinalIgnoreCase);
        return new DbZonePlanRequest(
            new XZoneInfo(zoneName, allocFlags, freeFlags),
            path,
            exists,
            missingIsNonFatal,
            position);
    }

    private static DbZonePlanRequest CreateFreeRequest(
        XZoneFlags freeFlags,
        bool targetSeen)
    {
        return new DbZonePlanRequest(
            new XZoneInfo(null, XZoneFlags.None, freeFlags),
            FastFilePath: null,
            FileExists: true,
            MissingIsNonFatal: false,
            targetSeen ? DbZonePlanPosition.AfterTarget : DbZonePlanPosition.BeforeTarget);
    }

    private static XZoneFlags NativeAllocFlags(string targetName)
    {
        if (string.Equals(targetName, UiZone, StringComparison.OrdinalIgnoreCase))
            return XZoneFlags.DB_ZONE_UI;
        if (MapZones.Contains(targetName))
            return XZoneFlags.DB_ZONE_GAME;
        return XZoneFlags.DB_ZONE_COMMON;
    }

    /// <summary>
    /// Mirrors default.elf's add-on test: the name begins with "so_" and
    /// contains another underscore after that prefix. The base is the suffix
    /// following that second underscore.
    /// </summary>
    private static bool TryGetSpecOpsBaseZone(
        string targetName,
        out string? baseZone)
    {
        baseZone = null;
        if (!targetName.StartsWith("so_", StringComparison.OrdinalIgnoreCase))
            return false;

        int separator = targetName.IndexOf('_', "so_".Length);
        if (separator < 0)
            return false;

        baseZone = targetName[(separator + 1)..];
        return true;
    }

    private static bool IsStartupZone(string targetName) =>
        string.Equals(targetName, CodeZone, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(targetName, PatchZone, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(targetName, UiZone, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(targetName, CommonZone, StringComparison.OrdinalIgnoreCase);

    private static bool IsCoreZone(string targetName) =>
        string.Equals(targetName, CodeZone, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(targetName, PatchZone, StringComparison.OrdinalIgnoreCase);

    private static void ValidateSupportedTarget(string targetName)
    {
        if (SupportsTarget(targetName))
            return;

        throw new NotSupportedException(
            $"Zone '{targetName}' does not use the default single-player dependency lifecycle. " +
            "Open it in isolation or select a profile for its game mode.");
    }

    private static string? ResolveTargetOverride(string targetNameOrPath)
    {
        bool containsDirectory = targetNameOrPath.Contains(Path.DirectorySeparatorChar) ||
                                 targetNameOrPath.Contains(Path.AltDirectorySeparatorChar);
        return Path.IsPathRooted(targetNameOrPath) || containsDirectory
            ? Path.GetFullPath(targetNameOrPath)
            : null;
    }
}

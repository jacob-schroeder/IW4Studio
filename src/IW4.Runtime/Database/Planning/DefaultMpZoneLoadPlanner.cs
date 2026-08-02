using IW4.FastFiles.Zone;

namespace IW4.Runtime.Database.Planning;

/// <summary>
/// Builds the default multiplayer dependency lifecycle used by the engine.
/// Map targets are identified by the engine's mp_ naming convention so the
/// same pipeline works for stock and add-on maps.
/// </summary>
public sealed class DefaultMpZoneLoadPlanner
{
    private const string CodeZone = "code_post_gfx_mp";
    private const string PatchZone = "patch_mp";
    private const string Dlc2UiZone = "dlc2_ui_mp";
    private const string Dlc1UiZone = "dlc1_ui_mp";
    private const string UiZone = "ui_mp";
    private const string CommonZone = "common_mp";
    private readonly DbZoneCatalog _catalog;

    public DefaultMpZoneLoadPlanner(DbZoneCatalog catalog)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    /// <summary>
    /// Returns true for the default multiplayer startup zones and map/load
    /// zones following the engine's mp_ naming convention.
    /// </summary>
    public static bool SupportsTarget(string targetNameOrPath)
    {
        if (string.IsNullOrWhiteSpace(targetNameOrPath))
            return false;

        string targetName = DbZoneCatalog.NormalizeZoneName(targetNameOrPath);
        return string.Equals(targetName, CodeZone, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(targetName, PatchZone, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(targetName, Dlc2UiZone, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(targetName, Dlc1UiZone, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(targetName, UiZone, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(targetName, CommonZone, StringComparison.OrdinalIgnoreCase) ||
               IsMapZone(targetName, out _, out _);
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
    /// Builds the plan for one logical target while loading that target
    /// from an explicit candidate path. Dependencies continue to come from the
    /// planner's catalog. This is required for transactional validation because
    /// a temporary sibling's random filename is not the target's zone identity.
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
                CreateLoadRequest(CodeZone, XZoneFlags.DB_ZONE_COMMON, XZoneFlags.None, targetName, targetOverride, targetSeen, out targetSeen),
                CreateLoadRequest(PatchZone, XZoneFlags.DB_ZONE_COMMON, XZoneFlags.None, targetName, targetOverride, targetSeen, out targetSeen)
            ]));

        bool needsUiCommon = !IsCoreZone(targetName) || scope == DbZonePlanScope.StableRuntime;
        if (needsUiCommon)
        {
            batches.Add(new DbLoadXAssetsBatch(
                "StartupUiCommon",
                Synchronous: false,
                Requests:
                [
                    CreateLoadRequest(Dlc2UiZone, XZoneFlags.DB_ZONE_UI, XZoneFlags.None, targetName, targetOverride, targetSeen, out targetSeen),
                    CreateLoadRequest(Dlc1UiZone, XZoneFlags.DB_ZONE_UI, XZoneFlags.None, targetName, targetOverride, targetSeen, out targetSeen),
                    CreateLoadRequest(UiZone, XZoneFlags.DB_ZONE_UI, XZoneFlags.None, targetName, targetOverride, targetSeen, out targetSeen),
                    CreateLoadRequest(CommonZone, XZoneFlags.DB_ZONE_COMMON, XZoneFlags.None, targetName, targetOverride, targetSeen, out targetSeen)
                ]));
        }

        if (IsMapZone(targetName, out string mapName, out bool targetIsLoadZone))
        {
            string loadZoneName = mapName + "_load";
            batches.Add(new DbLoadXAssetsBatch(
                "LevelPreload",
                Synchronous: true,
                Requests:
                [
                    CreateLoadRequest(
                        loadZoneName,
                        XZoneFlags.DB_ZONE_LOAD,
                        XZoneFlags.DB_ZONE_LOAD | XZoneFlags.DB_ZONE_DEV,
                        targetName,
                        targetOverride,
                        targetSeen,
                        out targetSeen)
                ]));

            batches.Add(new DbLoadXAssetsBatch(
                "LevelRetireUiAndGame",
                Synchronous: false,
                Requests:
                [
                    CreateFreeRequest(
                        XZoneFlags.DB_ZONE_UI | XZoneFlags.DB_ZONE_GAME,
                        targetSeen)
                ]));

            batches.Add(new DbLoadXAssetsBatch(
                "LevelGame",
                Synchronous: false,
                Requests:
                [
                    CreateLoadRequest(
                        mapName,
                        XZoneFlags.DB_ZONE_GAME,
                        XZoneFlags.None,
                        targetName,
                        targetOverride,
                        targetSeen,
                        out targetSeen)
                ]));

            if (scope == DbZonePlanScope.StableRuntime || targetIsLoadZone)
            {
                batches.Add(new DbLoadXAssetsBatch(
                    "LevelRetireLoadAndDev",
                    Synchronous: false,
                    Requests:
                    [
                        CreateFreeRequest(
                            XZoneFlags.DB_ZONE_LOAD | XZoneFlags.DB_ZONE_DEV,
                            targetSeen)
                    ]));
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

        bool missingIsNonFatal = !isTarget && IsNativeMissingNonFatalZoneName(zoneName);
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
        if (string.Equals(targetName, UiZone, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(targetName, Dlc1UiZone, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(targetName, Dlc2UiZone, StringComparison.OrdinalIgnoreCase))
        {
            return XZoneFlags.DB_ZONE_UI;
        }

        if (targetName.EndsWith("_load", StringComparison.OrdinalIgnoreCase))
            return XZoneFlags.DB_ZONE_LOAD;
        if (targetName.StartsWith("mp_", StringComparison.OrdinalIgnoreCase))
            return XZoneFlags.DB_ZONE_GAME;
        return XZoneFlags.DB_ZONE_COMMON;
    }

    private static bool IsNativeMissingNonFatalZoneName(string zoneName)
    {
        return zoneName.StartsWith("patch", StringComparison.OrdinalIgnoreCase) ||
               zoneName.StartsWith("ez_", StringComparison.OrdinalIgnoreCase) ||
               zoneName.StartsWith("dlc", StringComparison.OrdinalIgnoreCase) ||
               zoneName.Contains("_load", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCoreZone(string targetName) =>
        string.Equals(targetName, CodeZone, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(targetName, PatchZone, StringComparison.OrdinalIgnoreCase);

    private static bool IsMapZone(
        string targetName,
        out string mapName,
        out bool targetIsLoadZone)
    {
        targetIsLoadZone = targetName.EndsWith("_load", StringComparison.OrdinalIgnoreCase);
        mapName = targetIsLoadZone ? targetName[..^"_load".Length] : targetName;
        return mapName.StartsWith("mp_", StringComparison.OrdinalIgnoreCase) &&
               mapName.Length > "mp_".Length;
    }

    private static void ValidateSupportedTarget(string targetName)
    {
        if (SupportsTarget(targetName))
            return;

        throw new NotSupportedException(
            $"Zone '{targetName}' does not use the default multiplayer dependency lifecycle. " +
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

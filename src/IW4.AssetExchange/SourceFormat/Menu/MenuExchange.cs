using IW4.Assets.Assets.Menu;

namespace IW4.AssetExchange.SourceFormat.Menu;

/// <summary>
/// Writes loaded IW4 MenuFile and Menu assets in the developer-facing menu
/// source format used by OpenAssetTools.
/// </summary>
public sealed class MenuExchange
{
    private static readonly StringComparer SourcePathComparer =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private readonly SourceOutput _output;
    private readonly Dictionary<string, MenuDumpingState> _menuStates =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _knownMenuFiles =
        new(SourcePathComparer);
    private readonly HashSet<string> _handledAssets =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _completedAssets =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _aggregateCompletedMenus =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _claimedAggregateMenus =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _writtenPaths =
        new(SourcePathComparer);
    private readonly object _writeGate = new();

    public MenuExchange(
        string sourceDirectory,
        IEnumerable<MenuFileAsset> menuFiles)
    {
        ArgumentNullException.ThrowIfNull(menuFiles);
        _output = new SourceOutput(sourceDirectory);

        foreach (MenuFileAsset menuFile in menuFiles)
        {
            ArgumentNullException.ThrowIfNull(menuFile);
            RegisterMenuFile(menuFile);
        }
    }

    /// <summary>
    /// Writes one MenuFile source file and each non-embedded Menu definition
    /// materialized by that MenuFile. External Menu references are load-only.
    /// </summary>
    public IReadOnlyList<string> Unlink(MenuFileAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        string menuFilePath = MenuFilePath(asset);
        if (!_knownMenuFiles.Contains(menuFilePath))
        {
            throw new InvalidOperationException(
                $"MenuFile '{menuFilePath}' was not part of this MenuExchange context.");
        }

        IReadOnlyList<ResolvedMenuRegistration> registrations =
            ResolveMenuRegistrations(asset, menuFilePath);
        string assetKey = MenuFileAssetKey(menuFilePath);
        var writes = new List<PendingSourceWrite>
        {
            new(
                assetKey,
                menuFilePath,
                textWriter => WriteMenuFileSource(
                    textWriter,
                    registrations,
                    menuFilePath),
                IsAggregateMenu: false)
        };

        var addedMenus = new HashSet<string>(StringComparer.Ordinal);
        foreach (ResolvedMenuRegistration registration in registrations)
        {
            if (!registration.Reference.Pointer.ConsumesSource ||
                registration.State.EmbeddedMenuFilePath is not null ||
                !SourcePathComparer.Equals(
                    registration.State.MaterializingMenuFilePath,
                    menuFilePath) ||
                !addedMenus.Add(registration.MenuIdentity))
            {
                continue;
            }

            writes.Add(CreateMenuSourceWrite(
                registration.Menu,
                registration.MenuIdentity,
                registration.State.Path,
                isAggregateMenu: true));
        }

        return WriteSourceBatch(writes);
    }

    /// <summary>
    /// Writes one standalone or MenuFile-owned Menu source file. An embedded
    /// Menu is already written by its MenuFile and therefore produces no file.
    /// </summary>
    public IReadOnlyList<string> Unlink(MenuDefAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        string menuName = CanonicalMenuName(asset);
        string menuIdentity = MenuIdentity(menuName);
        string assetKey = MenuAssetKey(menuIdentity);

        if (_menuStates.TryGetValue(menuIdentity, out MenuDumpingState? state) &&
            !MenuSourceWriter.SupportingDataEquivalent(
                state.ExpressionData,
                asset.ExpressionDataValue))
        {
            throw new InvalidDataException(
                $"Menu '{menuName}' has expression-supporting data that differs " +
                "from its MenuFile registration.");
        }

        if (state?.EmbeddedMenuFilePath is not null)
        {
            MarkEmbeddedMenuHandled(
                assetKey,
                state.EmbeddedMenuFilePath);
            return [];
        }

        if (TryClaimAggregateMenu(assetKey))
            return [];

        string menuPath = state?.Path ?? StandaloneMenuPath(asset);
        return WriteSourceBatch([
            CreateMenuSourceWrite(
                asset,
                menuIdentity,
                menuPath,
                isAggregateMenu: false)
        ]);
    }

    private void RegisterMenuFile(MenuFileAsset menuFile)
    {
        string menuFilePath = MenuFilePath(menuFile);
        if (!_knownMenuFiles.Add(menuFilePath))
        {
            throw new InvalidDataException(
                $"Duplicate MenuFile source path '{menuFilePath}'.");
        }

        ValidateMenuCount(menuFile);
        string parentPath = ParentPath(menuFilePath);
        foreach (MenuDefReference reference in menuFile.Menus)
        {
            MenuDefAsset? menu = SourceDefinition(reference);
            if (menu is null)
                continue;

            string menuName = CanonicalMenuName(menu);
            string menuIdentity = MenuIdentity(menuName);
            string derivedPath = $"{parentPath}{menuName}.menu";
            string? embeddedMenuFilePath = reference.Pointer.ConsumesSource &&
                SourcePathComparer.Equals(derivedPath, menuFilePath)
                ? menuFilePath
                : null;
            string? materializingMenuFilePath = reference.Pointer.ConsumesSource
                ? menuFilePath
                : null;

            if (!_menuStates.TryGetValue(menuIdentity, out MenuDumpingState? existing))
            {
                _menuStates.Add(
                    menuIdentity,
                    new MenuDumpingState(
                        derivedPath,
                        embeddedMenuFilePath,
                        materializingMenuFilePath,
                        menu.ExpressionDataValue));
                continue;
            }

            bool existingMaterializes =
                existing.MaterializingMenuFilePath is not null;
            bool candidateMaterializes =
                materializingMenuFilePath is not null;
            if (existingMaterializes &&
                candidateMaterializes &&
                !MenuSourceWriter.SupportingDataEquivalent(
                    existing.ExpressionData,
                    menu.ExpressionDataValue))
            {
                throw new InvalidDataException(
                    $"Duplicate source Menu '{menuName}' registrations have " +
                    "non-equivalent expression-supporting data.");
            }

            if (existing.MaterializingMenuFilePath is not null &&
                materializingMenuFilePath is not null &&
                !SourcePathComparer.Equals(
                    existing.MaterializingMenuFilePath,
                    materializingMenuFilePath))
            {
                throw new InvalidDataException(
                    $"Menu '{menuName}' is materialized by both " +
                    $"'{existing.MaterializingMenuFilePath}' and " +
                    $"'{materializingMenuFilePath}'.");
            }

            string? selectedMaterializingMenuFilePath =
                existing.MaterializingMenuFilePath ?? materializingMenuFilePath;
            ExpressionSupportingData? selectedExpressionData =
                !existingMaterializes && candidateMaterializes
                    ? menu.ExpressionDataValue
                    : existing.ExpressionData;

            // OpenAssetTools keeps the first parent-derived path, except that
            // an exact source-consuming MenuFile-name match wins so the Menu
            // can be embedded.
            if (existing.EmbeddedMenuFilePath is null &&
                embeddedMenuFilePath is not null)
            {
                _menuStates[menuIdentity] = new MenuDumpingState(
                    derivedPath,
                    embeddedMenuFilePath,
                    selectedMaterializingMenuFilePath,
                    selectedExpressionData);
            }
            else if (existing.EmbeddedMenuFilePath is not null &&
                     embeddedMenuFilePath is not null &&
                     !SourcePathComparer.Equals(
                         existing.EmbeddedMenuFilePath,
                         embeddedMenuFilePath))
            {
                throw new InvalidDataException(
                    $"Menu '{menuName}' is embedded by both " +
                    $"'{existing.EmbeddedMenuFilePath}' and '{embeddedMenuFilePath}'.");
            }
            else if (!existingMaterializes && candidateMaterializes)
            {
                _menuStates[menuIdentity] = existing with
                {
                    MaterializingMenuFilePath = selectedMaterializingMenuFilePath,
                    ExpressionData = selectedExpressionData
                };
            }
        }
    }

    private IReadOnlyList<string> WriteSourceBatch(
        IReadOnlyList<PendingSourceWrite> requestedWrites)
    {
        if (requestedWrites.Count == 0)
            return [];

        var writes = new List<PendingSourceWrite>(requestedWrites.Count);
        lock (_writeGate)
        {
            try
            {
                foreach (PendingSourceWrite requested in requestedWrites)
                {
                    if (requested.IsAggregateMenu &&
                        _completedAssets.Contains(requested.AssetKey))
                    {
                        continue;
                    }

                    if (!_handledAssets.Add(requested.AssetKey))
                    {
                        throw new InvalidOperationException(
                            $"Asset '{requested.AssetKey}' was already unlinked.");
                    }

                    if (!_writtenPaths.Add(requested.RelativePath))
                    {
                        _handledAssets.Remove(requested.AssetKey);
                        throw new InvalidOperationException(
                            $"Source path '{requested.RelativePath}' was already written " +
                            "by another asset.");
                    }

                    writes.Add(requested);
                }
            }
            catch
            {
                ReleaseWrites(writes);
                throw;
            }
        }

        try
        {
            IReadOnlyList<string> outputPaths = _output.WriteTextBatch(
                writes.Select(write => (
                    write.RelativePath,
                    write.Write)));
            lock (_writeGate)
            {
                foreach (PendingSourceWrite write in writes)
                {
                    _completedAssets.Add(write.AssetKey);
                    if (write.IsAggregateMenu)
                        _aggregateCompletedMenus.Add(write.AssetKey);
                }
            }

            return outputPaths;
        }
        catch
        {
            lock (_writeGate)
                ReleaseWrites(writes);

            throw;
        }
    }

    private void ReleaseWrites(IEnumerable<PendingSourceWrite> writes)
    {
        foreach (PendingSourceWrite write in writes)
        {
            _handledAssets.Remove(write.AssetKey);
            _completedAssets.Remove(write.AssetKey);
            _aggregateCompletedMenus.Remove(write.AssetKey);
            _writtenPaths.Remove(write.RelativePath);
        }
    }

    private bool TryClaimAggregateMenu(string assetKey)
    {
        lock (_writeGate)
        {
            if (!_aggregateCompletedMenus.Contains(assetKey))
                return false;

            if (!_claimedAggregateMenus.Add(assetKey))
            {
                throw new InvalidOperationException(
                    $"Asset '{assetKey}' was already unlinked.");
            }

            return true;
        }
    }

    private void MarkEmbeddedMenuHandled(
        string assetKey,
        string menuFilePath)
    {
        lock (_writeGate)
        {
            string owningAssetKey = MenuFileAssetKey(menuFilePath);
            if (!_completedAssets.Contains(owningAssetKey))
            {
                throw new InvalidOperationException(
                    $"Embedded Menu '{assetKey["menu:".Length..]}' was not written because " +
                    $"its owning MenuFile '{menuFilePath}' did not complete successfully.");
            }

            if (!_handledAssets.Add(assetKey))
                throw new InvalidOperationException($"Asset '{assetKey}' was already unlinked.");
            _completedAssets.Add(assetKey);
        }
    }

    private IReadOnlyList<ResolvedMenuRegistration> ResolveMenuRegistrations(
        MenuFileAsset menuFile,
        string menuFilePath)
    {
        ValidateMenuCount(menuFile);
        var registrations = new List<ResolvedMenuRegistration>(
            menuFile.Menus.Count);
        foreach (MenuDefReference reference in menuFile.Menus)
        {
            MenuDefAsset? menu = SourceDefinition(reference);
            if (menu is null)
                continue;

            string menuName = CanonicalMenuName(menu);
            string menuIdentity = MenuIdentity(menuName);
            if (!_menuStates.TryGetValue(
                    menuIdentity,
                    out MenuDumpingState? state))
            {
                throw new InvalidDataException(
                    $"Menu '{menuName}' has no path in the MenuExchange context.");
            }

            if (reference.Pointer.ConsumesSource &&
                !MenuSourceWriter.SupportingDataEquivalent(
                    state.ExpressionData,
                    menu.ExpressionDataValue))
            {
                throw new InvalidDataException(
                    $"Menu '{menuName}' has expression-supporting data that differs " +
                    "from its MenuExchange registration.");
            }

            if (reference.Pointer.ConsumesSource &&
                state.MaterializingMenuFilePath is not null &&
                !SourcePathComparer.Equals(
                    state.MaterializingMenuFilePath,
                    menuFilePath))
            {
                throw new InvalidDataException(
                    $"Menu '{menuName}' is not owned by MenuFile '{menuFilePath}'.");
            }

            registrations.Add(new ResolvedMenuRegistration(
                reference,
                menu,
                menuIdentity,
                state));
        }

        return registrations.AsReadOnly();
    }

    private static MenuDefAsset? SourceDefinition(MenuDefReference reference) =>
        reference.Pointer.ConsumesSource
            ? reference.SourceMenu ?? reference.CanonicalMenu
            : reference.CanonicalMenu;

    private static void WriteMenuFileSource(
        TextWriter textWriter,
        IReadOnlyList<ResolvedMenuRegistration> registrations,
        string menuFilePath)
    {
        var writer = new MenuSourceWriter(textWriter);
        writer.Start();
        writer.WriteSharedFunctionDefinitions(
            registrations
                .Where(registration => registration.Reference.Pointer.ConsumesSource)
                .Select(registration => registration.Menu));

        foreach (ResolvedMenuRegistration registration in registrations)
        {
            if (registration.Reference.Pointer.ConsumesSource &&
                registration.State.EmbeddedMenuFilePath is not null &&
                SourcePathComparer.Equals(
                    registration.State.EmbeddedMenuFilePath,
                    menuFilePath))
            {
                writer.WriteMenu(registration.Menu);
            }
            else
            {
                writer.IncludeMenu(registration.State.Path);
            }
        }

        writer.End();
    }

    private static PendingSourceWrite CreateMenuSourceWrite(
        MenuDefAsset menu,
        string menuIdentity,
        string menuPath,
        bool isAggregateMenu) =>
        new(
            MenuAssetKey(menuIdentity),
            menuPath,
            textWriter => WriteMenuSource(textWriter, menu),
            isAggregateMenu);

    private static void WriteMenuSource(
        TextWriter textWriter,
        MenuDefAsset menu)
    {
        var writer = new MenuSourceWriter(textWriter);
        writer.Start();
        writer.WriteMenu(menu);
        writer.End();
    }

    private static void ValidateMenuCount(MenuFileAsset menuFile)
    {
        if (menuFile.MenuCount < 0)
        {
            throw new InvalidDataException(
                $"MenuFile '{menuFile.Name}' has invalid MenuCount {menuFile.MenuCount}.");
        }

        if (menuFile.MenuCount != menuFile.Menus.Count)
        {
            throw new InvalidDataException(
                $"MenuFile '{menuFile.Name}' declares {menuFile.MenuCount} menus " +
                $"but exposes {menuFile.Menus.Count} resolved registrations.");
        }
    }

    private static string MenuFilePath(MenuFileAsset menuFile)
    {
        if (string.IsNullOrEmpty(menuFile.Name))
            throw new InvalidDataException("A MenuFile cannot be unlinked without a name.");

        return NormalizeSourcePath(menuFile.Name);
    }

    private static string CanonicalMenuName(MenuDefAsset menu)
    {
        string? name = menu.Window.Name;
        if (string.IsNullOrEmpty(name))
            throw new InvalidDataException("A Menu cannot be unlinked without a window name.");

        string canonicalName = name[0] == ',' ? name[1..] : name;
        if (canonicalName.Length == 0)
            throw new InvalidDataException("A Menu window name cannot contain only an external-asset marker.");

        return NormalizeSourcePath(canonicalName);
    }

    private static string StandaloneMenuPath(MenuDefAsset menu)
    {
        string? name = menu.Window.Name;
        if (string.IsNullOrEmpty(name))
            throw new InvalidDataException("A Menu cannot be unlinked without a window name.");

        return $"ui_mp/{NormalizeSourcePath(name)}.menu";
    }

    private static string ParentPath(string path)
    {
        int separator = path.LastIndexOf('/');
        return separator < 0 ? string.Empty : path[..(separator + 1)];
    }

    private static string NormalizeSourcePath(string path) =>
        path.Replace('\\', '/');

    private static string MenuIdentity(string canonicalName) =>
        canonicalName.ToLowerInvariant();

    private static string MenuFileAssetKey(string menuFilePath) =>
        $"menufile:{menuFilePath.ToLowerInvariant()}";

    private static string MenuAssetKey(string menuIdentity) =>
        $"menu:{menuIdentity}";

    private sealed record MenuDumpingState(
        string Path,
        string? EmbeddedMenuFilePath,
        string? MaterializingMenuFilePath,
        ExpressionSupportingData? ExpressionData);

    private sealed record ResolvedMenuRegistration(
        MenuDefReference Reference,
        MenuDefAsset Menu,
        string MenuIdentity,
        MenuDumpingState State);

    private sealed record PendingSourceWrite(
        string AssetKey,
        string RelativePath,
        Action<TextWriter> Write,
        bool IsAggregateMenu);
}

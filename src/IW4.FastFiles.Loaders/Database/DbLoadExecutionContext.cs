using IW4.Runtime.Database;
using IW4.Assets.Zone;
using System.Buffers.Binary;
using IW4.Assets.Assets;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Leaderboard;
using IW4.Assets.Assets.Localize;
using IW4.Assets.Assets.Menu;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.Physics;
using IW4.Assets.Assets.RawFile;
using IW4.Assets.Assets.Sound;
using IW4.Assets.Assets.StringTable;
using IW4.Assets.Assets.StructuredData;
using IW4.Assets.Assets.TechniqueSet;
using IW4.Assets.Assets.Tracer;
using IW4.Assets.Assets.Weapon;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Database.Streaming;
using IW4.FastFiles.Zone;
using IW4.Runtime.Assets;
using IW4.Runtime.Diagnostics;
using IW4.FastFiles.Loaders.Pointers;
using IW4.Runtime.Strings;

namespace IW4.FastFiles.Loaders.Database;

/// <summary>
/// Loader-owned execution state for PS3-shaped asset readers. Runtime creates
/// and supplies the load session; this context owns only loader mechanics and
/// the typed registration operations required by migrated readers. This is a
/// managed implementation aid, not an original IW4 type.
/// </summary>
public class DbLoadExecutionContext
{
    private readonly XAssetLoadSession _assetLoadSession;
    private readonly TypedMaterializationRegistry _typedMaterializations = new();
    private readonly Dictionary<XBlockAddress, MaterialVertexDeclarationAsset>
        _materialVertexDeclarationsByAddress = new();
    private readonly Dictionary<XBlockAddress, MaterialTechniqueAsset>
        _materialTechniquesByAddress = new();
    private readonly Dictionary<XBlockAddress, MaterialAsset> _materialsByAddress = new();
    private readonly Dictionary<XBlockAddress, IReadOnlyList<uint>>
        _gfxStateLoadBitsByAliasCell = new();
    // Recursive Menu payloads are allowed to point back into an earlier
    // block-stream object.  Keep those materializations per load context so
    // an offset reuses the same managed graph node rather than turning into
    // a null child after its source was already consumed.
    private readonly Dictionary<XBlockAddress, object> _menuObjectsByAddress = new();
    // ColMap CPlane pointers can legally compact to an earlier LARGE-stream
    // address.  Unlike an asset-pool alias, that address names an authored
    // geometry record, so retain its materialization for later pointer cells.
    private readonly Dictionary<XBlockAddress, CPlane> _clipPlanesByAddress = new();
    private readonly MaterialTechniqueStateCache _materialTechniqueStateCache;

    public DbLoadExecutionContext(
        XAssetLoadSession assetLoadSession,
        MaterialTechniqueStateCache? materialTechniqueStateCache = null)
    {
        _assetLoadSession = assetLoadSession
            ?? throw new ArgumentNullException(nameof(assetLoadSession));
        _materialTechniqueStateCache =
            materialTechniqueStateCache ?? new MaterialTechniqueStateCache();
        Blocks = new DbStreamState();
        PointerReader = new XFilePointerReader(Blocks, _assetLoadSession.AssetPool);
        Diagnostics = new LoadDiagnostics();
    }

    public DbStreamState Blocks { get; }

    public XFilePointerReader PointerReader { get; }

    public LoadDiagnostics Diagnostics { get; }

    /// <summary>
    /// Registers a nested semantic node at the stable block address produced
    /// when its inline payload was materialized.
    /// </summary>
    internal T RegisterMaterialized<T>(XBlockAddress address, T value, string targetName)
        where T : class =>
        _typedMaterializations.Register(address, value, targetName);

    /// <summary>
    /// Resolves a nested direct pointer to the semantic node materialized at
    /// its packed block address. Callers remain responsible for validating the
    /// target's serialized range and for supporting their field's semantics.
    /// </summary>
    internal T ResolveMaterializedDirect<T>(XPointerReference pointer, string targetName)
        where T : class =>
        _typedMaterializations.ResolveDirectOffset<T>(pointer, targetName);

    internal bool TryGetMaterialized<T>(XBlockAddress address, out T? value)
        where T : class =>
        _typedMaterializations.TryGet(address, out value);

    internal T RegisterMaterializedView<T>(
        XBlockAddress address,
        string serializedView,
        T value,
        string targetName)
        where T : class =>
        _typedMaterializations.RegisterView(
            address,
            serializedView,
            value,
            targetName);

    internal bool TryGetMaterializedView<T>(
        XBlockAddress address,
        string serializedView,
        out T? value)
        where T : class =>
        _typedMaterializations.TryGetView(
            address,
            serializedView,
            out value);

    public bool TryGetMenuObject<T>(XBlockAddress address, out T? value)
        where T : class
    {
        if (_menuObjectsByAddress.TryGetValue(address, out object? stored) && stored is T typed)
        {
            value = typed;
            return true;
        }

        value = null;
        return false;
    }

    public void RegisterMenuObject<T>(XBlockAddress address, T value)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(value);
        if (_menuObjectsByAddress.TryGetValue(address, out object? existing) && !ReferenceEquals(existing, value))
            throw new InvalidDataException($"Menu graph address {address} was materialized as both {existing.GetType().Name} and {typeof(T).Name}.");
        _menuObjectsByAddress[address] = value;
    }

    public bool TryGetClipPlane(XBlockAddress address, out CPlane? value) =>
        _clipPlanesByAddress.TryGetValue(address, out value);

    public void RegisterClipPlane(XBlockAddress address, CPlane value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (_clipPlanesByAddress.TryGetValue(address, out CPlane? existing) && !ReferenceEquals(existing, value))
            throw new InvalidDataException($"ColMap CPlane address {address} was materialized more than once.");
        _clipPlanesByAddress[address] = value;
    }

    public ZoneScriptStringTable ZoneScriptStrings => _assetLoadSession.ZoneScriptStrings;

    protected XAssetLoadSession AssetLoadSessionCore => _assetLoadSession;

    protected MaterialTechniqueStateCache MaterialTechniqueStateCacheCore =>
        _materialTechniqueStateCache;

    protected IReadOnlyDictionary<XBlockAddress, MaterialAsset> MaterialsByAddressCore =>
        _materialsByAddress;

    /// <summary>
    /// Common registration boundary for every typed DB_AddXAsset wrapper.
    /// It captures the incoming provider before pool selection can replace the
    /// caller-visible asset with an earlier canonical provider, then reports
    /// the exact provider id and current active provider to a derived context.
    /// </summary>
    protected XAssetPoolEntry RegisterAsset(
        XAssetType serializedType,
        XAssetType canonicalFamily,
        string? originalName,
        BaseAsset asset,
        XBlockAddress stagingAddress,
        ReadOnlySpan<byte> headerBytes,
        XBlockAddress pointerCellAddress,
        out bool added,
        DbStreamState? sourceBlocks = null,
        ReadOnlySpan<byte> nativePoolCopyBytes = default,
        int? nativePoolCopyCapturedLength = null)
    {
        ArgumentNullException.ThrowIfNull(asset);
        string capturedOriginalName = originalName ?? string.Empty;

        XAssetPoolEntry entry = _assetLoadSession.RegisterAsset(
            canonicalFamily,
            capturedOriginalName,
            asset,
            stagingAddress,
            headerBytes,
            out added,
            out XAssetProviderId providerId,
            sourceBlocks,
            nativePoolCopyBytes,
            nativePoolCopyCapturedLength);
        if (!_assetLoadSession.AssetPool.TryGetSlot(entry.Address, out XAssetSlot? slot) ||
            slot is null)
        {
            throw new InvalidDataException(
                $"DB_AddXAsset registered {canonicalFamily} '{capturedOriginalName}', " +
                "but its canonical slot cannot be recovered for provenance capture.");
        }

        XAssetProviderContribution incoming = slot.Providers.SingleOrDefault(
            provider => provider.Id == providerId)
            ?? throw new InvalidDataException(
                $"DB_AddXAsset registered provider {providerId}, but the canonical slot does not contain it.");
        XAssetProviderContribution active = slot.ActiveProvider;
        var incomingMaterialization = new XAssetProviderMaterialization(
            incoming.Id,
            new XAssetStableIdentity(serializedType, canonicalFamily, capturedOriginalName),
            capturedOriginalName,
            asset,
            incoming.IsReferencePlaceholder
                ? XAssetProviderRegistrationDisposition.ReferencePlaceholder
                : XAssetProviderRegistrationDisposition.FullDefinition);
        OnAssetProviderRegistered(
            pointerCellAddress,
            incomingMaterialization,
            active.Id);
        return entry;
    }

    protected virtual void OnAssetProviderRegistered(
        XBlockAddress pointerCellAddress,
        XAssetProviderMaterialization provider,
        XAssetProviderId activeProviderId)
    {
    }

    internal bool TryGetCanonicalXModelSurfsEntry(
        XModelSurfsAsset asset,
        out XAssetPoolEntry entry) =>
        _assetLoadSession.AssetPool.TryGetEntry(asset, out entry);

    /// <summary>
    /// Retains the immutable two-word loadBits payload addressed by a
    /// GfxStateBits pointer cell. The PS3 material loader permits later rows to
    /// alias that cell while the pointed-to TEMP bytes are staging storage and
    /// can be rewound. The stable alias-cell identity therefore owns the
    /// managed snapshot; the transient target address does not.
    /// </summary>
    internal IReadOnlyList<uint> RegisterGfxStateLoadBits(
        XBlockAddress aliasCell,
        IReadOnlyList<uint> loadBits)
    {
        ArgumentNullException.ThrowIfNull(loadBits);
        if (loadBits.Count != 2)
        {
            throw new InvalidDataException(
                $"GfxStateBits loadBits at {aliasCell} contains {loadBits.Count} words; expected exactly 2.");
        }

        if (_gfxStateLoadBitsByAliasCell.TryGetValue(aliasCell, out IReadOnlyList<uint>? existing))
        {
            if (existing[0] != loadBits[0] || existing[1] != loadBits[1])
            {
                throw new InvalidDataException(
                    $"GfxStateBits alias cell {aliasCell} was registered with conflicting loadBits " +
                    $"0x{existing[0]:X8}/0x{existing[1]:X8} and 0x{loadBits[0]:X8}/0x{loadBits[1]:X8}.");
            }

            return existing;
        }

        IReadOnlyList<uint> snapshot = Array.AsReadOnly([loadBits[0], loadBits[1]]);
        _gfxStateLoadBitsByAliasCell.Add(aliasCell, snapshot);
        return snapshot;
    }

    internal IReadOnlyList<uint> ResolveGfxStateLoadBits(XPointerReference pointer)
    {
        if (pointer.Type != PointerType.Offset ||
            pointer.ResolutionMode != XPointerResolutionMode.AliasCell ||
            pointer.PackedAddress is not { } aliasCell)
        {
            throw new InvalidDataException(
                $"GfxStateBits loadBits pointer 0x{unchecked((uint)pointer.Raw):X8} is not a packed alias-cell pointer.");
        }

        if (_gfxStateLoadBitsByAliasCell.TryGetValue(aliasCell, out IReadOnlyList<uint>? loadBits))
            return loadBits;

        throw new InvalidDataException(
            $"GfxStateBits loadBits alias cell {aliasCell} has no immutable payload snapshot.");
    }

    /// <summary>
    /// Retains a semantic declaration at its materialized block address. PS3
    /// material passes commonly reuse declarations through packed direct
    /// pointers, so validating the bytes is insufficient: later passes must
    /// recover the already-loaded declaration object without consuming source.
    /// </summary>
    internal MaterialVertexDeclarationAsset
        RegisterMaterialVertexDeclaration(
            XBlockAddress address,
            MaterialVertexDeclarationAsset declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        if (address.BlockType != XFileBlockType.LARGE)
        {
            throw new InvalidDataException(
                $"MaterialVertexDeclaration semantic owner {address} is not in the LARGE stream.");
        }
        if (_materialVertexDeclarationsByAddress.TryGetValue(
                address,
                out MaterialVertexDeclarationAsset? existing))
        {
            if (existing.StreamCount != declaration.StreamCount ||
                existing.HasOptionalSource != declaration.HasOptionalSource ||
                !existing.Routing.SequenceEqual(declaration.Routing))
            {
                throw new InvalidDataException(
                    $"MaterialVertexDeclaration {address} was registered with conflicting layouts.");
            }

            return existing;
        }

        _materialVertexDeclarationsByAddress.Add(address, declaration);
        return declaration;
    }

    internal MaterialVertexDeclarationAsset
        ResolveMaterialVertexDeclaration(XPointerReference pointer)
    {
        if (pointer.Type != PointerType.Offset ||
            pointer.ResolutionMode != XPointerResolutionMode.Direct ||
            pointer.PackedAddress is not { } address)
        {
            throw new InvalidDataException(
                $"MaterialVertexDeclaration pointer 0x{unchecked((uint)pointer.Raw):X8} is not a packed direct pointer.");
        }

        if (_materialVertexDeclarationsByAddress.TryGetValue(
                address,
                out MaterialVertexDeclarationAsset? declaration))
        {
            return declaration;
        }

        throw new InvalidDataException(
            $"Packed MaterialVertexDeclaration target {address} has no earlier materialized semantic owner.");
    }

    internal MaterialTechniqueAsset RegisterMaterialTechnique(
        XBlockAddress address,
        MaterialTechniqueAsset technique)
    {
        ArgumentNullException.ThrowIfNull(technique);
        if (address.BlockType != XFileBlockType.LARGE)
        {
            throw new InvalidDataException(
                $"MaterialTechnique semantic owner {address} is not in the LARGE stream.");
        }
        if (_materialTechniquesByAddress.TryGetValue(
                address,
                out MaterialTechniqueAsset? existing))
        {
            if (existing.Flags != technique.Flags ||
                existing.PassCount != technique.PassCount ||
                !string.Equals(
                    existing.Name,
                    technique.Name,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"MaterialTechnique {address} was registered with conflicting roots.");
            }

            return existing;
        }

        _materialTechniquesByAddress.Add(address, technique);
        return technique;
    }

    internal MaterialTechniqueAsset ResolveMaterialTechnique(
        XPointerReference pointer)
    {
        if (pointer.Type != PointerType.Offset ||
            pointer.ResolutionMode != XPointerResolutionMode.Direct ||
            pointer.PackedAddress is not { } address)
        {
            throw new InvalidDataException(
                $"MaterialTechnique pointer 0x{unchecked((uint)pointer.Raw):X8} is not a packed direct pointer.");
        }

        if (_materialTechniquesByAddress.TryGetValue(
                address,
                out MaterialTechniqueAsset? technique))
        {
            return technique;
        }

        throw new InvalidDataException(
            $"Packed MaterialTechnique target {address} has no earlier materialized semantic owner.");
    }

    /// <summary>
    /// Executes the common DB_AddXAsset registration path for an asset whose
    /// native pool copy is exactly its serialized root. Asset families with a
    /// larger pool projection or specialized post-registration policy retain
    /// dedicated overloads.
    /// </summary>
    public TAsset DB_AddXAsset<TAsset>(
        XAssetType serializedType,
        string? name,
        TAsset asset,
        XBlockAddress pointerCellAddress)
        where TAsset : BaseAsset
    {
        XAssetTypeRuntimeMetadata metadata = XAssetTypeRuntimeMetadataCatalog.Get(serializedType);
        if (!metadata.HasCanonicalRegistration)
        {
            throw new InvalidOperationException(
                $"Serialized XAsset type {serializedType} has no canonical pool registration.");
        }

        if (metadata.NativePoolCopySize != metadata.SerializedRootSize)
        {
            throw new InvalidOperationException(
                $"{serializedType} requires a dedicated DB_AddXAsset implementation because its native pool copy " +
                $"is 0x{metadata.NativePoolCopySize:X} bytes while its serialized root is 0x{metadata.SerializedRootSize:X} bytes.");
        }

        XBlockAddress stagingAddress = asset.StagingAddress
            ?? throw new InvalidDataException(
                $"{serializedType} has no staging block address for DB_AddXAsset canonicalization.");
        byte[] headerBytes = Blocks.ReadBytes(stagingAddress, metadata.SerializedRootSize);
        XAssetPoolEntry entry = RegisterAsset(
            serializedType,
            metadata.CanonicalType,
            name,
            asset,
            stagingAddress,
            headerBytes,
            pointerCellAddress,
            out bool added,
            Blocks);
        if (entry.Asset is not TAsset canonical)
        {
            throw new InvalidDataException(
                $"Canonical {metadata.CanonicalType} asset '{entry.Name}' has managed type " +
                $"{entry.Asset.GetType().Name}, expected {typeof(TAsset).Name}.");
        }

        Blocks.WriteInt32(pointerCellAddress, entry.Address.RawValue);
        return canonical;
    }

    /// <summary>
    /// Resolves either a direct canonical pool pointer or an alias cell for a
    /// dispatcher-supported XAsset type. ColMapSp intentionally resolves in
    /// the native ColMapMp canonical family.
    /// </summary>
    public TAsset? ResolveCanonicalAsset<TAsset>(
        XPointerReference pointer,
        XAssetType serializedType)
        where TAsset : BaseAsset
    {
        XAssetTypeRuntimeMetadata metadata = XAssetTypeRuntimeMetadataCatalog.Get(serializedType);
        if (!metadata.HasCanonicalRegistration)
            return null;

        return ResolveSimpleCanonicalAsset<TAsset>(pointer, metadata.CanonicalType);
    }

    /// <summary>
    /// Applies the ColMap-specific pre-registration mutation. Both serialized
    /// type 0x0D and 0x0E use the shared loader and are registered as native
    /// ColMapMp (0x0E).
    /// </summary>
    public ClipMapAsset DB_AddXAsset(
        XAssetType serializedType,
        ClipMapAsset clipMap,
        XBlockAddress pointerCellAddress)
    {
        if (serializedType is not (XAssetType.ColMapSp or XAssetType.ColMapMp))
        {
            throw new ArgumentOutOfRangeException(
                nameof(serializedType),
                serializedType,
                "The ClipMap DB_AddXAsset wrapper only accepts ColMapSp or ColMapMp.");
        }

        XAssetTypeRuntimeMetadata metadata = XAssetTypeRuntimeMetadataCatalog.Get(serializedType);
        if (metadata.CanonicalType != XAssetType.ColMapMp ||
            metadata.SerializedRootSize != ClipMapAsset.SerializedSize ||
            metadata.NativePoolCopySize != ClipMapAsset.SerializedSize)
        {
            throw new InvalidOperationException(
                $"{serializedType} runtime metadata does not describe the required 0x100-byte ColMapMp pool copy.");
        }

        XBlockAddress stagingAddress = clipMap.StagingAddress
            ?? throw new InvalidDataException("ColMap has no staging block address for DB_AddXAsset canonicalization.");

        // Mark the staged root in use immediately before registration.
        Blocks.WriteInt32(stagingAddress.Add(sizeof(int)), 1);
        clipMap.MarkInUseForRegistration();

        return DB_AddXAsset(
            serializedType,
            clipMap.Name,
            clipMap,
            pointerCellAddress);
    }

    // The serialized WeaponVariantDef body is 0x74 bytes, while its native
    // pool copy is 0x684 bytes. Capture the complete current TEMP scratch
    // window, including nested staging remnants after +0x74; the separately
    // loaded 0x684-byte WeaponDef remains in LARGE.
    public WeaponAsset DB_AddXAsset(
        WeaponAsset weapon,
        XBlockAddress pointerCellAddress)
    {
        XBlockAddress stagingAddress = weapon.StagingAddress
            ?? throw new InvalidDataException("Weapon has no staging block address for DB_AddXAsset canonicalization.");
        byte[] headerBytes = Blocks.ReadBytes(stagingAddress, WeaponAsset.SerializedSize);
        byte[] nativePoolCopyBytes = CaptureWeaponNativePoolCopy(
            stagingAddress,
            out int nativePoolCopyCapturedLength);
        XAssetPoolEntry entry = RegisterAsset(
            XAssetType.Weapon,
            XAssetType.Weapon,
            weapon.Name,
            weapon,
            stagingAddress,
            headerBytes,
            pointerCellAddress,
            out bool added,
            Blocks,
            nativePoolCopyBytes,
            nativePoolCopyCapturedLength);
        var canonical = (WeaponAsset)entry.Asset;

        Blocks.WriteInt32(pointerCellAddress, entry.Address.RawValue);
        return canonical;
    }

    private byte[] CaptureWeaponNativePoolCopy(
        XBlockAddress stagingAddress,
        out int capturedLength)
    {
        if (stagingAddress.BlockType != XFileBlockType.TEMP)
        {
            throw new InvalidDataException(
                $"Weapon staging address {stagingAddress} is not in the TEMP block.");
        }

        XZoneMemory memory = Blocks.ZoneMemory
            ?? throw new InvalidOperationException("Cannot snapshot a Weapon pool copy before DB_InitStreams.");
        byte[] source = memory[XFileBlockType.TEMP].Data;
        if ((uint)stagingAddress.Offset > (uint)source.Length)
            throw new InvalidDataException($"Weapon staging address {stagingAddress} lies outside TEMP.");

        var copy = new byte[WeaponAsset.NativePoolCopySize];
        capturedLength = Math.Min(copy.Length, source.Length - stagingAddress.Offset);
        source.AsSpan(stagingAddress.Offset, capturedLength).CopyTo(copy);
        return copy;
    }

    // Copy the completed 0x0C-byte MenuFile header from TEMP into the
    // canonical type-0x18 pool.
    public MenuFileAsset DB_AddXAsset(
        MenuFileAsset menuFile,
        XBlockAddress pointerCellAddress)
    {
        XBlockAddress stagingAddress = menuFile.StagingAddress
            ?? throw new InvalidDataException("MenuFile has no staging block address for DB_AddXAsset canonicalization.");
        byte[] headerBytes = Blocks.ReadBytes(stagingAddress, MenuFileAsset.SerializedSize);
        XAssetPoolEntry entry = RegisterAsset(
            XAssetType.MenuFile,
            XAssetType.MenuFile,
            menuFile.Name,
            menuFile,
            stagingAddress,
            headerBytes,
            pointerCellAddress,
            out bool added,
            Blocks);
        var canonical = (MenuFileAsset)entry.Asset;

        Blocks.WriteInt32(pointerCellAddress, entry.Address.RawValue);
        return canonical;
    }

    // Copy the completed 0x2F0-byte MenuDef root from TEMP into the canonical
    // type-0x19 pool, then walk the original staging root's ItemDef table and
    // write the canonical Menu identity to every copied ItemDef owner cell.
    public MenuDefAsset DB_AddXAsset(
        MenuDefAsset menu,
        XBlockAddress pointerCellAddress)
    {
        XBlockAddress stagingAddress = menu.StagingAddress
            ?? throw new InvalidDataException("MenuDef has no staging block address for DB_AddXAsset canonicalization.");
        byte[] headerBytes = Blocks.ReadBytes(stagingAddress, MenuDefAsset.SerializedSize);
        XAssetPoolEntry entry = RegisterAsset(
            XAssetType.Menu,
            XAssetType.Menu,
            menu.Window.Name,
            menu,
            stagingAddress,
            headerBytes,
            pointerCellAddress,
            out bool added,
            Blocks);
        var canonical = (MenuDefAsset)entry.Asset;

        // Patch the caller before walking the original staging root, even
        // when DB_AddXAsset deduplicated to an older asset.
        Blocks.WriteInt32(pointerCellAddress, entry.Address.RawValue);
        PatchItemRuntimeParents(menu, entry.Address);

        return canonical;
    }

    private void PatchItemRuntimeParents(
        MenuDefAsset stagingMenu,
        XAssetPoolAddress canonicalAddress)
    {
        if (stagingMenu.ItemCount <= 0)
            return;

        XBlockAddress stagingAddress = stagingMenu.StagingAddress
            ?? throw new InvalidDataException("MenuDef has no staging address for ItemDef owner patching.");
        int itemsRaw = Blocks.ReadInt32(stagingAddress.Add(0x128));
        if (!XPointerCodec.TryDecodeBlockAddress(itemsRaw, out XBlockAddress itemTableAddress))
        {
            throw new InvalidDataException(
                $"MenuDef {stagingMenu.Window.Name ?? "<unnamed>"} has {stagingMenu.ItemCount} item(s), " +
                $"but its materialized +0x128 table pointer is 0x{unchecked((uint)itemsRaw):X8}.");
        }

        Blocks.ValidateMaterializedRange(
            itemTableAddress,
            checked(stagingMenu.ItemCount * sizeof(int)),
            $"MenuDef {stagingMenu.Window.Name ?? "<unnamed>"} ItemDef table",
            itemsRaw);

        for (int i = 0; i < stagingMenu.ItemCount; i++)
        {
            int itemRaw = Blocks.ReadInt32(itemTableAddress.Add(checked(i * sizeof(int))));
            if (!XPointerCodec.TryDecodeBlockAddress(itemRaw, out XBlockAddress itemAddress))
            {
                throw new InvalidDataException(
                    $"MenuDef {stagingMenu.Window.Name ?? "<unnamed>"} ItemDef[{i}] has invalid " +
                    $"materialized pointer 0x{unchecked((uint)itemRaw):X8}.");
            }

            Blocks.ValidateMaterializedRange(
                itemAddress,
                ItemDefAsset.SerializedSize,
                $"MenuDef {stagingMenu.Window.Name ?? "<unnamed>"} ItemDef[{i}]",
                itemRaw);
            Blocks.WriteInt32(itemAddress.Add(0x134), canonicalAddress.RawValue);

            if (i < stagingMenu.Items.Count && stagingMenu.Items[i].Item is { } item)
                item.SetRuntimeParentAddress(canonicalAddress);
        }
    }

    // Copy the completed 0x08-byte Localize header from TEMP into the
    // canonical type-0x1A pool.
    public LocalizeAsset DB_AddXAsset(
        LocalizeAsset localize,
        XBlockAddress pointerCellAddress) =>
        RegisterCanonicalAsset(
            XAssetType.Localize,
            "Localize",
            localize.Name,
            localize,
            LocalizeAsset.SerializedSize,
            pointerCellAddress,
            "Localize has no staging block address for DB_AddXAsset canonicalization.");

    // Canonicalize the completed 0x18-byte TEMP LeaderboardDef root as XAsset
    // type 0x25.
    public LeaderboardDefAsset DB_AddXAsset(
        LeaderboardDefAsset leaderboard,
        XBlockAddress pointerCellAddress) =>
        RegisterCanonicalAsset(
            XAssetType.LeaderboardDef,
            "LeaderboardDef",
            leaderboard.Name,
            leaderboard,
            LeaderboardDefAsset.SerializedSize,
            pointerCellAddress,
            "LeaderboardDef has no staging block address for DB_AddXAsset canonicalization.");

    // Canonicalize the completed 0x2C-byte TEMP PhysPreset root as XAsset
    // type 0x00.
    public PhysPresetAsset DB_AddXAsset(
        PhysPresetAsset physPreset,
        XBlockAddress pointerCellAddress) =>
        RegisterCanonicalAsset(
            XAssetType.PhysPreset,
            "PhysPreset",
            physPreset.Name,
            physPreset,
            PhysPresetAsset.SerializedSize,
            pointerCellAddress,
            "PhysPreset has no staging block address for DB_AddXAsset canonicalization.");

    // Copy the completed 0x10-byte RawFile header from TEMP into the canonical
    // type-0x23 pool.
    public RawFileAsset DB_AddXAsset(
        RawFileAsset rawFile,
        XBlockAddress pointerCellAddress) =>
        RegisterCanonicalAsset(
            XAssetType.RawFile,
            "RawFile",
            rawFile.Name,
            rawFile,
            RawFileAsset.SerializedSize,
            pointerCellAddress,
            "RawFile has no staging block address for DB_AddXAsset canonicalization.");

    // Canonicalize the completed 0x88-byte TEMP SndCurve root as XAsset
    // type 0x0B.
    public SndCurve DB_AddXAsset(
        SndCurve sndCurve,
        XBlockAddress pointerCellAddress) =>
        RegisterCanonicalAsset(
            XAssetType.SndCurve,
            "SndCurve",
            sndCurve.Filename,
            sndCurve,
            SndCurve.SerializedSize,
            pointerCellAddress,
            "SndCurve has no staging block address for DB_AddXAsset canonicalization.");

    // Copy the completed 0x10-byte StringTable header from TEMP into the
    // canonical type-0x24 pool.
    public StringTableAsset DB_AddXAsset(
        StringTableAsset stringTable,
        XBlockAddress pointerCellAddress) =>
        RegisterCanonicalAsset(
            XAssetType.StringTable,
            "StringTable",
            stringTable.Name,
            stringTable,
            StringTableAsset.SerializedSize,
            pointerCellAddress,
            "StringTable has no staging block address for DB_AddXAsset canonicalization.");

    // StructuredDataDef uses a 0x0C-byte pool copy with no type-specific
    // post-copy callback.
    public StructuredDataDefSetAsset DB_AddXAsset(
        StructuredDataDefSetAsset defSet,
        XBlockAddress pointerCellAddress) =>
        RegisterCanonicalAsset(
            XAssetType.StructuredDataDef,
            "StructuredDataDef",
            defSet.Name,
            defSet,
            StructuredDataDefSetAsset.SerializedSize,
            pointerCellAddress,
            "StructuredDataDefSet has no staging block address for DB_AddXAsset canonicalization.");

    // Copy the completed 0x9C-byte Techset header from TEMP into the canonical
    // type-0x08 pool.
    public MaterialTechniqueSetAsset DB_AddXAsset(
        MaterialTechniqueSetAsset techniqueSet,
        XBlockAddress pointerCellAddress) =>
        RegisterCanonicalAsset(
            XAssetType.Techset,
            "Techset",
            techniqueSet.Name,
            techniqueSet,
            MaterialTechniqueSetAsset.SerializedSize,
            pointerCellAddress,
            "Techset has no staging block address for DB_AddXAsset canonicalization.");

    // Material roots are TEMP staging objects. Type 5 canonicalization strips
    // a leading ',' for the global asset-name lookup and patches the caller's
    // destination cell.
    public MaterialAsset DB_AddXAsset(
        MaterialAsset material,
        XBlockAddress pointerCellAddress)
    {
        XBlockAddress stagingAddress = material.StagingAddress
            ?? throw new InvalidDataException("Material has no staging block address for DB_AddXAsset canonicalization.");
        byte[] headerBytes = Blocks.ReadBytes(stagingAddress, MaterialAsset.SerializedSize);
        XAssetPoolEntry entry = RegisterAsset(
            XAssetType.Material,
            XAssetType.Material,
            material.Info.Name,
            material,
            stagingAddress,
            headerBytes,
            pointerCellAddress,
            out bool added,
            Blocks);
        var canonical = (MaterialAsset)entry.Asset;

        Blocks.WriteInt32(pointerCellAddress, entry.Address.RawValue);
        if (added &&
            !entry.IsReferencePlaceholder &&
            ReferenceEquals(entry.Asset, material))
        {
            _materialTechniqueStateCache.ApplyNewProvider(
                material,
                _assetLoadSession.AssetPool,
                entry);
        }
        _materialsByAddress[pointerCellAddress] = canonical;
        return canonical;
    }

    // Canonicalize the completed 0x70-byte TEMP TracerDef root as XAsset
    // type 0x27.
    public TracerDefAsset DB_AddXAsset(
        TracerDefAsset tracer,
        XBlockAddress pointerCellAddress)
    {
        XBlockAddress stagingAddress = tracer.StagingAddress
            ?? throw new InvalidDataException("TracerDef has no staging block address for DB_AddXAsset canonicalization.");
        byte[] headerBytes = Blocks.ReadBytes(stagingAddress, TracerDefAsset.SerializedSize);
        XAssetPoolEntry entry = RegisterAsset(
            XAssetType.Tracer,
            XAssetType.Tracer,
            tracer.Name,
            tracer,
            stagingAddress,
            headerBytes,
            pointerCellAddress,
            out bool added,
            Blocks);
        var canonical = (TracerDefAsset)entry.Asset;

        Blocks.WriteInt32(pointerCellAddress, entry.Address.RawValue);
        return canonical;
    }

    // Canonicalize the completed 0x120-byte TEMP XModel root as type 4, then
    // apply its LOD fixups before TEMP is popped. Provider retirement belongs
    // to zone unload and is not executed during registration.
    public XModelAsset DB_AddXAsset(
        XModelAsset model,
        XBlockAddress pointerCellAddress)
    {
        XBlockAddress stagingAddress = model.StagingAddress
            ?? throw new InvalidDataException("XModel has no staging block address for DB_AddXAsset canonicalization.");
        byte[] headerBytes = Blocks.ReadBytes(stagingAddress, XModelAsset.SerializedSize);
        XAssetPoolEntry entry = RegisterAsset(
            XAssetType.XModel,
            XAssetType.XModel,
            model.Name,
            model,
            stagingAddress,
            headerBytes,
            pointerCellAddress,
            out bool added,
            Blocks);
        var canonical = (XModelAsset)entry.Asset;

        Blocks.WriteInt32(pointerCellAddress, entry.Address.RawValue);
        ApplyCanonicalXModelLodFixup(canonical, entry);
        return canonical;
    }

    // For each loaded LOD, the canonical XModelSurfs header supplies numSurfs,
    // partBits, and the runtime surface pointer. SurfIndex becomes the
    // cumulative surface count, and the model stores the final total. This is
    // post-registration behavior, not part of the serialized XModel loader.
    private void ApplyCanonicalXModelLodFixup(
        XModelAsset model,
        XAssetPoolEntry modelEntry)
    {
        int numLods = model.NumLods;
        if (numLods > model.Lods.Count || numLods > 4)
        {
            throw new InvalidDataException(
                $"Canonical XModel '{model.Name}' declares {numLods} LODs but exposes {model.Lods.Count}; maximum is 4.");
        }

        ushort cumulativeSurfaceCount = 0;
        for (int lodIndex = 0; lodIndex < numLods; lodIndex++)
        {
            XModelLodInfo lod = model.Lods[lodIndex];
            XModelSurfsAsset modelSurfs = lod.ModelSurfs
                ?? throw new InvalidDataException(
                    $"Canonical XModel '{model.Name}' LOD {lodIndex} has no canonical XModelSurfs object.");
            if (!_assetLoadSession.AssetPool.TryGetEntry(
                    modelSurfs,
                    out XAssetPoolEntry modelSurfsEntry) ||
                modelSurfsEntry.AssetType != XAssetType.XModelSurfs ||
                modelSurfsEntry.HeaderBytes.Length != XModelSurfsAsset.SerializedSize)
            {
                throw new InvalidDataException(
                    $"Canonical XModel '{model.Name}' LOD {lodIndex} does not reference a complete type-3 XModelSurfs header.");
            }

            ReadOnlySpan<byte> modelSurfsHeader = modelSurfsEntry.HeaderBytes;
            int surfsRaw = BinaryPrimitives.ReadInt32BigEndian(
                modelSurfsHeader.Slice(0x04, sizeof(int)));
            ushort numSurfs = BinaryPrimitives.ReadUInt16BigEndian(
                modelSurfsHeader.Slice(0x08, sizeof(ushort)));
            var partBits = new uint[6];
            for (int partBitIndex = 0; partBitIndex < partBits.Length; partBitIndex++)
            {
                partBits[partBitIndex] = BinaryPrimitives.ReadUInt32BigEndian(
                    modelSurfsHeader.Slice(
                        0x0c + (partBitIndex * sizeof(uint)),
                        sizeof(uint)));
            }

            ushort surfIndex = cumulativeSurfaceCount;
            cumulativeSurfaceCount = checked((ushort)(cumulativeSurfaceCount + numSurfs));
            lod.ApplyCanonicalSurfaceFixup(
                numSurfs,
                surfIndex,
                partBits,
                new XPointer<byte[]>(
                    surfsRaw,
                    XPointerResolutionMode.Direct,
                    lod.SurfsRuntimePointer.CellAddress));

            int lodOffset = 0x40 + (lodIndex * XModelLodInfo.SerializedSize);
            PatchCanonicalXModelLodHeader(
                modelEntry.HeaderBytes,
                lodOffset,
                numSurfs,
                surfIndex,
                partBits,
                surfsRaw);
            if (!ReferenceEquals(modelEntry.NativePoolCopyBytes, modelEntry.HeaderBytes))
            {
                PatchCanonicalXModelLodHeader(
                    modelEntry.NativePoolCopyBytes,
                    lodOffset,
                    numSurfs,
                    surfIndex,
                    partBits,
                    surfsRaw);
            }
        }

        if (cumulativeSurfaceCount > byte.MaxValue)
        {
            throw new InvalidDataException(
                $"Canonical XModel '{model.Name}' has {cumulativeSurfaceCount} LOD surfaces; XModel +0x06 is one byte.");
        }

        byte totalSurfaceCount = (byte)cumulativeSurfaceCount;
        model.ApplyCanonicalSurfaceCount(totalSurfaceCount);
        modelEntry.HeaderBytes[0x06] = totalSurfaceCount;
        modelEntry.NativePoolCopyBytes[0x06] = totalSurfaceCount;
    }

    private static void PatchCanonicalXModelLodHeader(
        byte[] header,
        int lodOffset,
        ushort numSurfs,
        ushort surfIndex,
        IReadOnlyList<uint> partBits,
        int surfsRaw)
    {
        BinaryPrimitives.WriteUInt16BigEndian(
            header.AsSpan(lodOffset + 0x04, sizeof(ushort)),
            numSurfs);
        BinaryPrimitives.WriteUInt16BigEndian(
            header.AsSpan(lodOffset + 0x06, sizeof(ushort)),
            surfIndex);
        for (int partBitIndex = 0; partBitIndex < partBits.Count; partBitIndex++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(
                header.AsSpan(
                    lodOffset + 0x0c + (partBitIndex * sizeof(uint)),
                    sizeof(uint)),
                partBits[partBitIndex]);
        }

        BinaryPrimitives.WriteInt32BigEndian(
            header.AsSpan(lodOffset + 0x24, sizeof(int)),
            surfsRaw);
    }

    public LocalizeAsset? ResolveLocalize(XPointerReference pointer) =>
        ResolveSimpleCanonicalAsset<LocalizeAsset>(pointer, XAssetType.Localize);

    public LeaderboardDefAsset? ResolveLeaderboardDef(XPointerReference pointer) =>
        ResolveSimpleCanonicalAsset<LeaderboardDefAsset>(
            pointer,
            XAssetType.LeaderboardDef);

    public MenuFileAsset? ResolveMenuFile(XPointerReference pointer) =>
        ResolveSimpleCanonicalAsset<MenuFileAsset>(pointer, XAssetType.MenuFile);

    public MenuDefAsset? ResolveMenuDef(XPointerReference pointer) =>
        ResolveSimpleCanonicalAsset<MenuDefAsset>(pointer, XAssetType.Menu);

    public PhysPresetAsset? ResolvePhysPreset(XPointerReference pointer) =>
        ResolveSimpleCanonicalAsset<PhysPresetAsset>(pointer, XAssetType.PhysPreset);

    public RawFileAsset? ResolveRawFile(XPointerReference pointer) =>
        ResolveSimpleCanonicalAsset<RawFileAsset>(pointer, XAssetType.RawFile);

    public SndCurve? ResolveSndCurve(XPointerReference pointer) =>
        ResolveSimpleCanonicalAsset<SndCurve>(pointer, XAssetType.SndCurve);

    public StringTableAsset? ResolveStringTable(XPointerReference pointer) =>
        ResolveSimpleCanonicalAsset<StringTableAsset>(pointer, XAssetType.StringTable);

    public StructuredDataDefSetAsset? ResolveStructuredDataDefSet(
        XPointerReference pointer) =>
        ResolveSimpleCanonicalAsset<StructuredDataDefSetAsset>(
            pointer,
            XAssetType.StructuredDataDef);

    public MaterialTechniqueSetAsset? ResolveTechniqueSet(
        XPointerReference pointer) =>
        ResolveSimpleCanonicalAsset<MaterialTechniqueSetAsset>(
            pointer,
            XAssetType.Techset);

    public MaterialAsset? ResolveMaterial(XPointerReference pointer)
    {
        if (_assetLoadSession.AssetPool.TryResolve(
                pointer.Raw,
                XAssetType.Material,
                out MaterialAsset? directCanonical))
        {
            return directCanonical;
        }

        if (pointer.PackedAddress is { } packedAddress &&
            _materialsByAddress.TryGetValue(packedAddress, out MaterialAsset? material))
        {
            return material;
        }

        if (pointer.Type == PointerType.Offset &&
            pointer.ResolutionMode == XPointerResolutionMode.AliasCell)
        {
            if (pointer.PackedAddress is not { } aliasCell)
                return null;

            int raw = Blocks.ReadInt32(aliasCell);
            if (_assetLoadSession.AssetPool.TryResolve(
                    raw,
                    XAssetType.Material,
                    out MaterialAsset? canonical))
            {
                return canonical;
            }

            if (XPointerCodec.TryDecodeBlockAddress(raw, out XBlockAddress targetAddress) &&
                _materialsByAddress.TryGetValue(targetAddress, out material))
            {
                return material;
            }
        }

        return null;
    }

    public TracerDefAsset? ResolveTracerDef(XPointerReference pointer) =>
        ResolveSimpleCanonicalAsset<TracerDefAsset>(pointer, XAssetType.Tracer);

    public WeaponAsset? ResolveWeapon(XPointerReference pointer) =>
        ResolveSimpleCanonicalAsset<WeaponAsset>(pointer, XAssetType.Weapon);

    /// <summary>
    /// Asset-specific extension points used while GfxImage orchestration and
    /// registration state still live in the legacy compatibility context.
    /// The target reader depends only on this loader-owned seam; the legacy
    /// context supplies that behavior without exposing its DB header,
    /// stream table, or renderer hook.
    /// </summary>
    public virtual int? AllocateGfxImageStreamIndex(bool hasStreamingData) =>
        throw MissingGfxImageExecutionCapability();

    public virtual IReadOnlyList<DbHeaderImageStreamEntry> GetGfxImageStreamEntries(
        int? imageIndex) =>
        throw MissingGfxImageExecutionCapability();

    /// <summary>
    /// Number of language-specific SoundFile records serialized behind each
    /// non-null snd_alias_t sound-file pointer. Contexts without a DB header
    /// retain the native single-language default.
    /// </summary>
    public virtual int SoundFileCount => 1;

    public virtual GfxImageAsset DB_AddXAsset(
        GfxImageAsset image,
        XBlockAddress pointerCellAddress) =>
        throw MissingGfxImageExecutionCapability();

    public virtual GfxImageAsset? ResolveGfxImage(XPointerReference pointer) =>
        throw MissingGfxImageExecutionCapability();

    private TAsset RegisterCanonicalAsset<TAsset>(
        XAssetType assetType,
        string diagnosticType,
        string? name,
        TAsset asset,
        int serializedSize,
        XBlockAddress pointerCellAddress,
        string missingStagingAddressMessage)
        where TAsset : BaseAsset
    {
        XBlockAddress stagingAddress = asset.StagingAddress
            ?? throw new InvalidDataException(missingStagingAddressMessage);
        byte[] headerBytes = Blocks.ReadBytes(stagingAddress, serializedSize);
        XAssetPoolEntry entry = RegisterAsset(
            assetType,
            assetType,
            name,
            asset,
            stagingAddress,
            headerBytes,
            pointerCellAddress,
            out bool added,
            Blocks);
        var canonical = (TAsset)entry.Asset;

        Blocks.WriteInt32(pointerCellAddress, entry.Address.RawValue);
        return canonical;
    }

    private TAsset? ResolveSimpleCanonicalAsset<TAsset>(
        XPointerReference pointer,
        XAssetType assetType)
        where TAsset : BaseAsset
    {
        if (_assetLoadSession.AssetPool.TryResolve(
                pointer.Raw,
                assetType,
                out TAsset? directCanonical))
        {
            return directCanonical;
        }

        if (pointer.Type != PointerType.Offset ||
            pointer.ResolutionMode != XPointerResolutionMode.AliasCell ||
            pointer.PackedAddress is not { } aliasCell)
        {
            return null;
        }

        int raw = Blocks.ReadInt32(aliasCell);
        return _assetLoadSession.AssetPool.TryResolve(raw, assetType, out TAsset? canonical)
            ? canonical
            : null;
    }

    private static NotSupportedException MissingGfxImageExecutionCapability() =>
        new(
            "This load execution context does not provide the transitional " +
            "GfxImage registration and DB-header binding capability.");
}

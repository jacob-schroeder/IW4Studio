using IW4.Assets.Assets;
using IW4.FastFiles.Strings;
using IW4.FastFiles.Zone;
using IW4.Runtime.Assets;
using IW4.Runtime.Strings;

namespace IW4.Runtime.Database;

/// <summary>
/// Runtime-owned state for one in-progress XZone asset load. It coordinates
/// process-wide canonical assets and script strings with the zone's immutable
/// contribution ledger. This is a managed implementation aid, not an original
/// IW4 structure.
/// </summary>
public sealed class XAssetLoadSession
{
    private readonly IXAssetProviderRegistrationSink _registrationSink;
    private DbZoneContributionBuilder? _contributions;

    public XAssetLoadSession(
        XAssetPool assetPool,
        ScriptStringTable scriptStrings,
        IXAssetProviderRegistrationSink registrationSink)
    {
        AssetPool = assetPool ?? throw new ArgumentNullException(nameof(assetPool));
        ScriptStrings = scriptStrings ?? throw new ArgumentNullException(nameof(scriptStrings));
        _registrationSink = registrationSink
            ?? throw new ArgumentNullException(nameof(registrationSink));
    }

    public XAssetPool AssetPool { get; }

    public ScriptStringTable ScriptStrings { get; }

    public ZoneScriptStringTable ZoneScriptStrings { get; } = new();

    public DbZoneHandle ZoneOwner => _contributions?.Zone ?? default;

    public ScriptStringTableEntry InternZoneString(
        string text,
        ScriptStringUser user = ScriptStringUser.XZone)
    {
        ScriptStringTableEntry entry;
        if (_contributions is { } contributions)
        {
            entry = ScriptStrings.Intern(text, user, contributions.Zone);
            contributions.Add(entry.Handle);
        }
        else
        {
            // Direct phase-level loader calls without a DbRuntime owner remain
            // persistent and cannot be selected by XZone free flags.
            entry = ScriptStrings.Intern(text, user);
        }

        return entry;
    }

    /// <summary>
    /// Registers one canonical provider: intern its name, publish it to the
    /// pool, record zone ownership, then initialize managed runtime state.
    /// </summary>
    public XAssetPoolEntry RegisterAsset(
        XAssetType assetType,
        string name,
        BaseAsset asset,
        XBlockAddress stagingAddress,
        ReadOnlySpan<byte> headerBytes,
        out bool added,
        IXAssetSourceMemory? sourceBlocks = null,
        ReadOnlySpan<byte> nativePoolCopyBytes = default,
        int? nativePoolCopyCapturedLength = null) =>
        RegisterAsset(
            assetType,
            name,
            asset,
            stagingAddress,
            headerBytes,
            out added,
            out _,
            sourceBlocks,
            nativePoolCopyBytes,
            nativePoolCopyCapturedLength);

    /// <summary>
    /// Registers one provider and returns its exact provider identity even
    /// when an earlier canonical slot remains active.
    /// </summary>
    public XAssetPoolEntry RegisterAsset(
        XAssetType assetType,
        string name,
        BaseAsset asset,
        XBlockAddress stagingAddress,
        ReadOnlySpan<byte> headerBytes,
        out bool added,
        out XAssetProviderId providerId,
        IXAssetSourceMemory? sourceBlocks = null,
        ReadOnlySpan<byte> nativePoolCopyBytes = default,
        int? nativePoolCopyCapturedLength = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(asset);

        if (!string.IsNullOrWhiteSpace(name))
        {
            string canonicalName = XAssetPool.CanonicalizeName(name);
            InternZoneString(canonicalName);
        }

        if (_contributions is not { } contributions)
        {
            return AssetPool.DB_AddXAsset(
                assetType,
                name,
                asset,
                stagingAddress,
                headerBytes,
                owner: default,
                out added,
                out providerId,
                sourceBlocks,
                nativePoolCopyBytes,
                nativePoolCopyCapturedLength);
        }

        XAssetPoolEntry entry = AssetPool.DB_AddXAsset(
            assetType,
            name,
            asset,
            stagingAddress,
            headerBytes,
            contributions.Zone,
            out added,
            out providerId,
            sourceBlocks,
            nativePoolCopyBytes,
            nativePoolCopyCapturedLength);
        XAssetProviderContribution provider = AssetPool.GetProvider(
            entry.Address,
            providerId);
        contributions.Add(provider);
        _registrationSink.RegisterProvider(AssetPool, entry.Address, providerId);
        return entry;
    }

    internal void BindZoneOwner(DbZoneHandle zone)
    {
        if (zone.IsNone)
            throw new ArgumentOutOfRangeException(nameof(zone));
        if (_contributions is not null)
            throw new InvalidOperationException("The XAsset load session already has an XZone contribution owner.");

        _contributions = new DbZoneContributionBuilder(zone);
    }

    internal DbZoneContributions FreezeZoneContributions()
    {
        DbZoneContributionBuilder contributions = _contributions
            ?? throw new InvalidOperationException("The XAsset load session has no XZone contribution owner.");
        return contributions.Freeze();
    }
}

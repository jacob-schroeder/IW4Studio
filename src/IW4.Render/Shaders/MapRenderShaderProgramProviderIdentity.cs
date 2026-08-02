using IW4.Assets.Zone;
using IW4.FastFiles.Zone;
using IW4.Runtime.Assets;
using IW4.Runtime.Database;

namespace IW4.Render.Shaders;

/// <summary>Immutable provider provenance captured with one selected program.</summary>
public sealed record MapRenderShaderProgramProviderIdentity(
    XAssetPoolAddress SlotAddress,
    XAssetProviderId ProviderId,
    DbZoneHandle Owner,
    long RegistrationSequence,
    XBlockAddress StagingAddress,
    XRuntimeAddress? RuntimeAddress,
    bool IsReferencePlaceholder,
    bool IsActiveCanonicalProvider);

using System.Diagnostics.CodeAnalysis;
using IW4.Assets.Assets.TechniqueSet;
using IW4.Render.Materials;
using IW4.Render.Shaders;

namespace IW4.Render.Execution;

/// <summary>
/// Supplies only the canonical material graph and shader state required to
/// construct renderer-neutral material execution contracts.
/// </summary>
public interface IMaterialExecutionLookup :
    IMapRenderStateLoadBitsResolver,
    IMapRenderSelectedPassProgramProvider
{
    bool TryResolveCanonicalMaterialTechniqueBinding(
        string name,
        long expectedPoolRevision,
        [NotNullWhen(true)] out MapRenderMaterialTechniqueBinding? binding);

    bool HasCanonicalAssetPoolRevision(long expectedPoolRevision);

    IReadOnlyList<MaterialTechniqueSlot> ResolveTechniqueSlots(
        MaterialTechniqueSetAsset techniqueSet);
}

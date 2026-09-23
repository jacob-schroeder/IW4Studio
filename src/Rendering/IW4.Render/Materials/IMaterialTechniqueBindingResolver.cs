using System.Diagnostics.CodeAnalysis;

namespace IW4.Render.Materials;

/// <summary>
/// Resolves the material-owned technique state addressed by one engine draw
/// group. The contract intentionally exposes no broader asset-lookup surface.
/// </summary>
public interface IMaterialTechniqueBindingResolver
{
    bool TryResolveMaterialTechniqueBinding(
        int materialSortedIndex,
        [NotNullWhen(true)] out MaterialTechniqueBinding? binding);
}

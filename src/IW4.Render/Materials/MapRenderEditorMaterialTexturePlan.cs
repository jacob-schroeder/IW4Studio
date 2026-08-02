namespace IW4.Render.Materials;

/// <summary>
/// Deterministically ordered EditorPreview texture-role plan. All source rows
/// survive planning, including duplicates and unclassified rows.
/// </summary>
public sealed class MapRenderEditorMaterialTexturePlan
{
    private readonly MapRenderEditorMaterialTextureBinding[] _bindings;

    public MapRenderEditorMaterialTexturePlan(
        IReadOnlyList<MapRenderEditorMaterialTextureBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        _bindings = bindings.ToArray();
        Bindings = Array.AsReadOnly(_bindings);
    }

    public IReadOnlyList<MapRenderEditorMaterialTextureBinding> Bindings { get; }

    public bool TryGetUniqueBinding(
        MapRenderEditorMaterialTextureRole role,
        out MapRenderEditorMaterialTextureBinding? binding)
    {
        binding = null;
        foreach (MapRenderEditorMaterialTextureBinding candidate in _bindings)
        {
            if (candidate.Role != role)
                continue;
            if (binding is not null)
            {
                binding = null;
                return false;
            }

            binding = candidate;
        }

        return binding is not null;
    }
}

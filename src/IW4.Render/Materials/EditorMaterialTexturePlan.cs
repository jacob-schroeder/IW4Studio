namespace IW4.Render.Materials;

/// <summary>
/// Deterministically ordered EditorPreview texture-role plan. All source rows
/// survive planning, including duplicates and unclassified rows.
/// </summary>
public sealed class EditorMaterialTexturePlan
{
    private readonly EditorMaterialTextureBinding[] _bindings;

    public EditorMaterialTexturePlan(
        IReadOnlyList<EditorMaterialTextureBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        _bindings = bindings.ToArray();
        Bindings = Array.AsReadOnly(_bindings);
    }

    public IReadOnlyList<EditorMaterialTextureBinding> Bindings { get; }

    public bool TryGetUniqueBinding(
        EditorMaterialTextureRole role,
        out EditorMaterialTextureBinding? binding)
    {
        binding = null;
        foreach (EditorMaterialTextureBinding candidate in _bindings)
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

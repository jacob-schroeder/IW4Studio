using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;

namespace IW4.Runtime.Assets;

internal sealed class MaterialEntryComparer : IComparer<XAssetPoolEntry>
{
    private readonly MaterialGraphResolver _resolver;
    private readonly Dictionary<XAssetPoolEntry, MaterialSortInputs> _inputs =
        new(ReferenceEqualityComparer.Instance);

    public MaterialEntryComparer(MaterialGraphResolver resolver)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    public int Compare(XAssetPoolEntry? x, XAssetPoolEntry? y)
    {
        if (ReferenceEquals(x, y))
            return 0;
        if (x is null)
            return -1;
        if (y is null)
            return 1;

        MaterialSortInputs first = GetInputs(x);
        MaterialSortInputs second = GetInputs(y);

        int comparison = first.Material.Info.SortKey.CompareTo(second.Material.Info.SortKey);
        if (comparison != 0)
            return comparison;

        // Equal sort keys next group by the applicable technique family and
        // the lightmap game flag.
        if (first.LitTechnique is null)
        {
            comparison = IsPresent(second.EmissiveTechnique)
                .CompareTo(IsPresent(first.EmissiveTechnique));
        }
        else
        {
            comparison = IsSet(
                    second.Material.Info.GameFlags,
                    MaterialGameFlags.HasLightmap)
                .CompareTo(IsSet(
                    first.Material.Info.GameFlags,
                    MaterialGameFlags.HasLightmap));
        }
        if (comparison != 0)
            return comparison;

        comparison = first.StandardPrepassSortKey
            .CompareTo(second.StandardPrepassSortKey);
        if (comparison != 0)
            return comparison;

        comparison = second.WritesDepth.CompareTo(first.WritesDepth);
        if (comparison != 0)
            return comparison;

        if (first.ComparisonTechnique is not null &&
            second.ComparisonTechnique is not null)
        {
            comparison = StringComparer.Ordinal.Compare(
                first.PixelShaderName,
                second.PixelShaderName);
            if (comparison != 0)
                return comparison;

            if (first.LitTechnique is null || first.WritesDepth)
            {
                comparison = CompareStableMaterialArguments(first, second);
                if (comparison != 0)
                    return comparison;
            }

            comparison = StringComparer.Ordinal.Compare(
                first.VertexShaderName,
                second.VertexShaderName);
            if (comparison != 0)
                return comparison;
        }

        comparison = StringComparer.Ordinal.Compare(
            first.TechniqueSetName,
            second.TechniqueSetName);
        if (comparison != 0)
            return comparison;

        return StringComparer.Ordinal.Compare(
            first.Material.Info.Name ?? string.Empty,
            second.Material.Info.Name ?? string.Empty);
    }

    private static int IsPresent(object? value) => value is null ? 0 : 1;
    private static bool IsSet(MaterialGameFlags value, MaterialGameFlags mask) =>
        (value & mask) != 0;

    private static bool IsSet(MaterialStateFlags value, MaterialStateFlags mask) =>
        (value & mask) != 0;

    // Compare only the stable argument tail. Code constants compare by their
    // 16-bit source index. Material/literal pixel constants are resolved,
    // sorted by destination, and compared destination-first and then by float
    // component.
    private static int CompareStableMaterialArguments(
        MaterialSortInputs first,
        MaterialSortInputs second)
    {
        int comparison = first.CodePixelConstantIndices.Length
            .CompareTo(second.CodePixelConstantIndices.Length);
        if (comparison != 0)
            return comparison;
        for (int index = 0; index < first.CodePixelConstantIndices.Length; index++)
        {
            comparison = first.CodePixelConstantIndices[index]
                .CompareTo(second.CodePixelConstantIndices[index]);
            if (comparison != 0)
                return comparison;
        }

        comparison = first.PixelConstants.Length
            .CompareTo(second.PixelConstants.Length);
        if (comparison != 0)
            return comparison;

        for (int index = 0; index < first.PixelConstants.Length; index++)
        {
            ResolvedConstant firstConstant = first.PixelConstants[index];
            ResolvedConstant secondConstant = second.PixelConstants[index];
            comparison = firstConstant.Destination.CompareTo(secondConstant.Destination);
            if (comparison != 0)
                return comparison;
            comparison = firstConstant.Value.X.CompareTo(secondConstant.Value.X);
            if (comparison != 0)
                return comparison;
            comparison = firstConstant.Value.Y.CompareTo(secondConstant.Value.Y);
            if (comparison != 0)
                return comparison;
            comparison = firstConstant.Value.Z.CompareTo(secondConstant.Value.Z);
            if (comparison != 0)
                return comparison;
            comparison = firstConstant.Value.W.CompareTo(secondConstant.Value.W);
            if (comparison != 0)
                return comparison;
        }

        return 0;
    }

    private MaterialSortInputs GetInputs(XAssetPoolEntry entry)
    {
        if (_inputs.TryGetValue(entry, out MaterialSortInputs? inputs))
            return inputs;

        var material = (MaterialAsset)entry.Asset;
        MaterialTechniqueAsset? lit = _resolver.GetTechnique(
            material,
            MaterialPostLoadProcessor.LitTechniqueSlot);
        MaterialTechniqueAsset? emissive = _resolver.GetTechnique(
            material,
            MaterialPostLoadProcessor.EmissiveTechniqueSlot);
        MaterialTechniqueAsset? comparisonTechnique = lit ?? emissive;
        MaterialPassAsset? pass = comparisonTechnique?.Passes.FirstOrDefault();
        MaterialShaderArgumentAsset[] stableArguments =
            MaterialStableArgumentResolver.GetStableArguments(pass);
        inputs = new MaterialSortInputs(
            material,
            lit,
            emissive,
            comparisonTechnique,
            MaterialPostLoadProcessor.GetStandardPrepassSortKey(
                material,
                _resolver),
            IsSet(material.StateFlags, MaterialStateFlags.WritesDepth),
            pass is null
                ? string.Empty
                : MaterialPostLoadProcessor.GetShader(
                    pass,
                    MaterialShaderKind.Pixel,
                    _resolver)?.Name ?? string.Empty,
            pass is null
                ? string.Empty
                : MaterialPostLoadProcessor.GetShader(
                    pass,
                    MaterialShaderKind.Vertex,
                    _resolver)?.Name ?? string.Empty,
            _resolver.GetTechniqueSet(material)?.Name ?? string.Empty,
            MaterialStableArgumentResolver.GetCodePixelConstantIndices(
                stableArguments),
            MaterialStableArgumentResolver.ResolvePixelConstants(
                material,
                stableArguments));
        _inputs.Add(entry, inputs);
        return inputs;
    }

    private sealed record MaterialSortInputs(
        MaterialAsset Material,
        MaterialTechniqueAsset? LitTechnique,
        MaterialTechniqueAsset? EmissiveTechnique,
        MaterialTechniqueAsset? ComparisonTechnique,
        MaterialPrepassType StandardPrepassSortKey,
        bool WritesDepth,
        string PixelShaderName,
        string VertexShaderName,
        string TechniqueSetName,
        ushort[] CodePixelConstantIndices,
        ResolvedConstant[] PixelConstants);
}

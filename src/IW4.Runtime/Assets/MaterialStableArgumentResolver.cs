using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;

namespace IW4.Runtime.Assets;

internal static class MaterialStableArgumentResolver
{
    // One state record can hold 16 resolved pixel constants.
    private const int MaxResolvedPixelConstants = 16;

    public static MaterialShaderArgumentAsset[] GetStableArguments(
        MaterialPassAsset? pass,
        bool requireComplete = false)
    {
        if (pass is null || pass.StableArgCount == 0)
            return [];

        int start = pass.PerPrimArgCount + pass.PerObjArgCount;
        int requiredCount = checked(start + pass.StableArgCount);
        if (requireComplete && pass.Args.Count < requiredCount)
        {
            throw new InvalidDataException(
                $"Material pass requires {requiredCount} shader arguments, but only {pass.Args.Count} are materialized.");
        }

        return pass.Args.Skip(start).Take(pass.StableArgCount).ToArray();
    }

    public static ushort[] GetCodePixelConstantIndices(
        IEnumerable<MaterialShaderArgumentAsset> stableArguments)
    {
        ArgumentNullException.ThrowIfNull(stableArguments);
        return stableArguments
            .Where(argument => argument.Type == MaterialShaderArgumentType.CodePixelConst)
            .Select(argument => unchecked((ushort)((uint)argument.ArgumentRaw >> 16)))
            .ToArray();
    }

    public static ResolvedConstant[] ResolvePixelConstants(
        MaterialAsset material,
        IEnumerable<MaterialShaderArgumentAsset> stableArguments,
        bool requireResolved = false)
    {
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(stableArguments);

        var constants = new List<ResolvedConstant>();
        foreach (MaterialShaderArgumentAsset argument in stableArguments)
        {
            MaterialShaderLiteralConstant? value = argument.Type switch
            {
                MaterialShaderArgumentType.MaterialPixelConst => ResolveMaterialConstant(
                    material,
                    argument.ArgumentRaw),
                MaterialShaderArgumentType.LiteralPixelConst => argument.LiteralConstant,
                _ => null
            };

            bool isPixelConstant = argument.Type is
                MaterialShaderArgumentType.MaterialPixelConst or
                MaterialShaderArgumentType.LiteralPixelConst;
            if (requireResolved && isPixelConstant && !value.HasValue)
            {
                throw new InvalidDataException(
                    $"Material '{material.Info.Name}' cannot resolve stable pixel constant type " +
                    $"{argument.Type} destination {argument.Dest} raw=0x{unchecked((uint)argument.ArgumentRaw):X8}.");
            }

            if (value.HasValue)
                constants.Add(new ResolvedConstant(argument.Dest, value.Value));
        }

        if (requireResolved && constants.Count > MaxResolvedPixelConstants)
        {
            throw new InvalidDataException(
                $"Material '{material.Info.Name}' pass resolves {constants.Count} stable pixel constants; " +
                $"the state record has capacity for {MaxResolvedPixelConstants}.");
        }

        return constants
            .OrderBy(constant => constant.Destination)
            .ToArray();
    }

    private static MaterialShaderLiteralConstant? ResolveMaterialConstant(
        MaterialAsset material,
        int nameHash)
    {
        MaterialConstantDef? constant = material.Constants.FirstOrDefault(
            candidate => unchecked((int)candidate.NameHash) == nameHash);
        return constant is null
            ? null
            : new MaterialShaderLiteralConstant(
                constant.Literal.X,
                constant.Literal.Y,
                constant.Literal.Z,
                constant.Literal.W);
    }
}

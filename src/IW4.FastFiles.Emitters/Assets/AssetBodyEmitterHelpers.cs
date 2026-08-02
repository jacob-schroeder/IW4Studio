using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Emission;

namespace IW4.FastFiles.Emitters.Assets;

internal static class AssetBodyEmitterHelpers
{
    public static List<EmissionError> ValidateIdentity(
        IXAssetBuildData value,
        XAssetType expected,
        int? rowIndex)
    {
        var diagnostics = new List<EmissionError>();
        if (value.AssetType != expected)
        {
            diagnostics.Add(new EmissionError(
                "type",
                $"Emitter '{expected}' cannot emit build data declared as '{value.AssetType}'.",
                rowIndex,
                expected));
        }

        return diagnostics;
    }

    public static void RequireNoDiagnostics(IReadOnlyList<EmissionError> diagnostics)
    {
        if (diagnostics.Count != 0)
            throw new InvalidDataException(string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.ToString())));
    }

    public static bool IsLatin1CString(string value) =>
        value.IndexOf('\0') < 0 && value.All(character => character <= byte.MaxValue);

    public static string XAssetAliasKey(
        XAssetType type,
        string serializedName)
    {
        ArgumentException.ThrowIfNullOrEmpty(serializedName);
        return $"{(int)type}\u0000{serializedName.TrimStart(',').Replace('\\', '/').ToLowerInvariant()}";
    }

    public static PlannedString? PlanString(
        string? value,
        EmissionPlan plan,
        List<EmissionBlockSegment> segments,
        IDictionary<string, EmissionAddress> aliases)
    {
        if (value is null)
            return null;
        if (aliases.TryGetValue(value, out EmissionAddress existing))
            return new PlannedString(existing, true);
        EmissionAddress address = plan.Allocate(checked(value.Length + 1));
        var writer = new XSourceWriter();
        writer.WriteLatin1CString(value);
        segments.Add(new EmissionBlockSegment(address, writer.ToArray()));
        aliases.Add(value, address);
        return new PlannedString(address, false);
    }

    public static int SourcePointer(PlannedString? value) => value switch
    {
        null => 0,
        { IsExistingMaterialization: true } existing => existing.Address.ToPackedPointer(),
        _ => -1
    };
}

public readonly record struct PlannedString(EmissionAddress Address, bool IsExistingMaterialization);

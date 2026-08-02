using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Emission;

namespace IW4.FastFiles.Emitters.Assets;

/// <summary>Emitter for the ComMap root and its
/// inline primary-light payload. No renderer-derived light state is emitted.</summary>
public sealed class ComWorldBodyEmitter : IXAssetBodyEmitter
{
    private const int RootSize = 0x10;
    private const int PrimaryLightSize = 0x44;

    public XAssetType AssetType => XAssetType.ComMap;

    public IReadOnlyList<EmissionError> Validate(IXAssetBuildData buildData, int? rowIndex = null)
    {
        var diagnostics = AssetBodyEmitterHelpers.ValidateIdentity(buildData, AssetType, rowIndex);
        if (buildData is not IComWorldBuildData data)
        {
            diagnostics.Add(Error("body", "ComMap build data does not implement IComWorldBuildData.", rowIndex));
            return diagnostics;
        }

        ValidateString(data.Name, "name", diagnostics, rowIndex);
        if (data.PrimaryLights.Count > 0x10000)
            diagnostics.Add(Error("primaryLights", "ComMap supports at most 65536 primary lights.", rowIndex));
        for (int index = 0; index < data.PrimaryLights.Count; index++)
        {
            ComPrimaryLightBuildData light = data.PrimaryLights[index];
            ValidateVector(light.Color, $"primaryLights[{index}].color", diagnostics, rowIndex);
            ValidateVector(light.Direction, $"primaryLights[{index}].direction", diagnostics, rowIndex);
            ValidateVector(light.Origin, $"primaryLights[{index}].origin", diagnostics, rowIndex);
            ValidateFinite(light.Radius, $"primaryLights[{index}].radius", diagnostics, rowIndex);
            ValidateFinite(light.CosHalfFovOuter, $"primaryLights[{index}].cosHalfFovOuter", diagnostics, rowIndex);
            ValidateFinite(light.CosHalfFovInner, $"primaryLights[{index}].cosHalfFovInner", diagnostics, rowIndex);
            ValidateFinite(light.CosHalfFovExpanded, $"primaryLights[{index}].cosHalfFovExpanded", diagnostics, rowIndex);
            ValidateFinite(light.RotationLimit, $"primaryLights[{index}].rotationLimit", diagnostics, rowIndex);
            ValidateFinite(light.TranslationLimit, $"primaryLights[{index}].translationLimit", diagnostics, rowIndex);
            ValidateString(light.DefName, $"primaryLights[{index}].defName", diagnostics, rowIndex);
        }
        return diagnostics;
    }

    public AssetBodyEmission Plan(IXAssetBuildData buildData, EmissionPlan plan, int? rowIndex = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        AssetBodyEmitterHelpers.RequireNoDiagnostics(Validate(buildData, rowIndex));
        IComWorldBuildData data = (IComWorldBuildData)buildData;
        var segments = new List<EmissionBlockSegment>();

        plan.Push(XFileBlockType.TEMP);
        EmissionAddress root = plan.Allocate(RootSize, 4);
        plan.Push(XFileBlockType.LARGE);
        var nameSegments = new List<EmissionBlockSegment>();
        int beforeName = segments.Count;
        PlannedString? name = AssetBodyEmitterHelpers.PlanString(data.Name, plan, segments, plan.StringAliases);
        nameSegments.AddRange(segments.Skip(beforeName));
        EmissionAddress? lightsAddress = null;
        if (data.PrimaryLights.Count != 0)
            lightsAddress = plan.Allocate(checked(data.PrimaryLights.Count * PrimaryLightSize), 4);
        var defNames = new PlannedString?[data.PrimaryLights.Count];
        var defNameSegments = new List<EmissionBlockSegment>();
        for (int index = 0; index < data.PrimaryLights.Count; index++)
        {
            int beforeDefName = segments.Count;
            defNames[index] = AssetBodyEmitterHelpers.PlanString(data.PrimaryLights[index].DefName, plan, segments, plan.StringAliases);
            defNameSegments.AddRange(segments.Skip(beforeDefName));
        }
        plan.Pop(XFileBlockType.LARGE);
        plan.Pop(XFileBlockType.TEMP);

        if (lightsAddress is { } address)
        {
            var writer = new XSourceWriter();
            for (int index = 0; index < data.PrimaryLights.Count; index++)
                WriteLight(writer, data.PrimaryLights[index], defNames[index]);
            if (writer.Position != checked(data.PrimaryLights.Count * PrimaryLightSize))
                throw new InvalidDataException("ComMap primary-light emission did not produce the expected fixed stride.");
            segments.Add(new EmissionBlockSegment(address, writer.ToArray()));
        }

        var rootWriter = new XSourceWriter();
        rootWriter.WriteInt32(AssetBodyEmitterHelpers.SourcePointer(name));
        rootWriter.WriteInt32(data.IsInUse);
        rootWriter.WriteInt32(data.PrimaryLights.Count);
        rootWriter.WriteInt32(lightsAddress is null ? 0 : -1);
        if (rootWriter.Position != RootSize)
            throw new InvalidDataException("ComMap root emission did not produce 0x10 bytes.");
        EmissionBlockSegment rootSegment = new(root, rootWriter.ToArray());
        segments.Add(rootSegment);

        // The loader reads root, name, light table, then each light's XString.
        // Keep the source order independent of allocation-list order: the
        // light table must precede its child XStrings even though those names
        // were planned while composing the table bytes.
        List<EmissionBlockSegment> source = [rootSegment];
        source.AddRange(nameSegments);
        if (lightsAddress is { } lightAddress)
            source.Add(segments.Single(segment => segment.Address == lightAddress));
        source.AddRange(defNameSegments);
        return new AssetBodyEmission(AssetType, root, segments, source);
    }

    private static void WriteLight(XSourceWriter writer, ComPrimaryLightBuildData light, PlannedString? defName)
    {
        writer.WriteByte(light.Type);
        writer.WriteByte(light.CanUseShadowMap);
        writer.WriteByte(light.Exponent);
        writer.WriteByte(light.Unused);
        WriteVector(writer, light.Color);
        WriteVector(writer, light.Direction);
        WriteVector(writer, light.Origin);
        writer.WriteSingle(light.Radius);
        writer.WriteSingle(light.CosHalfFovOuter);
        writer.WriteSingle(light.CosHalfFovInner);
        writer.WriteSingle(light.CosHalfFovExpanded);
        writer.WriteSingle(light.RotationLimit);
        writer.WriteSingle(light.TranslationLimit);
        writer.WriteInt32(AssetBodyEmitterHelpers.SourcePointer(defName));
    }

    private static void WriteVector(XSourceWriter writer, Float3BuildData value)
    {
        writer.WriteSingle(value.X);
        writer.WriteSingle(value.Y);
        writer.WriteSingle(value.Z);
    }

    private static void ValidateString(string? value, string path, List<EmissionError> diagnostics, int? rowIndex)
    {
        if (value is not null && !AssetBodyEmitterHelpers.IsLatin1CString(value))
            diagnostics.Add(Error(path, "XString must be a Latin-1 C string.", rowIndex));
    }

    private static void ValidateVector(Float3BuildData value, string path, List<EmissionError> diagnostics, int? rowIndex)
    {
        ValidateFinite(value.X, $"{path}.x", diagnostics, rowIndex);
        ValidateFinite(value.Y, $"{path}.y", diagnostics, rowIndex);
        ValidateFinite(value.Z, $"{path}.z", diagnostics, rowIndex);
    }

    private static void ValidateFinite(float value, string path, List<EmissionError> diagnostics, int? rowIndex)
    {
        if (!float.IsFinite(value))
            diagnostics.Add(Error(path, "Value must be finite.", rowIndex));
    }

    private static EmissionError Error(string path, string message, int? rowIndex) =>
        new(path, message, rowIndex, XAssetType.ComMap);
}

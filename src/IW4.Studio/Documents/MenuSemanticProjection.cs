using IW4.FastFiles.Zone;
using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using IW4.Assets.Assets.Menu;

namespace IW4.Studio.Documents;

/// <summary>
/// Canonical comparison projection for Menu graphs.  It preserves object
/// identity/topology and all authored scalar/list/union values, but omits
/// pointer cells, runtime pool objects, destination addresses, and evaluator
/// cache state.  Using this instead of a loaded model's JSON prevents a
/// pointer relocation from being mistaken for an authored change.
/// </summary>
internal static class MenuSemanticProjection
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]>
        PropertiesByType = new();

    public static string Serialize(MenuDefAsset value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Serialize((object)value);
    }

    /// <summary>Same relocation-free projection for another detached graph.
    /// It is intentionally object-based so map readers can compare their
    /// capture build model with a freshly materialized, re-detached model.</summary>
    public static string Serialize(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return JsonSerializer.Serialize(Project(value, new Context()));
    }

    private static object? Project(object? value, Context context)
    {
        if (value is null) return null;
        Type type = value.GetType();
        if (value is byte[] bytes)
            return Convert.ToHexString(bytes);
        if (value is IEnumerable<byte> byteSequence)
            return Convert.ToHexString(byteSequence.ToArray());
        if (value is float number) return $"f:{BitConverter.SingleToInt32Bits(number):X8}";
        if (value is double number64) return $"d:{BitConverter.DoubleToInt64Bits(number64):X16}";
        if (value is string or char or bool || type.IsEnum || type.IsPrimitive || value is decimal)
            return ProjectScalar(value);
        if (value is IEnumerable enumerable)
        {
            var values = new List<object?>();
            foreach (object? item in enumerable) values.Add(Project(item, context));
            return values;
        }
        bool preserveIdentity = PreservesAuthoredIdentity(type);
        if (preserveIdentity && context.Ids.TryGetValue(value, out int existing))
            return new SortedDictionary<string, object?> { ["$ref"] = existing };
        int id = 0;
        if (preserveIdentity)
        {
            id = context.Ids.Count + 1;
            context.Ids.Add(value, id);
        }
        var result = new SortedDictionary<string, object?> { ["$type"] = type.FullName };
        if (preserveIdentity)
            result.Add("$id", id);
        foreach (PropertyInfo property in PropertiesByType.GetOrAdd(
                     type,
                     static value => value
                         .GetProperties(
                             BindingFlags.Instance |
                             BindingFlags.Public)
                         .OrderBy(
                             property => property.Name,
                             StringComparer.Ordinal)
                         .ToArray()))
        {
            if (!property.CanRead || property.GetIndexParameters().Length != 0 || Skip(property)) continue;
            object? propertyValue = property.GetValue(value);
            if (propertyValue is string text && IsWireOnlyReferenceName(property))
                propertyValue = text.TrimStart(',');
            result.Add(property.Name, Project(propertyValue, context));
        }
        return result;
    }

    // These nodes may be recursive, and their sharing can carry Menu graph
    // meaning.  Other model objects are projected by value so source-layout
    // aliasing (for example, reused colors or scalar shells) cannot create a
    // false semantic mismatch after canonical re-emission.
    private static bool PreservesAuthoredIdentity(Type type) =>
        type == typeof(MenuEventHandlerSet) ||
        type == typeof(MenuEventHandler) ||
        type == typeof(Statement) ||
        type == typeof(ExpressionSupportingData) ||
        type == typeof(ItemKeyHandler) ||
        type == typeof(ItemDefAsset) ||
        type == typeof(ConditionalScript) ||
        type == typeof(SetLocalVarData);

    // These managed fields retain an imported wire spelling for the legacy
    // bridge.  A leading comma denotes an external XAsset only on the wire;
    // it is not part of the logical Menu value.
    private static bool IsWireOnlyReferenceName(PropertyInfo property) =>
        property.Name is "BackgroundMaterialName" or "SelectIconMaterialName" or "FocusSoundName";

    private static object ProjectScalar(object value) => value switch
    {
        byte[] bytes => Convert.ToHexString(bytes),
        _ => value
    };

    private static bool Skip(PropertyInfo property)
    {
        Type type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        // Imported linker provenance controls byte-for-byte reconstruction,
        // but it is not authored semantics. In particular, reusable-storage
        // token values are allocation-order-local labels and can differ
        // between two fresh dependency-plan captures of the same graph.
        if (property.Name == "LinkerProvenance" &&
            type.Name.EndsWith("LinkerProvenance", StringComparison.Ordinal))
        {
            return true;
        }
        if (type.Namespace == "IW4.FastFiles.Pointers" || type.FullName?.Contains("XPointer", StringComparison.Ordinal) == true)
            return true;
        // Addresses describe a particular load/materialization, never Menu
        // authored state.  This also covers BaseAsset.StagingAddress, which
        // is populated only after a fresh DB-stream load.
        if (type.Namespace == "IW4.FastFiles.Zone")
            return true;
        // OperandValue.EncodedValue is a serialized pointer cell for string
        // and function operands (and merely duplicates the scalar value for
        // the other operand arms).  The detached StringValue/FunctionStatement
        // fields below carry the authored union semantics.
        if (property.Name == "EncodedValue" &&
            (property.DeclaringType == typeof(OperandValue) || property.DeclaringType == typeof(Operand)))
            return true;
        // The +0x08 operator-tail dword is retained for raw inspection but is
        // not authored Menu state. Kind, OperationCode, and the operand's
        // active union arm remain projected normally.
        if (property.DeclaringType == typeof(ExpressionEntry) &&
            property.Name == nameof(ExpressionEntry.OperatorTail))
            return true;
        if (property.Name is "Offset" or "RuntimeAddress" or "RuntimeParentAddress" or "DestinationAddress" or "BackgroundMaterial" or "SelectIconMaterial" or "FocusSoundAsset" or "Info")
            return true;
        // GfxWorld's loader materializes these into RUNTIME/VIRTUAL blocks or
        // replaces them during its renderer post-load pass.  The world build
        // contract validates their required zero allocations, but they are
        // intentionally not authored semantics.
        if (property.DeclaringType?.Namespace == "IW4.Assets.Assets.GfxMap" &&
            property.Name is "SceneEntCellBits" or "ReflectionProbeTextures" or "LightmapPrimaryTextures" or "LightmapSecondaryTextures" or
                "WorldVbHandle" or "WorldVbOffset" or "LayerVbHandle" or "LayerVbOffset" or
                "CellCasterBits" or "CellCasterBits2" or "SceneDynModels" or "SceneDynBrushes" or
                "PrimaryLightEntityShadowVis" or "PrimaryLightDynEntShadowVis0" or "PrimaryLightDynEntShadowVis1" or "PrimaryLightForModelDynEnt" or
                "SModelVisData" or "SurfaceVisData" or "SurfaceMaterials" or "SurfaceCastsSunShadow" or "AuthoredSurfaceIndexByRuntimeSlot" or
                "DynEntCellBits" or "DynEntVisData" or "UmbraGateData" or "UmbraGateData2")
            return true;
        if (property.DeclaringType?.Namespace == "IW4.Assets.Assets.GfxMap" &&
            property.Name is "SkyImage" or "ReflectionProbeImages" or "Primary" or "Secondary" or "LightmapOverridePrimary" or "LightmapOverrideSecondary" or
                "Material" or "SpriteMaterial" or "FlareMaterial" or "OutdoorImage" or "Model")
            return true;
        // Runtime evaluator/UI caches are deliberately zeroed at capture and
        // do not participate in authored Menu semantics.
        return property.Name is "LastExecuteTime" or "LastResult" or "NextTime" or "RuntimeParentPointer";
    }

    private sealed class Context
    {
        public Dictionary<object, int> Ids { get; } = new(ReferenceEqualityComparer.Instance);
    }
}

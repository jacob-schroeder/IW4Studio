using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using IW4.Assets.Assets.Menu;
using IW4.FastFiles.Zone;

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
        ProjectedPropertiesByType = new();

    public static string Serialize(MenuDefAsset value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Serialize((object)value);
    }

    /// <summary>
    /// Compares the canonical authored relation directly. This is equivalent
    /// to comparing <see cref="Serialize(MenuDefAsset)"/> results, but exits at
    /// the first difference without allocating projected dictionaries, lists,
    /// or JSON.
    /// </summary>
    public static bool SemanticallyEquals(
        MenuDefAsset left,
        MenuDefAsset right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return ReferenceEquals(left, right) ||
            SemanticallyEquals(left, right, new ComparisonContext());
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
            return ProjectByteSequence(byteSequence);
        if (value is float number) return ProjectFloat(number);
        if (value is double number64) return ProjectDouble(number64);
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
        foreach (PropertyInfo property in GetProjectedProperties(type))
        {
            result.Add(
                property.Name,
                Project(GetProjectedPropertyValue(property, value), context));
        }
        return result;
    }

    private static bool SemanticallyEquals(
        object? left,
        object? right,
        ComparisonContext context)
    {
        if (left is null || right is null)
            return left is null && right is null;

        if (left is IEnumerable<byte> leftBytes)
        {
            return right switch
            {
                IEnumerable<byte> comparedBytes =>
                    leftBytes.SequenceEqual(comparedBytes),
                string rightText =>
                    string.Equals(
                        ProjectByteSequence(leftBytes),
                        rightText,
                        StringComparison.Ordinal),
                _ => false
            };
        }
        if (right is IEnumerable<byte> rightBytes)
        {
            return left is string leftText &&
                string.Equals(
                    leftText,
                    ProjectByteSequence(rightBytes),
                    StringComparison.Ordinal);
        }

        if (left is float leftFloat)
        {
            return right switch
            {
                float comparedFloat =>
                    BitConverter.SingleToInt32Bits(leftFloat) ==
                    BitConverter.SingleToInt32Bits(comparedFloat),
                string rightText =>
                    string.Equals(
                        ProjectFloat(leftFloat),
                        rightText,
                        StringComparison.Ordinal),
                _ => false
            };
        }
        if (right is float rightFloat)
        {
            return left is string leftText &&
                string.Equals(
                    leftText,
                    ProjectFloat(rightFloat),
                    StringComparison.Ordinal);
        }
        if (left is double leftDouble)
        {
            return right switch
            {
                double comparedDouble =>
                    BitConverter.DoubleToInt64Bits(leftDouble) ==
                    BitConverter.DoubleToInt64Bits(comparedDouble),
                string rightText =>
                    string.Equals(
                        ProjectDouble(leftDouble),
                        rightText,
                        StringComparison.Ordinal),
                _ => false
            };
        }
        if (right is double rightDouble)
        {
            return left is string leftText &&
                string.Equals(
                    leftText,
                    ProjectDouble(rightDouble),
                    StringComparison.Ordinal);
        }

        Type leftType = left.GetType();
        Type rightType = right.GetType();
        bool leftScalar = IsScalar(left, leftType);
        bool rightScalar = IsScalar(right, rightType);
        if (leftScalar || rightScalar)
        {
            return leftScalar &&
                rightScalar &&
                ScalarsSemanticallyEqual(left, right, leftType, rightType);
        }

        if (left is IEnumerable || right is IEnumerable)
        {
            return left is IEnumerable leftValues &&
                right is IEnumerable rightValues &&
                EnumerablesSemanticallyEqual(
                    leftValues,
                    rightValues,
                    context);
        }

        if (!string.Equals(
                leftType.FullName,
                rightType.FullName,
                StringComparison.Ordinal))
        {
            return false;
        }

        bool leftPreservesIdentity = PreservesAuthoredIdentity(leftType);
        bool rightPreservesIdentity = PreservesAuthoredIdentity(rightType);
        if (leftPreservesIdentity != rightPreservesIdentity)
            return false;
        if (leftPreservesIdentity)
        {
            bool leftSeen = context.LeftIds.TryGetValue(left, out int leftId);
            bool rightSeen = context.RightIds.TryGetValue(right, out int rightId);
            if (leftSeen || rightSeen)
                return leftSeen && rightSeen && leftId == rightId;

            int id = context.LeftIds.Count + 1;
            context.LeftIds.Add(left, id);
            context.RightIds.Add(right, id);
        }

        PropertyInfo[] leftProperties = GetProjectedProperties(leftType);
        PropertyInfo[] rightProperties = GetProjectedProperties(rightType);
        if (leftProperties.Length != rightProperties.Length)
            return false;
        for (int index = 0; index < leftProperties.Length; index++)
        {
            PropertyInfo leftProperty = leftProperties[index];
            PropertyInfo rightProperty = rightProperties[index];
            if (!string.Equals(
                    leftProperty.Name,
                    rightProperty.Name,
                    StringComparison.Ordinal) ||
                !SemanticallyEquals(
                    GetProjectedPropertyValue(leftProperty, left),
                    GetProjectedPropertyValue(rightProperty, right),
                    context))
            {
                return false;
            }
        }

        return true;
    }

    private static bool EnumerablesSemanticallyEqual(
        IEnumerable left,
        IEnumerable right,
        ComparisonContext context)
    {
        IEnumerator leftEnumerator = left.GetEnumerator();
        IEnumerator rightEnumerator = right.GetEnumerator();
        try
        {
            while (true)
            {
                bool hasLeft = leftEnumerator.MoveNext();
                bool hasRight = rightEnumerator.MoveNext();
                if (hasLeft != hasRight)
                    return false;
                if (!hasLeft)
                    return true;
                if (!SemanticallyEquals(
                        leftEnumerator.Current,
                        rightEnumerator.Current,
                        context))
                {
                    return false;
                }
            }
        }
        finally
        {
            (leftEnumerator as IDisposable)?.Dispose();
            (rightEnumerator as IDisposable)?.Dispose();
        }
    }

    private static bool ScalarsSemanticallyEqual(
        object left,
        object right,
        Type leftType,
        Type rightType)
    {
        if (leftType == rightType && left is not decimal)
            return left.Equals(right);

        // Scalar projections omit runtime type. The uncommon cross-type case
        // (for example char versus a one-character string) therefore compares
        // the exact JSON scalar spelling used by Project.
        return string.Equals(
            JsonSerializer.Serialize(ProjectScalar(left)),
            JsonSerializer.Serialize(ProjectScalar(right)),
            StringComparison.Ordinal);
    }

    private static bool IsScalar(object value, Type type) =>
        value is string or char or bool ||
        type.IsEnum ||
        type.IsPrimitive ||
        value is decimal;

    private static string ProjectByteSequence(IEnumerable<byte> value) =>
        value is byte[] bytes
            ? Convert.ToHexString(bytes)
            : Convert.ToHexString(value.ToArray());

    private static string ProjectFloat(float value) =>
        $"f:{BitConverter.SingleToInt32Bits(value):X8}";

    private static string ProjectDouble(double value) =>
        $"d:{BitConverter.DoubleToInt64Bits(value):X16}";

    private static PropertyInfo[] GetProjectedProperties(Type type) =>
        ProjectedPropertiesByType.GetOrAdd(
            type,
            static value => value
                .GetProperties(
                    BindingFlags.Instance |
                    BindingFlags.Public)
                .Where(property =>
                    property.CanRead &&
                    property.GetIndexParameters().Length == 0 &&
                    !Skip(property))
                .OrderBy(
                    property => property.Name,
                    StringComparer.Ordinal)
                .ToArray());

    private static object? GetProjectedPropertyValue(
        PropertyInfo property,
        object instance)
    {
        object? value = property.GetValue(instance);
        return value is string text && IsWireOnlyReferenceName(property)
            ? text.TrimStart(',')
            : value;
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
        type == typeof(SetLocalVarData) ||
        type == typeof(StaticDvar) ||
        type == typeof(EditFieldDef) ||
        type == typeof(ListBoxDef) ||
        type == typeof(MultiDef) ||
        type == typeof(NewsTickerDef) ||
        type == typeof(TextScrollDef);

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

    private sealed class ComparisonContext
    {
        public Dictionary<object, int> LeftIds { get; } =
            new(ReferenceEqualityComparer.Instance);

        public Dictionary<object, int> RightIds { get; } =
            new(ReferenceEqualityComparer.Instance);
    }
}

using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using IW4.Assets.Assets.Material;
using IW4.FastFiles.Emitters.Assets;

namespace IW4.Studio.Documents;

/// <summary>
/// Streams a deterministic authored-content fingerprint while excluding
/// destination addresses, packed offsets, and allocation-local alias tokens.
/// Pointer source-form enums and symbolic XAsset identities remain semantic.
/// Method-backed opaque payload bytes are appended explicitly.
/// </summary>
public static class RelocationInvariantAssetSemanticDigest
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]>
        PropertiesByType = new();
    private static readonly JsonSerializerOptions JsonOptions =
        CreateJsonOptions();

    public static string Compute(
        IXAssetBuildData value,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        cancellationToken.ThrowIfCancellationRequested();
        using IncrementalHash hash =
            IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using (var sink = new IncrementalHashWriteStream(
                   hash,
                   cancellationToken))
        {
            JsonSerializer.Serialize(
                sink,
                value,
                value.GetType(),
                JsonOptions);
        }
        AppendMethodBackedPayloads(
            hash,
            value,
            "$",
            new HashSet<object>(
                ReferenceEqualityComparer.Instance),
            cancellationToken);
        return Convert.ToHexString(hash.GetHashAndReset())
            .ToLowerInvariant();
    }

    /// <summary>
    /// Computes the same detached semantic digest for a loaded Material by
    /// first projecting it through the canonical authoring adapter. Runtime
    /// pointers and allocation addresses therefore cannot satisfy an
    /// immutable dependency-evidence comparison accidentally.
    /// </summary>
    public static string ComputeLoadedMaterial(
        MaterialAsset material,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(material);
        MaterialBuildData buildData =
            MaterialAuthoredSnapshot
                .FromLoaded(material)
                .Data;
        return Compute(buildData, cancellationToken);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(RemoveLayoutAndRuntimeState);
        var options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            ReferenceHandler = ReferenceHandler.IgnoreCycles,
            TypeInfoResolver = resolver
        };
        options.Converters.Add(new ExactSingleConverter());
        options.Converters.Add(new ExactDoubleConverter());
        options.Converters.Add(
            new AssetBuildDataInterfaceConverter());
        return options;
    }

    private static void RemoveLayoutAndRuntimeState(
        JsonTypeInfo type)
    {
        foreach (JsonPropertyInfo property in type.Properties
                     .Where(ShouldSkip)
                     .ToArray())
        {
            type.Properties.Remove(property);
        }
    }

    private static bool ShouldSkip(
        JsonPropertyInfo property)
    {
        Type type =
            Nullable.GetUnderlyingType(property.PropertyType) ??
            property.PropertyType;
        string? declaringNamespace =
            (property.AttributeProvider as MemberInfo)
                ?.DeclaringType?.Namespace;
        bool runtimeAssetProperty =
            declaringNamespace?.StartsWith(
                "IW4.Assets.",
                StringComparison.Ordinal) == true;
        if (type.Namespace == "IW4.FastFiles.Pointers" ||
            type.FullName?.Contains(
                "XPointer",
                StringComparison.Ordinal) == true ||
            (!type.IsEnum &&
             type.Namespace == "IW4.FastFiles.Zone") ||
            type.Name.EndsWith(
                "StorageToken",
                StringComparison.Ordinal) ||
            type.Name.EndsWith(
                "AliasToken",
                StringComparison.Ordinal))
        {
            return true;
        }
        if (IsLayoutRaw(property.Name))
            return true;

        if (property.Name is
            "StagingAddress" or
            "RuntimeAddress" or
            "RuntimeParentAddress" or
            "DestinationAddress")
        {
            return true;
        }
        if (!runtimeAssetProperty)
            return false;

        return property.Name is
            "Offset" or
            "SerializedSurfaceState" or
            "SkyImage" or
            "ReflectionProbeImagePointers" or
            "ReflectionProbeImages" or
            "Primary" or
            "Secondary" or
            "LightmapOverridePrimary" or
            "LightmapOverrideSecondary" or
            "Material" or
            "SpriteMaterial" or
            "FlareMaterial" or
            "OutdoorImage" or
            "Model" or
            "IncomingDefinition" or
            "IncomingDefinitions" or
            "BackgroundMaterial" or
            "SelectIconMaterial" or
            "FocusSoundAsset" or
            "Info" or
            "LastExecuteTime" or
            "LastResult" or
            "NextTime" or
            "RuntimeParentPointer" or
            "SceneEntCellBits" or
            "ReflectionProbeTextures" or
            "LightmapPrimaryTextures" or
            "LightmapSecondaryTextures" or
            "WorldVbHandle" or
            "WorldVbOffset" or
            "LayerVbHandle" or
            "LayerVbOffset" or
            "CellCasterBits" or
            "CellCasterBits2" or
            "SceneDynModels" or
            "SceneDynBrushes" or
            "PrimaryLightEntityShadowVis" or
            "PrimaryLightDynEntShadowVis0" or
            "PrimaryLightDynEntShadowVis1" or
            "PrimaryLightForModelDynEnt" or
            "SModelVisData" or
            "SurfaceVisData" or
            "SurfaceMaterials" or
            "SurfaceCastsSunShadow" or
            "AuthoredSurfaceIndexByRuntimeSlot" or
            "DynEntCellBits" or
            "DynEntVisData" or
            "UmbraGateData" or
            "UmbraGateData2";
    }

    private static bool IsLayoutRaw(string name) =>
        name is
            "InsertOwnerRaw" or
            "InlineOwnerRaw" or
            "TargetAlias" or
            "OwnerAlias" ||
        name.StartsWith(
            "Imported",
            StringComparison.Ordinal) &&
        (name.EndsWith(
             "Raw",
             StringComparison.Ordinal) ||
         name.EndsWith(
             "Raws",
             StringComparison.Ordinal)) ||
        name.EndsWith(
            "PointerRaw",
            StringComparison.Ordinal) ||
        name.EndsWith(
            "PointerRaws",
            StringComparison.Ordinal);

    private static void AppendMethodBackedPayloads(
        IncrementalHash hash,
        object? value,
        string path,
        ISet<object> active,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (value is null)
            return;
        Type type = value.GetType();
        if (value is string ||
            value is byte[] ||
            type.IsEnum ||
            type.IsPrimitive ||
            value is decimal)
        {
            return;
        }

        bool tracksReference =
            !type.IsValueType;
        if (tracksReference && !active.Add(value))
            return;
        try
        {
            AppendOpaqueValue(hash, value, path);
            // Public byte sequences are already covered by the streaming
            // JSON pass. Treat them as terminal here so large packed map
            // buffers are not boxed and traversed one byte at a time.
            if (value is IEnumerable<byte>)
                return;
            if (value is IEnumerable sequence)
            {
                if (SequenceCannotContainOpaquePayload(type))
                    return;
                int index = 0;
                foreach (object? item in sequence)
                {
                    AppendMethodBackedPayloads(
                        hash,
                        item,
                        $"{path}[{index++}]",
                        active,
                        cancellationToken);
                }
                return;
            }

            foreach (PropertyInfo property in
                     PropertiesByType.GetOrAdd(
                         type,
                         static value => value
                             .GetProperties(
                                 BindingFlags.Instance |
                                 BindingFlags.Public)
                             .Where(property =>
                                 property.CanRead &&
                                 property.GetIndexParameters().Length == 0)
                             .OrderBy(
                                 property => property.Name,
                                 StringComparer.Ordinal)
                             .ToArray()))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ShouldSkipReflection(property) ||
                    property.PropertyType.Namespace?.StartsWith(
                        "IW4.Assets.",
                        StringComparison.Ordinal) == true)
                {
                    continue;
                }
                AppendMethodBackedPayloads(
                    hash,
                    property.GetValue(value),
                    $"{path}.{property.Name}",
                    active,
                    cancellationToken);
            }
        }
        finally
        {
            if (tracksReference)
                active.Remove(value);
        }
    }

    private static bool SequenceCannotContainOpaquePayload(
        Type type)
    {
        Type? elementType = type.IsArray
            ? type.GetElementType()
            : type.GetInterfaces()
                .Where(candidate =>
                    candidate.IsGenericType &&
                    candidate.GetGenericTypeDefinition() ==
                        typeof(IEnumerable<>))
                .Select(candidate =>
                    candidate.GetGenericArguments()[0])
                .FirstOrDefault();
        return elementType is not null &&
               (elementType.IsValueType ||
                elementType == typeof(string));
    }

    private static bool ShouldSkipReflection(
        PropertyInfo property)
    {
        Type type =
            Nullable.GetUnderlyingType(property.PropertyType) ??
            property.PropertyType;
        return type.Namespace == "IW4.FastFiles.Pointers" ||
               type.FullName?.Contains(
                   "XPointer",
                   StringComparison.Ordinal) == true ||
               IsLayoutRaw(property.Name);
    }

    private static void AppendOpaqueValue(
        IncrementalHash hash,
        object value,
        string path)
    {
        switch (value)
        {
            case IRawFileBuildData rawFile:
                Append(
                    hash,
                    $"{path}:rawFile",
                    rawFile.GetSerializedPayloadCopy());
                break;
            case ILeaderboardColumnBuildData column:
                Append(
                    hash,
                    $"{path}:leaderboardPad",
                    column.GetPad0DTo0FCopy());
                break;
            case ILightDefBuildData lightDef:
                Append(
                    hash,
                    $"{path}:lightDefPad",
                    lightDef.GetPad09To0BCopy());
                break;
            case GGlassDataBuildData glass:
                Append(
                    hash,
                    $"{path}:glassPad",
                    glass.GetPad14To7FCopy());
                break;
            case IMapEntsBuildData mapEnts:
                Append(
                    hash,
                    $"{path}:mapEntsBytes",
                    mapEnts.GetEntityStringBytesCopy());
                Append(
                    hash,
                    $"{path}:mapEntsPad",
                    mapEnts.GetPad29To2BCopy());
                break;
            case IAddonMapEntsBuildData addonMapEnts:
                Append(
                    hash,
                    $"{path}:addonMapEntsBytes",
                    addonMapEnts.GetEntityStringBytesCopy());
                break;
            case IMaterialShaderBuildData shader:
                AppendNullable(
                    hash,
                    $"{path}:shaderData",
                    shader.GetDataCopy());
                Append(
                    hash,
                    $"{path}:shaderProgram",
                    shader.GetProgramBytesCopy());
                break;
            case ILoadedSoundBuildData sound:
                AppendNullable(
                    hash,
                    $"{path}:soundSeek",
                    sound.GetSeekTableCopy());
                AppendNullable(
                    hash,
                    $"{path}:soundPhysical",
                    sound.GetPhysicalDataCopy());
                break;
            case IGfxImageBuildData image:
                AppendNullable(
                    hash,
                    $"{path}:imagePayload",
                    image.GetPayloadCopy());
                break;
        }
    }

    private static void AppendNullable(
        IncrementalHash hash,
        string role,
        byte[]? bytes)
    {
        Append(hash, role);
        Append(
            hash,
            bytes is null ? "null" : "value");
        if (bytes is not null)
            Append(hash, bytes);
    }

    private static void Append(
        IncrementalHash hash,
        string role,
        ReadOnlySpan<byte> bytes)
    {
        Append(hash, role);
        Append(hash, bytes);
    }

    private static void Append(
        IncrementalHash hash,
        string value) =>
        Append(hash, Encoding.UTF8.GetBytes(value));

    private static void Append(
        IncrementalHash hash,
        ReadOnlySpan<byte> bytes)
    {
        Span<byte> length = stackalloc byte[sizeof(int)];
        System.Buffers.Binary.BinaryPrimitives
            .WriteInt32LittleEndian(
                length,
                bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private sealed class AssetBuildDataInterfaceConverter
        : JsonConverter<IXAssetBuildData>
    {
        public override IXAssetBuildData? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options) =>
            throw new NotSupportedException();

        public override void Write(
            Utf8JsonWriter writer,
            IXAssetBuildData value,
            JsonSerializerOptions options) =>
            JsonSerializer.Serialize(
                writer,
                value,
                value.GetType(),
                options);
    }

    private sealed class ExactSingleConverter
        : JsonConverter<float>
    {
        public override float Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options) =>
            throw new NotSupportedException();

        public override void Write(
            Utf8JsonWriter writer,
            float value,
            JsonSerializerOptions options) =>
            writer.WriteStringValue(
                $"f:{BitConverter.SingleToInt32Bits(value):X8}");
    }

    private sealed class ExactDoubleConverter
        : JsonConverter<double>
    {
        public override double Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options) =>
            throw new NotSupportedException();

        public override void Write(
            Utf8JsonWriter writer,
            double value,
            JsonSerializerOptions options) =>
            writer.WriteStringValue(
                $"d:{BitConverter.DoubleToInt64Bits(value):X16}");
    }

    private sealed class IncrementalHashWriteStream(
        IncrementalHash hash,
        CancellationToken cancellationToken)
        : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length =>
            throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();

        public override long Seek(
            long offset,
            SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(
            byte[] buffer,
            int offset,
            int count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            hash.AppendData(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            cancellationToken.ThrowIfCancellationRequested();
            hash.AppendData(buffer);
        }
    }
}

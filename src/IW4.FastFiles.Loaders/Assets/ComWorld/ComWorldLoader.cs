using IW4.FastFiles.Loaders.Database;
using IW4.Assets.Assets.ComWorld;
using IW4.Assets.Math;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;
using IW4.Runtime.IO;

namespace IW4.FastFiles.Loaders.Assets.ComWorld;

public sealed class ComWorldLoader
{
    public ComWorldAsset LoadFromAssetPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        if (pointer.Type == PointerType.Null)
            throw new InvalidDataException("Top-level ComWorld pointer is null.");

        if (pointer.Type == PointerType.Offset)
        {
            ComWorldAsset canonical = context.ResolveCanonicalAsset<ComWorldAsset>(
                    pointer,
                    XAssetType.ComMap)
                ?? throw new InvalidDataException(
                    $"Top-level ComWorld pointer 0x{unchecked((uint)pointer.Raw):X8} " +
                    "does not resolve to a canonical ComMap asset.");
            context.PatchCanonicalAssetPointerCell(
                pointer,
                canonical,
                "Packed ComWorld pointer has no destination cell.",
                "Canonical ComWorld has no runtime address.");
            return canonical;
        }

        if (pointer.Type is not (PointerType.Inline or PointerType.Insert))
            throw new InvalidDataException($"Top-level ComWorld pointer 0x{pointer.Raw:X8} does not reference inline/insert payload data.");

        ProviderRegistrationOccurrence providerRegistration = context.BeginProviderRegistration(pointer);

        context.Blocks.Push(XFileBlockType.TEMP);
        try
        {
            XBlockAddress rootAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
            ComWorldAsset comWorld = ReadComWorld(cursor, rootAddress, context);
            ComWorldAsset canonical = context.DB_AddXAsset(
                XAssetType.ComMap,
                comWorld.Name,
                comWorld,
                providerRegistration);

            return canonical;
        }
        finally
        {
            context.Blocks.Pop();
        }
    }

    private static ComWorldAsset ReadComWorld(
        FastFileCursor cursor,
        XBlockAddress expectedRootAddress,
        DbLoadExecutionContext context)
    {
        int sourceOffset = cursor.Offset;
        byte[] rootBytes = context.Blocks.Load(cursor, ComWorldAsset.SerializedSize, out XBlockAddress rootAddress);
        if (rootAddress != expectedRootAddress)
            throw new InvalidDataException($"ComWorld pointer patched to {expectedRootAddress}, but root loaded at {rootAddress}.");

        var rootCursor = new FastFileCursor(rootBytes, rootAddress);
        XPointer<string> namePointer = context.PointerReader.ReadPointer<string>(rootCursor, XPointerResolutionMode.Direct);
        int isInUse = rootCursor.ReadInt32();
        int primaryLightCount = rootCursor.ReadInt32();
        XPointer<ComPrimaryLight[]> primaryLightsPointer =
            context.PointerReader.ReadPointer<ComPrimaryLight[]>(rootCursor, XPointerResolutionMode.Direct);

        if (rootCursor.Offset != ComWorldAsset.SerializedSize)
            throw new InvalidDataException($"ComWorld consumed 0x{rootCursor.Offset:X} bytes instead of 0x{ComWorldAsset.SerializedSize:X}.");

        if (primaryLightCount < 0 || primaryLightCount > 0x10000)
        {
            throw new InvalidDataException(
                $"ComWorld at source 0x{sourceOffset:X} has invalid primaryLightCount {primaryLightCount}; " +
                $"name=0x{namePointer.Raw:X8}, primaryLights=0x{primaryLightsPointer.Raw:X8}.");
        }


        string? name;
        IReadOnlyList<ComPrimaryLight> primaryLights;
        context.Blocks.Push(XFileBlockType.LARGE);
        try
        {
            name = context.PointerReader.LoadXString(cursor, namePointer);
            primaryLights = ReadPrimaryLights(cursor, primaryLightsPointer.Untyped, primaryLightCount, context);
        }
        finally
        {
            context.Blocks.Pop();
        }


        return new ComWorldAsset
        {
            Offset = sourceOffset,
            RuntimeAddress = rootAddress,
            NamePointer = namePointer,
            Name = name,
            IsInUse = isInUse,
            PrimaryLightCount = primaryLightCount,
            PrimaryLightsPointer = primaryLightsPointer,
            PrimaryLights = primaryLights
        };
    }


    private static IReadOnlyList<ComPrimaryLight> ReadPrimaryLights(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        if (pointer.Type == PointerType.Null)
        {
            if (count != 0)
                throw new InvalidDataException($"ComWorld primaryLights is null with non-zero count {count}.");

            return [];
        }

        if (pointer.Type == PointerType.Offset)
        {
            throw new InvalidDataException(
                $"ComWorld primaryLights pointer 0x{pointer.Raw:X8} is a packed offset pointer, but the PS3 ComWorld body only proves null/non-null inline array loading.");
        }

        if (pointer.Type is not (PointerType.Inline or PointerType.Insert))
            throw new InvalidDataException($"ComWorld primaryLights pointer 0x{pointer.Raw:X8} is not inline/insert/null.");

        int sourceStart = cursor.Offset;
        int byteCount = checked(count * ComPrimaryLight.SerializedSize);
        XBlockAddress primaryLightsAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
        byte[] lightBytes = context.Blocks.Load(
            cursor,
            byteCount,
            out XBlockAddress loadedAddress);
        if (loadedAddress != primaryLightsAddress)
            throw new InvalidDataException($"ComWorld primaryLights pointer patched to {primaryLightsAddress}, but array loaded at {loadedAddress}.");


        var lightCursor = new FastFileCursor(lightBytes, primaryLightsAddress);
        var primaryLights = new ComPrimaryLight[count];
        for (int i = 0; i < primaryLights.Length; i++)
        {
            int rowStart = lightCursor.Offset;
            XBlockAddress rowAddress = primaryLightsAddress.Add(rowStart);
            GfxLightType type = (GfxLightType)lightCursor.ReadByte();
            byte canUseShadowMap = lightCursor.ReadByte();
            byte exponent = lightCursor.ReadByte();
            byte unused = lightCursor.ReadByte();
            Vec3 color = ReadVec3(lightCursor);
            Vec3 dir = ReadVec3(lightCursor);
            Vec3 origin = ReadVec3(lightCursor);
            float radius = lightCursor.ReadSingle();
            float cosHalfFovOuter = lightCursor.ReadSingle();
            float cosHalfFovInner = lightCursor.ReadSingle();
            float cosHalfFovExpanded = lightCursor.ReadSingle();
            float rotationLimit = lightCursor.ReadSingle();
            float translationLimit = lightCursor.ReadSingle();
            XPointer<string> defNamePointer = context.PointerReader.ReadPointer<string>(lightCursor, XPointerResolutionMode.Direct);

            if (lightCursor.Offset - rowStart != ComPrimaryLight.SerializedSize)
                throw new InvalidDataException($"ComPrimaryLight consumed 0x{lightCursor.Offset - rowStart:X} bytes instead of 0x{ComPrimaryLight.SerializedSize:X}.");

            string? defName = context.PointerReader.LoadXString(cursor, defNamePointer);

            primaryLights[i] = new ComPrimaryLight
            {
                Offset = rowAddress.Offset,
                Type = type,
                CanUseShadowMapRaw = canUseShadowMap,
                Exponent = exponent,
                Unused = unused,
                Color = color,
                Dir = dir,
                Origin = origin,
                Radius = radius,
                CosHalfFovOuter = cosHalfFovOuter,
                CosHalfFovInner = cosHalfFovInner,
                CosHalfFovExpanded = cosHalfFovExpanded,
                RotationLimit = rotationLimit,
                TranslationLimit = translationLimit,
                DefNamePointer = defNamePointer,
                DefName = defName
            };
        }


        return primaryLights;
    }

    private static Vec3 ReadVec3(FastFileCursor cursor)
    {
        return new Vec3
        {
            X = cursor.ReadSingle(),
            Y = cursor.ReadSingle(),
            Z = cursor.ReadSingle()
        };
    }

}

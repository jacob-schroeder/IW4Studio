using System.IO.Compression;
using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Emission;

namespace IW4.FastFiles.Emitters.Assets;

public sealed class RawFileBodyEmitter : IXAssetBodyEmitter
{
    public XAssetType AssetType => XAssetType.RawFile;

    public IReadOnlyList<EmissionError> Validate(IXAssetBuildData buildData, int? rowIndex = null)
    {
        ArgumentNullException.ThrowIfNull(buildData);
        var diagnostics = AssetBodyEmitterHelpers.ValidateIdentity(buildData, AssetType, rowIndex);
        if (buildData is not IRawFileBuildData raw)
        {
            diagnostics.Add(new EmissionError("body", "RawFile build data does not implement IRawFileBuildData.", rowIndex, AssetType));
            return diagnostics;
        }

        byte[] payload = raw.GetSerializedPayloadCopy();
        if (string.IsNullOrWhiteSpace(raw.OriginalName) || !AssetBodyEmitterHelpers.IsLatin1CString(raw.OriginalName))
            diagnostics.Add(new EmissionError("name", "RawFile name must be a non-empty Latin-1 C string.", rowIndex, AssetType));
        if (raw.CompressedLength < 0 || raw.UncompressedLength < 0)
            diagnostics.Add(new EmissionError("length", "RawFile lengths cannot be negative.", rowIndex, AssetType));
        if (raw.CompressedLength > 0)
        {
            if (!raw.HasBuffer || payload.Length != raw.CompressedLength)
                diagnostics.Add(new EmissionError("buffer", "Compressed RawFile payload must exactly match compressedLen.", rowIndex, AssetType));
            else if (raw.UncompressedLength >= 0 && !raw.PreserveOpaqueCompressedPayload)
            {
                string? compressionFailure = ValidateCompressedPayload(payload, raw.UncompressedLength);
                if (compressionFailure is not null)
                    diagnostics.Add(new EmissionError("buffer", compressionFailure, rowIndex, AssetType));
            }
        }
        else if (!raw.HasBuffer)
        {
            if (raw.UncompressedLength != 0 || payload.Length != 0)
                diagnostics.Add(new EmissionError("buffer", "A null RawFile buffer requires zero length and no payload.", rowIndex, AssetType));
        }
        else
        {
            int expected = -1;
            try { expected = checked(raw.UncompressedLength + 1); }
            catch (OverflowException) { }
            if (expected < 0 || payload.Length != expected || payload.Length == 0 || payload[^1] != 0)
                diagnostics.Add(new EmissionError("buffer", "Uncompressed RawFile payload must be len + 1 and end in a null byte.", rowIndex, AssetType));
        }

        return diagnostics;
    }

    public AssetBodyEmission Plan(IXAssetBuildData buildData, EmissionPlan plan, int? rowIndex = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        AssetBodyEmitterHelpers.RequireNoDiagnostics(Validate(buildData, rowIndex));
        IRawFileBuildData raw = (IRawFileBuildData)buildData;
        var segments = new List<EmissionBlockSegment>();
        plan.Push(XFileBlockType.TEMP);
        EmissionAddress root = plan.Allocate(0x10, alignment: 4);
        plan.Push(XFileBlockType.LARGE);
        IDictionary<string, EmissionAddress> aliases = plan.StringAliases;
        PlannedString name = AssetBodyEmitterHelpers.PlanString(raw.OriginalName, plan, segments, aliases)!.Value;
        EmissionAddress? buffer = raw.HasBuffer ? plan.Allocate(raw.GetSerializedPayloadCopy().Length) : null;
        if (buffer is { } payloadAddress)
            segments.Add(new EmissionBlockSegment(payloadAddress, raw.GetSerializedPayloadCopy()));
        plan.Pop(XFileBlockType.LARGE);
        plan.Pop(XFileBlockType.TEMP);

        var rootWriter = new XSourceWriter();
        rootWriter.WriteInt32(AssetBodyEmitterHelpers.SourcePointer(name));
        rootWriter.WriteInt32(raw.CompressedLength);
        rootWriter.WriteInt32(raw.UncompressedLength);
        rootWriter.WriteInt32(buffer is null ? 0 : -1);
        segments.Add(new EmissionBlockSegment(root, rootWriter.ToArray()));
        return new AssetBodyEmission(AssetType, root, segments);
    }

    private static string? ValidateCompressedPayload(
        ReadOnlySpan<byte> payload,
        int declaredUncompressedLength)
    {
        try
        {
            using var input = new MemoryStream(payload.ToArray(), writable: false);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress, leaveOpen: false);
            Span<byte> buffer = stackalloc byte[4096];
            long inflatedLength = 0;
            int read;
            while ((read = zlib.Read(buffer)) != 0)
            {
                inflatedLength += read;
                if (inflatedLength > declaredUncompressedLength)
                {
                    return $"Compressed RawFile inflated beyond its declared {declaredUncompressedLength}-byte logical length.";
                }
            }

            return inflatedLength == declaredUncompressedLength
                ? null
                : $"Compressed RawFile inflated to {inflatedLength} bytes; expected {declaredUncompressedLength}.";
        }
        catch (InvalidDataException)
        {
            return "Compressed RawFile payload is not a valid zlib stream.";
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            return "Compressed RawFile payload is not a valid zlib stream.";
        }
    }
}

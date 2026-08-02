using IW4.Assets.Zone;
using System.Security.Cryptography;
using IW4.Assets.Assets.TechniqueSet;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Render.Shaders;

/// <summary>
/// Atomic immutable shader-byte and provider snapshot. Raw pointer metadata is
/// retained even when no usable program is resolved.
/// </summary>
public sealed class MapRenderShaderProgramResolution
{
    private readonly byte[] _data;
    private readonly byte[] _rootProgramBytes;

    public MapRenderShaderProgramResolution(
        XPointerReference pointer,
        MaterialShaderKind kind,
        MaterialShaderAsset? shader,
        MapRenderShaderProgramResolutionKind resolutionKind,
        MapRenderShaderProgramProviderIdentity? providerIdentity)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (!Enum.IsDefined(resolutionKind))
            throw new ArgumentOutOfRangeException(nameof(resolutionKind));
        if (resolutionKind == MapRenderShaderProgramResolutionKind.CanonicalActiveProvider &&
            (providerIdentity is null ||
             providerIdentity.IsReferencePlaceholder ||
             !providerIdentity.IsActiveCanonicalProvider))
        {
            throw new ArgumentException(
                "Canonical program resolution requires the active provider identity.",
                nameof(providerIdentity));
        }

        _data = shader?.Data?.ToArray() ?? [];
        _rootProgramBytes = shader?.ProgramBytes?.ToArray() ?? [];
        Pointer = pointer;
        Kind = kind;
        ResolutionKind = resolutionKind;
        ProviderIdentity = providerIdentity;
        Name = shader?.Name ?? string.Empty;
        DeclaredDataSize = shader?.DataSize ?? 0;
        LoadedDataSize = _data.Length;
        DataSha256 = _data.Length == 0
            ? string.Empty
            : Convert.ToHexString(SHA256.HashData(_data));
        RuntimeAddress = shader?.RuntimeAddress;
        Data = new ReadOnlyMemory<byte>(_data);
        RootProgramBytes = new ReadOnlyMemory<byte>(_rootProgramBytes);
    }

    public XPointerReference Pointer { get; }

    public MaterialShaderKind Kind { get; }

    public MapRenderShaderProgramResolutionKind ResolutionKind { get; }

    public MapRenderShaderProgramProviderIdentity? ProviderIdentity { get; }

    public string Name { get; }

    public uint DeclaredDataSize { get; }

    public int LoadedDataSize { get; }

    public string DataSha256 { get; }

    public XRuntimeAddress? RuntimeAddress { get; }

    public ReadOnlyMemory<byte> Data { get; }

    public ReadOnlyMemory<byte> RootProgramBytes { get; }

    public bool HasProgramData => _data.Length > 0;

    internal byte[] CloneData() => _data.ToArray();
}

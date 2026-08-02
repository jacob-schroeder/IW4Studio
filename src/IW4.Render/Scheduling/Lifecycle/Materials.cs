using IW4.Assets.Assets.TechniqueSet;

namespace IW4.Render.Scheduling.Lifecycle;

/// <summary>One exact shader argument required by a fullscreen material.</summary>
public sealed record MapRenderNormalCameraMaterialArgumentContract
{
    public MapRenderNormalCameraMaterialArgumentContract(
        MaterialShaderArgumentType type,
        ushort destination,
        uint rawValue)
    {
        if (!Enum.IsDefined(type))
            throw new ArgumentOutOfRangeException(nameof(type));

        Type = type;
        Destination = destination;
        RawValue = rawValue;
    }

    public MaterialShaderArgumentType Type { get; }

    public ushort Destination { get; }

    public uint RawValue { get; }
}

/// <summary>
/// Exact asset, program, state-word, and argument recipe for a fullscreen
/// EditorPreview material.
/// </summary>
public sealed record MapRenderNormalCameraMaterialAssetContract
{
    private readonly MapRenderNormalCameraMaterialArgumentContract[]
        _arguments;
    private readonly MapRenderNormalCameraMaterialArgumentContract[]
        _codePixelConstants;

    public MapRenderNormalCameraMaterialAssetContract(
        string materialName,
        string techniqueSetName,
        string techniqueName,
        int techniqueSlot,
        ushort techniqueFlags,
        string vertexShaderName,
        string pixelShaderName,
        uint stateBits0,
        uint stateBits1,
        IReadOnlyList<MapRenderNormalCameraMaterialArgumentContract> arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(materialName);
        ArgumentException.ThrowIfNullOrWhiteSpace(techniqueSetName);
        ArgumentException.ThrowIfNullOrWhiteSpace(techniqueName);
        if (techniqueSlot < 0)
            throw new ArgumentOutOfRangeException(nameof(techniqueSlot));
        ArgumentException.ThrowIfNullOrWhiteSpace(vertexShaderName);
        ArgumentException.ThrowIfNullOrWhiteSpace(pixelShaderName);
        ArgumentNullException.ThrowIfNull(arguments);

        MaterialName = materialName;
        TechniqueSetName = techniqueSetName;
        TechniqueName = techniqueName;
        TechniqueSlot = techniqueSlot;
        TechniqueFlags = techniqueFlags;
        VertexShaderName = vertexShaderName;
        PixelShaderName = pixelShaderName;
        StateBits0 = stateBits0;
        StateBits1 = stateBits1;
        _arguments = arguments.ToArray();
        _codePixelConstants = _arguments
            .Where(argument =>
                argument.Type == MaterialShaderArgumentType.CodePixelConst)
            .ToArray();
    }

    public string MaterialName { get; }

    public string TechniqueSetName { get; }

    public string TechniqueName { get; }

    public int TechniqueSlot { get; }

    public ushort TechniqueFlags { get; }

    public int PassCount => 1;

    public string VertexShaderName { get; }

    public string PixelShaderName { get; }

    public uint StateBits0 { get; }

    public uint StateBits1 { get; }

    public IReadOnlyList<MapRenderNormalCameraMaterialArgumentContract>
        Arguments => _arguments;

    public IReadOnlyList<MapRenderNormalCameraMaterialArgumentContract>
        CodePixelConstants => _codePixelConstants;
}

namespace IW4.Assets.Assets.TechniqueSet;

/// <summary>
/// Update-frequency groups used when PS3 material arguments are compiled into
/// a pass. Custom code samplers are represented by the pass sampler flags,
/// outside its retained argument table.
/// </summary>
public enum MaterialUpdateFrequency : byte
{
    PerPrimitive = 0x0,
    PerObject = 0x1,
    Rarely = 0x2,
    Custom = 0x3
}

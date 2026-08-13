namespace IW4.Assets.Assets.Material;

/// <summary>
/// Material surface-type membership. The engine's default surface type is the
/// empty mask; authored surface types 1 through 30 map to bits 0 through 29.
/// </summary>
[Flags]
public enum MaterialSurfaceTypeBits : uint
{
    None = 0,
    Bark = 1u << 0,
    Brick = 1u << 1,
    Carpet = 1u << 2,
    Cloth = 1u << 3,
    Concrete = 1u << 4,
    Dirt = 1u << 5,
    Flesh = 1u << 6,
    Foliage = 1u << 7,
    Glass = 1u << 8,
    Grass = 1u << 9,
    Gravel = 1u << 10,
    Ice = 1u << 11,
    Metal = 1u << 12,
    Mud = 1u << 13,
    Paper = 1u << 14,
    Plaster = 1u << 15,
    Rock = 1u << 16,
    Sand = 1u << 17,
    Snow = 1u << 18,
    Water = 1u << 19,
    Wood = 1u << 20,
    Asphalt = 1u << 21,
    Ceramic = 1u << 22,
    Plastic = 1u << 23,
    Rubber = 1u << 24,
    Cushion = 1u << 25,
    Fruit = 1u << 26,
    PaintedMetal = 1u << 27,
    RiotShield = 1u << 28,
    Slush = 1u << 29
}

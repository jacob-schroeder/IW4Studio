namespace IW4.Assets.Assets.Material;

/// <summary>
/// State-table base index for the entry's ordinal technique slot. The wire
/// table stores only this byte; slot identity is its position in the fixed
/// 37-entry array.
/// </summary>
public readonly record struct MaterialStateBitsEntry(byte StateBitsIndex);

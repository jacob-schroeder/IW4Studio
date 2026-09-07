namespace MapConverter.Game.IW3.PC.Materials;

internal static class Iw3MaterialPropertyName
{
    internal static uint Hash(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        uint hash = 0;
        foreach (char character in value)
            hash = unchecked(hash * 33u ^ (byte)(character | 0x20));
        return hash;
    }
}

namespace MapConverter.IW3.PC.Assets;

/// <summary>A named registration in the source zone, in loader order. A leading comma marks a reference.</summary>
public sealed record Iw3PcAsset(int Id, string Type, string Name, int Offset, int[] Dependencies)
{
    internal static string CanonicalName(string name) =>
        name.TrimStart(',').Replace('\\', '/').Trim().ToLowerInvariant();

    internal readonly List<(string Type, string Name)> References = [];
    internal readonly List<string> StreamedSounds = [];
}

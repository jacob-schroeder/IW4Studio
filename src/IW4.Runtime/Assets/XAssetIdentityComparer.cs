using IW4.FastFiles.Zone;

namespace IW4.Runtime.Assets;

internal sealed class XAssetIdentityComparer : IEqualityComparer<(XAssetType Type, string Name)>
{
    public bool Equals((XAssetType Type, string Name) x, (XAssetType Type, string Name) y) =>
        x.Type == y.Type && StringComparer.Ordinal.Equals(x.Name, y.Name);

    public int GetHashCode((XAssetType Type, string Name) obj) =>
        HashCode.Combine(obj.Type, StringComparer.Ordinal.GetHashCode(obj.Name));
}

using IW4.Assets.Assets;

namespace IW4.AssetExchange.SourceFormat;

public interface ISourceExchange<in T> where T : BaseAsset
{
    public abstract void Link(T  asset);

    public abstract void Unlink();
}
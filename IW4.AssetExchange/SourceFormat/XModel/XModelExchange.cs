using IW4.Assets.Assets.XModel;

namespace IW4.AssetExchange.SourceFormat.XModel;

public class XModelExchange : SourceExchange<XModelAsset>, ISourceExchange<XModelAsset>
{
    public override XModelAsset Link(string path)
    {
        throw new NotImplementedException();
    }

    public override void Unlink(XModelAsset asset)
    {
        throw new NotImplementedException();
    }
}
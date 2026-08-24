using IW4.Assets.Assets.Menu;

namespace IW4.AssetExchange.SourceFormat.Menu;

public class MenuExchange : SourceExchange<MenuDefAsset>, ISourceExchange<MenuDefAsset>
{
    public override MenuDefAsset Link(string path)
    {
        throw new NotImplementedException();
    }

    public override void Unlink(MenuDefAsset asset)
    {
        throw new NotImplementedException();
    }
}
using System.Text.Json;
using System.Text.Json.Nodes;
using IW4.Formats.SourceFormat.Fx;
using IW4.Game.Assets.Fx;
using IW4.Game.Zone;
using IW4.Studio.Documents;

namespace IW4.Studio.Fx;

/// <summary>A detached version-one FX graph carried by an ordinary asset editing session.</summary>
public sealed class FxDraft
{
    internal FxDraft(string json, string assetName)
    {
        Json = json;
        AssetName = assetName;
    }

    public string Json { get; private set; }
    public string AssetName { get; }

    public void ReplaceJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        _ = new FxExchange().LinkJson(json, AssetName);
        Json = json;
    }

    internal FxDraft Clone() => new(Json, AssetName);
}

internal sealed class FxAdapter : AssetAuthoringAdapter<FxEffectDefAsset, FxDraft>
{
    private readonly FxExchange _exchange = new();

    public override XAssetType AssetType => XAssetType.Fx;
    public override FxDraft CreateDraft(FxEffectDefAsset definition) =>
        new(_exchange.ToJson(definition), definition.Name ??
            throw new InvalidDataException("The FX asset has no name."));
    public override FxDraft CloneDraft(FxDraft draft) => draft.Clone();
    public override FxEffectDefAsset CreateDefinition(FxDraft draft) =>
        _exchange.LinkJson(draft.Json, draft.AssetName);
    public override bool SemanticallyEquals(FxDraft left, FxDraft right) =>
        JsonNode.DeepEquals(JsonNode.Parse(left.Json), JsonNode.Parse(right.Json));

    public override IReadOnlyList<AssetValidationIssue> Validate(FxDraft draft)
    {
        try
        {
            _ = CreateDefinition(draft);
            return [];
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException or
            ArgumentException or OverflowException)
        {
            return [new AssetValidationIssue("Fx", exception.Message, AssetValidationSeverity.Error)];
        }
    }
}

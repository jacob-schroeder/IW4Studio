using IW4.Game.Assets.Fx;
using IW4.Game.Pointers;

namespace IW4.Loaders.Assets.Fx;

internal sealed record FxVisualPayload(
    FxElemDefVisuals? InlineVisual,
    XPointer<FxElemDefVisuals[]>? VisualArrayPointer,
    IReadOnlyList<FxElemDefVisuals> VisualArray,
    XPointer<FxElemMarkVisuals[]>? MarkVisualArrayPointer,
    IReadOnlyList<FxElemMarkVisuals> MarkVisualArray);

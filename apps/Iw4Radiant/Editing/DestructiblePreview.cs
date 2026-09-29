using System.Collections.Immutable;

namespace Iw4Radiant.Editing;

internal enum DestructiblePartState
{
    Intact,
    Damaged,
    Broken
}

internal sealed record DestructiblePreviewSettings(
    int Stage = 0,
    int PartIndex = 0,
    ImmutableDictionary<int, DestructiblePartState>? PartStates = null)
{
    internal DestructiblePartState PartState => StateFor(PartIndex);

    internal DestructiblePartState StateFor(int index) =>
        PartStates?.GetValueOrDefault(index) ?? DestructiblePartState.Intact;

    internal DestructiblePreviewSettings WithPartState(DestructiblePartState state) => this with
    {
        PartStates = state == DestructiblePartState.Intact
            ? PartStates?.Remove(PartIndex)
            : (PartStates ?? ImmutableDictionary<int, DestructiblePartState>.Empty).SetItem(PartIndex, state)
    };
}

internal sealed record DestructiblePreviewDefinition(
    IReadOnlyList<DestructiblePreviewStage> Stages,
    IReadOnlyList<DestructiblePreviewPart>? Parts = null,
    bool HasTires = false);

internal sealed record DestructiblePreviewStage(
    string Label,
    string? ModelName = null,
    string? FxName = null,
    string? FxTag = null,
    string? SoundName = null,
    string? TransitionSoundName = null,
    bool RepeatFx = false,
    bool WorldUpFx = false);

internal sealed record DestructiblePreviewPart(
    string Label,
    string IntactTag,
    string DamagedTag,
    string FxTag,
    string? FxName = null,
    string? SoundName = null);

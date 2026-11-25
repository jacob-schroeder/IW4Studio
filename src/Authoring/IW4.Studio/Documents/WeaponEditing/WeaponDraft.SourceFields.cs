using IW4.Game.Assets.Weapon;

namespace IW4.Studio.Documents;

public sealed partial class WeaponDraft
{
    public void SetDefinitionOverlayDescription(string? value) =>
        SetDefinitionValues(WeaponGraph.Copy(RequireDefinition(), value));

    public void SetDefinitionAimMovementTuningAimPadding(float value)
    {
        WeaponAimMovementTuningFields current = RequireDefinition().AimMovementTuning;
        var replacement = new WeaponAimMovementTuningFields
        {
            AutoAimRange = current.AutoAimRange,
            AimAssistRange = current.AimAssistRange,
            AimAssistRangeAds = current.AimAssistRangeAds,
            AimPadding = value,
            EnemyCrosshairRange = current.EnemyCrosshairRange,
            MoveSpeedScale = current.MoveSpeedScale,
            AdsMoveSpeedScale = current.AdsMoveSpeedScale,
            SprintDurationScale = current.SprintDurationScale,
            AdsZoomInFraction = current.AdsZoomInFraction,
            AdsZoomOutFraction = current.AdsZoomOutFraction
        };
        SetDefinitionValues(CopyWeaponDefAimMovementTuning(RequireDefinition(), replacement));
    }
}

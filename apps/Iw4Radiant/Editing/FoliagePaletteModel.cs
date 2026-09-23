using Iw4Radiant.Materials;

namespace Iw4Radiant.Editing;

internal sealed class FoliagePaletteModel(XModelSource model)
{
    private decimal? _weight = 1;

    internal XModelSource Model { get; } = model;
    public string Name => Model.Name;
    public decimal? Weight
    {
        get => _weight;
        set
        {
            decimal? weight = value is >= 0 and <= 100 ? value : null;
            if (_weight == weight) return;
            _weight = weight;
            Changed?.Invoke();
        }
    }

    internal event Action? Changed;
}

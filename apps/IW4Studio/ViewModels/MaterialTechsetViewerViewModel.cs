using System.Text;
using IW4.Assets.Assets.TechniqueSet;

namespace IW4.Studio.Desktop.ViewModels;

public sealed class MaterialTechsetViewerViewModel : ObservableObject
{
    private MaterialTechniqueSlotViewModel? _selectedTechnique;

    public MaterialTechsetViewerViewModel(MaterialTechniqueSetAsset techniqueSet)
    {
        ArgumentNullException.ThrowIfNull(techniqueSet);
        Name = techniqueSet.Name ?? "<unnamed techset>";
        WorldVertexFormat = FormatIdentifier(techniqueSet.WorldVertexFormat.ToString());
        Techniques = Array.AsReadOnly(
            techniqueSet.TechniqueSlots
                .Select(slot => new MaterialTechniqueSlotViewModel(slot))
                .ToArray());
        PopulatedTechniqueCount = Techniques.Count(slot => slot.IsPopulated);
        _selectedTechnique = Techniques.FirstOrDefault(slot => slot.IsPopulated)
            ?? Techniques.FirstOrDefault();
    }

    public string Name { get; }

    public string WorldVertexFormat { get; }

    public IReadOnlyList<MaterialTechniqueSlotViewModel> Techniques { get; }

    public int PopulatedTechniqueCount { get; }

    public string TechniqueCountText =>
        $"{PopulatedTechniqueCount} of {Techniques.Count} technique slots populated";

    public MaterialTechniqueSlotViewModel? SelectedTechnique
    {
        get => _selectedTechnique;
        set
        {
            if (!SetProperty(ref _selectedTechnique, value))
                return;

            OnPropertyChanged(nameof(SelectedTechniqueSummary));
        }
    }

    public string SelectedTechniqueSummary => SelectedTechnique is { } selected
        ? selected.IsPopulated
            ? $"{selected.PassCount} {(selected.PassCount == 1 ? "pass" : "passes")} · {selected.FlagsText}"
            : "This technique slot is not populated."
        : "No technique slot is selected.";

    internal static string FormatIdentifier(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var result = new StringBuilder(value.Length + 8);
        for (int index = 0; index < value.Length; index++)
        {
            char current = value[index];
            if (index > 0 &&
                char.IsUpper(current) &&
                (char.IsLower(value[index - 1]) ||
                 index + 1 < value.Length && char.IsLower(value[index + 1])))
            {
                result.Append(' ');
            }

            result.Append(current);
        }

        return result.ToString();
    }
}

public sealed class MaterialTechniqueSlotViewModel
{
    public MaterialTechniqueSlotViewModel(MaterialTechniqueSlot slot)
    {
        Slot = slot ?? throw new ArgumentNullException(nameof(slot));
        DisplayName = MaterialTechsetViewerViewModel.FormatIdentifier(slot.Type.ToString());
    }

    public MaterialTechniqueSlot Slot { get; }

    public MaterialTechniqueType Type => Slot.Type;

    public MaterialTechniqueAsset? Technique => Slot.Technique;

    public string DisplayName { get; }

    public bool IsPopulated => Technique is not null;

    public ushort PassCount => Technique?.PassCount ?? 0;

    public string FlagsText => Technique?.Flags is { } flags && flags != MaterialTechniqueFlags.None
        ? flags.ToString()
        : "No flags";

    public string Detail => IsPopulated
        ? $"{PassCount} {(PassCount == 1 ? "pass" : "passes")}"
        : "Not populated";
}

using IW4.Studio.Documents.MenuEditing;

namespace IW4.Studio.Desktop.ViewModels.Menu;

public sealed class MenuFileRegistrationViewModel
{
    internal MenuFileRegistrationViewModel(MenuFileRegistrationSnapshot value) =>
        Snapshot = value;

    internal MenuFileRegistrationSnapshot Snapshot { get; }
    public MenuRegistrationId Id => Snapshot.Id;
    public int Index => Snapshot.Index;
    public string? MenuName => Snapshot.Name;
    public string Name => Snapshot.Name ?? "Unresolved Menu";
    public bool IsEditableDefinition => Snapshot.IsEditableDefinition;
    public bool HasDefinition => Snapshot.Menu is not null;
    public string Status => IsEditableDefinition ? "INLINE" : "REFERENCE";
}

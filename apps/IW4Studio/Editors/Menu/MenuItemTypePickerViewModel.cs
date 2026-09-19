using IW4.Assets.Assets.Menu;
using IW4.Studio.Desktop.ViewModels;

namespace IW4.Studio.Desktop.Editors.Menu;

public sealed class MenuItemTypePickerViewModel : ObservableObject
{
    private ItemDefType _selectedItemType;

    public MenuItemTypePickerViewModel(ItemDefType currentItemType)
    {
        CurrentItemType = currentItemType;
        _selectedItemType = currentItemType;
        ItemTypes = Enum.IsDefined(currentItemType)
            ? Enum.GetValues<ItemDefType>()
            : Array.AsReadOnly(
                new[] { currentItemType }
                    .Concat(Enum.GetValues<ItemDefType>())
                    .ToArray());
    }

    public IReadOnlyList<ItemDefType> ItemTypes { get; }

    public ItemDefType CurrentItemType { get; }

    public ItemDefType SelectedItemType
    {
        get => _selectedItemType;
        set
        {
            if (!SetProperty(ref _selectedItemType, value))
                return;

            OnPropertyChanged(nameof(CanConfirm));
        }
    }

    public bool CanConfirm => SelectedItemType != CurrentItemType;
}

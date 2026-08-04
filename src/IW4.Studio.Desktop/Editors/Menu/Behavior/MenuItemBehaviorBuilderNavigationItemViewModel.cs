using IW4.Studio.Desktop.ViewModels;

namespace IW4.Studio.Desktop.Editors.Menu.Behavior;

/// <summary>Top-level pages in the ItemDef Behavior Builder.</summary>
public enum MenuItemBehaviorBuilderSection
{
    Events,
    Keys,
    Bindings
}

/// <summary>
/// A compact navigation projection. The session owns selection and updates
/// the summary when its corresponding local draft changes.
/// </summary>
public sealed class MenuItemBehaviorBuilderNavigationItemViewModel
    : ObservableObject
{
    private string _summary;
    private bool _isSelected;

    internal MenuItemBehaviorBuilderNavigationItemViewModel(
        MenuItemBehaviorBuilderSection section,
        string title,
        string description,
        string summary)
    {
        Section = section;
        Title = title;
        Description = description;
        _summary = summary;
    }

    public MenuItemBehaviorBuilderSection Section { get; }

    public string Title { get; }

    public string Description { get; }

    public string Summary
    {
        get => _summary;
        internal set => SetProperty(ref _summary, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        internal set => SetProperty(ref _isSelected, value);
    }
}

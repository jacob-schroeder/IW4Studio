using IW4.Studio.Desktop.ViewModels;

namespace IW4.Studio.Desktop.Workbench.Tools.ZoneDetails;

public sealed class ZoneScriptStringViewModel(int index, string? text, int assetUseCount = 0) : ObservableObject
{
    private int _index = index;
    private string? _text = text;

    public int Index
    {
        get => _index;
        internal set => SetProperty(ref _index, value);
    }

    public int AssetUseCount { get; } = assetUseCount;
    public bool CanEdit => AssetUseCount == 0;

    public string? Text
    {
        get => _text;
        set
        {
            if (CanEdit && SetProperty(ref _text, value))
            {
                OnPropertyChanged(nameof(Value));
                OnPropertyChanged(nameof(EditText));
            }
        }
    }

    public string EditText
    {
        get => Text ?? string.Empty;
        set
        {
            string input = value ?? string.Empty;
            if (!string.Equals(input, EditText, StringComparison.Ordinal))
                Text = input;
        }
    }

    public string Value => Text switch
    {
        null => "<null>",
        "" => "<empty>",
        "<null>" or "<empty>" => $"\"{Text}\"",
        _ => Text
    };
}

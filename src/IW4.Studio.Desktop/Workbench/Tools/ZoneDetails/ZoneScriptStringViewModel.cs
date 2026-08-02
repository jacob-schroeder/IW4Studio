namespace IW4.Studio.Desktop.Workbench.Tools.ZoneDetails;

public sealed class ZoneScriptStringViewModel
{
    public ZoneScriptStringViewModel(int index, string? value)
    {
        IndexText = index.ToString("N0");
        Value = value switch
        {
            null => "<null>",
            "" => "<empty>",
            _ => value
        };
    }

    public string IndexText { get; }

    public string Value { get; }
}

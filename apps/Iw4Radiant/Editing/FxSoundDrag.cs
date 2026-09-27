using Avalonia.Input;

namespace Iw4Radiant.Editing;

internal static class FxSoundDrag
{
    private static readonly DataFormat<string> Format =
        DataFormat.CreateStringApplicationFormat("com.iw4studio.iw4radiant.fx-sound");

    internal static DataTransfer Create(string name, bool isSound)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Contains('\n') || name.Contains('\r'))
            throw new ArgumentException("Asset names cannot contain line breaks.", nameof(name));
        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(Format, $"{(isSound ? 's' : 'f')}\n{name}"));
        return transfer;
    }

    internal static bool TryRead(IDataTransfer transfer, out string name, out bool isSound)
    {
        string? value = transfer.TryGetValue(Format);
        name = "";
        isSound = false;
        if (value is not { Length: > 2 } || value[1] != '\n' || value[0] is not ('s' or 'f')) return false;
        string candidate = value[2..];
        if (string.IsNullOrWhiteSpace(candidate) || candidate.Contains('\r') || candidate.Contains('\n')) return false;
        name = candidate;
        isSound = value[0] == 's';
        return true;
    }
}

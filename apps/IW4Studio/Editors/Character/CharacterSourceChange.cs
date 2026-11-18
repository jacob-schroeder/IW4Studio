namespace IW4.Studio.Desktop.Editors.Character;

internal sealed record CharacterSourceChange(int LineNumber, string Line, bool IsAddition)
{
    public string Marker => IsAddition ? "+" : "−";

    internal static IReadOnlyList<CharacterSourceChange> Between(string original, string draft)
    {
        if (original == draft)
            return [];

        string[] before = original.ReplaceLineEndings("\n").Split('\n');
        string[] after = draft.ReplaceLineEndings("\n").Split('\n');
        int first = 0;
        int oldEnd = before.Length;
        int newEnd = after.Length;
        while (first < oldEnd && first < newEnd && before[first] == after[first])
            first++;
        while (first < oldEnd && first < newEnd && before[oldEnd - 1] == after[newEnd - 1])
        {
            oldEnd--;
            newEnd--;
        }

        var changes = new List<CharacterSourceChange>();
        int oldCount = oldEnd - first;
        int newCount = newEnd - first;
        if (oldCount == 0 || newCount == 0 || (long)oldCount * newCount > 262_144)
        {
            for (int index = first; index < oldEnd; index++)
                changes.Add(new CharacterSourceChange(index + 1, before[index], false));
            for (int index = first; index < newEnd; index++)
                changes.Add(new CharacterSourceChange(index + 1, after[index], true));
            return changes;
        }

        var commonLines = new int[oldCount + 1, newCount + 1];
        for (int oldLine = oldCount - 1; oldLine >= 0; oldLine--)
        {
            for (int newLine = newCount - 1; newLine >= 0; newLine--)
            {
                commonLines[oldLine, newLine] = before[first + oldLine] == after[first + newLine]
                    ? 1 + commonLines[oldLine + 1, newLine + 1]
                    : Math.Max(commonLines[oldLine + 1, newLine], commonLines[oldLine, newLine + 1]);
            }
        }

        int oldIndex = 0;
        int newIndex = 0;
        while (oldIndex < oldCount || newIndex < newCount)
        {
            if (oldIndex < oldCount && newIndex < newCount && before[first + oldIndex] == after[first + newIndex])
            {
                oldIndex++;
                newIndex++;
            }
            else if (oldIndex < oldCount &&
                     (newIndex == newCount || commonLines[oldIndex + 1, newIndex] >= commonLines[oldIndex, newIndex + 1]))
            {
                changes.Add(new CharacterSourceChange(first + oldIndex + 1, before[first + oldIndex], false));
                oldIndex++;
            }
            else
            {
                changes.Add(new CharacterSourceChange(first + newIndex + 1, after[first + newIndex], true));
                newIndex++;
            }
        }

        return changes;
    }
}

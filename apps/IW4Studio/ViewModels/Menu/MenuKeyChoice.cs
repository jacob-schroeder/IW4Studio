namespace IW4.Studio.Desktop.ViewModels.Menu;

public enum MenuKeyGroup
{
    Controller,
    Keyboard,
    Character,
    Mouse,
    Auxiliary,
    Custom
}

/// <summary>PS3 input names for the signed key stored in a menu or item key handler.</summary>
public sealed record MenuKeyChoice(int Code, string Name, MenuKeyGroup Group)
{
    public string Label => $"{Name} · {Code}";
    public override string ToString() => Label;

    public static IReadOnlyList<MenuKeyChoice> Options { get; } = BuildOptions();

    public static MenuKeyChoice? Find(int code) =>
        Options.FirstOrDefault(choice => choice.Code == code);

    public static string Describe(int code) => Find(code)?.Label ?? $"Custom key · {code}";

    private static IReadOnlyList<MenuKeyChoice> BuildOptions()
    {
        var choices = new List<MenuKeyChoice>();
        void Add(MenuKeyGroup group, params (int Code, string Name)[] keys)
        {
            foreach (var (code, name) in keys)
                choices.Add(new MenuKeyChoice(code, name, group));
        }

        // default_mp.elf: keyNames 0x008a90f0, display names 0x008a9490.
        // 0x002e0da8 maps authored 1 and 2 through the PS3 system button assignment.
        // Physical pad event codes follow the mask table at 0x008a3f94.
        Add(MenuKeyGroup.Controller,
            (1, "Confirm"), (2, "Cancel / Back"), (3, "Square"), (4, "Triangle"),
            (20, "D-pad up"), (21, "D-pad down"), (22, "D-pad left"), (23, "D-pad right"),
            (5, "L1"), (6, "R1"), (18, "L2"), (19, "R2"),
            (16, "L3 · Left stick press"), (17, "R3 · Right stick press"),
            (14, "Start"), (15, "Select"),
            (28, "Analog up"), (29, "Analog down"), (30, "Analog left"), (31, "Analog right"));

        Add(MenuKeyGroup.Keyboard,
            (13, "Enter"), (27, "Escape"), (32, "Space"), (9, "Tab"), (127, "Backspace"),
            (154, "Up arrow"), (155, "Down arrow"), (156, "Left arrow"), (157, "Right arrow"),
            (158, "Alt"), (159, "Ctrl"), (160, "Shift"), (150, "Command"), (151, "Caps Lock"),
            (161, "Insert"), (162, "Delete"), (163, "Page Down"), (164, "Page Up"),
            (165, "Home"), (166, "End"), (153, "Pause"));
        for (int number = 1; number <= 12; number++)
            choices.Add(new MenuKeyChoice(166 + number, $"F{number}", MenuKeyGroup.Keyboard));
        Add(MenuKeyGroup.Keyboard,
            (182, "Keypad Home"), (183, "Keypad Up"), (184, "Keypad Page Up"),
            (185, "Keypad Left"), (186, "Keypad 5"), (187, "Keypad Right"),
            (188, "Keypad End"), (189, "Keypad Down"), (190, "Keypad Page Down"),
            (191, "Keypad Enter"), (192, "Keypad Insert"), (193, "Keypad Delete"),
            (194, "Keypad /"), (195, "Keypad −"), (196, "Keypad +"), (197, "Keypad Num Lock"),
            (198, "Keypad *"), (199, "Keypad ="));

        // The single-character path at 0x000a5b50 preserves the character byte and its case.
        for (char character = '!'; character <= '~'; character++)
        {
            string name = character switch
            {
                >= 'A' and <= 'Z' => $"{character} (uppercase)",
                >= 'a' and <= 'z' => $"{character} (lowercase)",
                ';' => "; (semicolon)",
                _ => character.ToString()
            };
            choices.Add(new MenuKeyChoice(character, name, MenuKeyGroup.Character));
        }

        // These legacy display bytes have their own engine codes starting at 128.
        const string extendedCharacters = "µ¿ßàáäåæçèéìñòóöøùúü";
        for (int index = 0; index < extendedCharacters.Length; index++)
            choices.Add(new MenuKeyChoice(128 + index, extendedCharacters[index].ToString(), MenuKeyGroup.Character));

        for (int number = 1; number <= 5; number++)
            choices.Add(new MenuKeyChoice(199 + number, $"Mouse button {number}", MenuKeyGroup.Mouse));
        Add(MenuKeyGroup.Mouse, (206, "Wheel up"), (205, "Wheel down"));

        for (int number = 1; number <= 16; number++)
            choices.Add(new MenuKeyChoice(206 + number, $"AUX{number}", MenuKeyGroup.Auxiliary));
        return choices.AsReadOnly();
    }
}

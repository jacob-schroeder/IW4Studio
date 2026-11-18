using System.Globalization;
using IW4.Game.Assets.Menu;

namespace IW4.Studio.Documents.MenuEditing.Debugging;

internal static class MenuDvarPredicate
{
    // Item_EnableShowViaDvar, PS3 default_mp.elf 0x00254D68. A match
    // selects the requested bit; exhaustion selects its inverse.
    public static MenuEvaluation<bool> Allows(DebugItemDefinition item,
        MenuDebugScenario scenario, ItemDvarFlags requested)
    {
        if (string.IsNullOrEmpty(item.DvarTest) || string.IsNullOrEmpty(item.EnableDvar))
            return MenuEvaluation<bool>.Known(true);

        var dependency = new MenuDebugDependency(MenuDebugDependencyKind.Dvar,
            item.DvarTest, MenuDebugValueKind.String);
        // Dvar_GetVariantString's missing-name branch (0x276F48)
        // returns the empty C string at 0x578D88 in default_mp.elf.
        bool supplied = scenario.Dvars.TryGetValue(item.DvarTest, out MenuDebugValue value);
        string current = string.Empty;
        if (supplied)
        {
            current = value.Kind == MenuDebugValueKind.Float && value.TryGetFloat(out float number)
                ? number.ToString("G6", CultureInfo.InvariantCulture)
                : value.AsString();
        }
        int end = item.EnableDvar.IndexOfAny(['\r', '\n']);
        var parsed = MenuDebugScriptParser.Parse(end < 0 ? item.EnableDvar : item.EnableDvar[..end]);
        bool positive = item.DvarFlags.HasFlag(requested);
        if (parsed.Failure is { } failure)
            return MenuEvaluation<bool>.Error(true, [dependency], [new(MenuEvaluationStatus.Error, failure)]);

        var missing = new List<MenuEvaluationTraceEntry>();
        bool matched = false;
        foreach (string token in parsed.Commands.SelectMany(command => command.Tokens))
        {
            if (token == ";")
                continue;
            string? candidate = token;
            if (token.StartsWith('@'))
            {
                string key = token[1..];
                candidate = scenario.LocalizationResolver?.Invoke(key);
                if (candidate is null)
                {
                    missing.Add(new(MenuEvaluationStatus.Unknown, $"Localization '{token}' is unavailable.",
                        Dependency: new(MenuDebugDependencyKind.Localization, key, MenuDebugValueKind.String)));
                    continue;
                }
            }
            if (string.Equals(current, candidate, StringComparison.OrdinalIgnoreCase))
            {
                matched = true;
                missing.Clear();
                break;
            }
        }

        bool result = matched == positive;
        if (!supplied)
            missing.Add(new(MenuEvaluationStatus.Unknown,
                $"Dvar '{item.DvarTest}' is unset in this scenario. Using the engine's empty-string fallback.",
                Dependency: dependency));
        return missing.Count == 0
            ? MenuEvaluation<bool>.Known(result, [dependency])
            : MenuEvaluation<bool>.Unknown(result,
                missing.Select(entry => entry.Dependency).OfType<MenuDebugDependency>().Append(dependency).Distinct(),
                missing);
    }
}

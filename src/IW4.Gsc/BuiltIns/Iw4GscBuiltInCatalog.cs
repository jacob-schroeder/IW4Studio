using System.Collections.ObjectModel;
using System.Text;
using IW4.Gsc.Syntax;

namespace IW4.Gsc.BuiltIns;

public enum Iw4GscBuiltInKind
{
    Function,
    Method,
    VmIntrinsic
}

public enum Iw4GscBuiltInReceiver
{
    None,
    Entity,
    Player,
    ScriptEntity,
    HudElement,
    Helicopter
}

/// <summary>
/// One engine-owned GSC callable or VM intrinsic recovered from the IW4
/// multiplayer executable. Native registration tables do not carry parameter
/// metadata, so the exposed signature deliberately uses an ellipsis.
/// </summary>
public sealed class Iw4GscBuiltInDefinition
{
    internal Iw4GscBuiltInDefinition(
        string name,
        string nativeHandler,
        Iw4GscBuiltInKind kind,
        Iw4GscBuiltInReceiver receiver,
        bool developerOnly,
        GscTextSpan referenceSpan)
    {
        Name = name;
        NativeHandler = nativeHandler;
        Kind = kind;
        Receiver = receiver;
        DeveloperOnly = developerOnly;
        ReferenceSpan = referenceSpan;
    }

    public string Name { get; }

    public string NativeHandler { get; }

    public Iw4GscBuiltInKind Kind { get; }

    public Iw4GscBuiltInReceiver Receiver { get; }

    public bool DeveloperOnly { get; }

    public GscTextSpan ReferenceSpan { get; }

    public string DisplaySignature => Kind switch
    {
        Iw4GscBuiltInKind.Function => $"{Name}(…)",
        Iw4GscBuiltInKind.Method => $"<{ReceiverLabel}> {Name}(…)",
        Iw4GscBuiltInKind.VmIntrinsic => Name,
        _ => throw new ArgumentOutOfRangeException()
    };

    public string ReceiverLabel => Receiver switch
    {
        Iw4GscBuiltInReceiver.None => string.Empty,
        Iw4GscBuiltInReceiver.Entity => "entity",
        Iw4GscBuiltInReceiver.Player => "player",
        Iw4GscBuiltInReceiver.ScriptEntity => "script entity",
        Iw4GscBuiltInReceiver.HudElement => "HUD element",
        Iw4GscBuiltInReceiver.Helicopter => "helicopter",
        _ => throw new ArgumentOutOfRangeException()
    };

    public string Description
    {
        get
        {
            string category = Kind switch
            {
                Iw4GscBuiltInKind.Function => "Engine global function",
                Iw4GscBuiltInKind.Method =>
                    $"Engine {ReceiverLabel} method",
                Iw4GscBuiltInKind.VmIntrinsic => "Compiler/VM intrinsic",
                _ => throw new ArgumentOutOfRangeException()
            };
            string development = DeveloperOnly ? " · developer-only" : string.Empty;
            string parameters = Kind == Iw4GscBuiltInKind.VmIntrinsic
                ? string.Empty
                : " · parameter metadata unavailable";
            return $"{category} · native handler {NativeHandler}{development}{parameters}";
        }
    }
}

/// <summary>
/// Generated, read-only navigation target for the executable-owned language
/// surface. It is kept separate from host-supplied RawFile documents.
/// </summary>
public sealed class Iw4GscBuiltInReferenceDocument
{
    private readonly IReadOnlyList<Iw4GscBuiltInDefinition> _definitions;
    private readonly IReadOnlyDictionary<string, Iw4GscBuiltInDefinition[]> _byName;

    internal Iw4GscBuiltInReferenceDocument(
        string text,
        IEnumerable<Iw4GscBuiltInDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(definitions);
        Text = text;
        Iw4GscBuiltInDefinition[] copiedDefinitions = definitions.ToArray();
        _definitions = Array.AsReadOnly(copiedDefinitions);
        _byName = new ReadOnlyDictionary<string, Iw4GscBuiltInDefinition[]>(
            copiedDefinitions
                .GroupBy(definition => definition.Name, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.ToArray(),
                    StringComparer.Ordinal));
    }

    public string Title => "IW4 multiplayer engine built-ins";

    public string Text { get; }

    public IReadOnlyList<Iw4GscBuiltInDefinition> Definitions => _definitions;

    public IReadOnlyList<Iw4GscBuiltInDefinition> FindCallables(string namePrefix)
    {
        ArgumentNullException.ThrowIfNull(namePrefix);
        string canonicalPrefix = namePrefix.ToLowerInvariant();
        return Array.AsReadOnly(_definitions
            .Where(definition =>
                definition.Kind != Iw4GscBuiltInKind.VmIntrinsic &&
                definition.Name.StartsWith(
                    canonicalPrefix,
                    StringComparison.Ordinal))
            .OrderBy(definition => definition.Name, StringComparer.Ordinal)
            .ThenBy(definition => definition.Kind)
            .ThenBy(definition => definition.Receiver)
            .ToArray());
    }

    public IReadOnlyList<Iw4GscBuiltInDefinition> FindCallablesByName(
        string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        string canonicalName = name.ToLowerInvariant();
        return _byName.TryGetValue(
                   canonicalName,
                   out Iw4GscBuiltInDefinition[]? definitions)
            ? Array.AsReadOnly(definitions
                .Where(definition =>
                    definition.Kind != Iw4GscBuiltInKind.VmIntrinsic)
                .ToArray())
            : [];
    }
}

/// <summary>
/// Built-in catalog for the IW4 multiplayer GSC runtime. Xbox 360 symbols
/// identify the native resolver tables; matching PS3 behavior is authoritative
/// if a platform conflict is discovered.
/// </summary>
public static partial class Iw4GscBuiltInCatalog
{
    static Iw4GscBuiltInCatalog() =>
        Multiplayer = BuildMultiplayerDocument();

    public static Iw4GscBuiltInReferenceDocument Multiplayer { get; }

    private static Iw4GscBuiltInReferenceDocument BuildMultiplayerDocument()
    {
        IReadOnlyList<RegistrationGroup> groups = GetRegistrationGroups();
        var text = new StringBuilder(
            "// IW4 multiplayer engine built-ins\n" +
            "// Generated from the native Scr_GetFunction/Scr_GetMethod resolver tables.\n" +
            "// This is read-only reference text, not compilable GSC.\n" +
            "// The registry exposes names, handlers, and developer flags; it does not expose parameters.\n" +
            "// 204 unique global functions (205 registrations; weaponfiretime is duplicated), 228 methods, 6 VM intrinsics.\n\n");
        var definitions = new List<Iw4GscBuiltInDefinition>(438);

        foreach (RegistrationGroup group in groups)
        {
            text.Append("// ").Append(group.Title).Append(" (")
                .Append(group.Registrations.Count).AppendLine(")");
            foreach (Registration registration in group.Registrations)
            {
                string prefix = group.Kind == Iw4GscBuiltInKind.Method
                    ? $"<{ReceiverLabel(group.Receiver)}> "
                    : string.Empty;
                text.Append(prefix);
                int nameStart = text.Length;
                text.Append(registration.Name);
                if (group.Kind != Iw4GscBuiltInKind.VmIntrinsic)
                    text.Append("(…)");
                text.Append("  // ").Append(registration.NativeHandler);
                if (registration.DeveloperOnly)
                    text.Append(" | developer-only");
                text.AppendLine();

                definitions.Add(new Iw4GscBuiltInDefinition(
                    registration.Name,
                    registration.NativeHandler,
                    group.Kind,
                    group.Receiver,
                    registration.DeveloperOnly,
                    new GscTextSpan(nameStart, registration.Name.Length)));
            }
            text.AppendLine();
        }

        return new Iw4GscBuiltInReferenceDocument(text.ToString(), definitions);
    }

    private static string ReceiverLabel(Iw4GscBuiltInReceiver receiver) =>
        receiver switch
        {
            Iw4GscBuiltInReceiver.Entity => "entity",
            Iw4GscBuiltInReceiver.Player => "player",
            Iw4GscBuiltInReceiver.ScriptEntity => "script entity",
            Iw4GscBuiltInReceiver.HudElement => "HUD element",
            Iw4GscBuiltInReceiver.Helicopter => "helicopter",
            _ => throw new ArgumentOutOfRangeException(nameof(receiver))
        };

    private static partial IReadOnlyList<RegistrationGroup>
        GetRegistrationGroups();

    private sealed record Registration(
        string Name,
        string NativeHandler,
        bool DeveloperOnly);

    private sealed record RegistrationGroup(
        string Title,
        Iw4GscBuiltInKind Kind,
        Iw4GscBuiltInReceiver Receiver,
        IReadOnlyList<Registration> Registrations);
}

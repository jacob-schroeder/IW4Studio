using System.Numerics;
using IW4.Studio.Desktop.Editors;
using IW4.Studio.Desktop.Editors.AddonMapEnts;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.ViewModels;

internal sealed class AddonMapEntsEditorViewModel : ObservableObject, IAssetEditorStagingState
{
    private readonly AssetEditorSession _session;
    private readonly List<(AddonMapEntsDraft Draft, int Index)> _undo = [];
    private AddonMapEntsDraft _baseline;
    private AddonMapEntsDraft _working;
    private string? _message;
    private Action? _confirm;
    private string? _template;
    private int _copyIndex = -1;

    internal AddonMapEntsEditorViewModel(AssetEditorSession session)
    {
        _session = session;
        _baseline = session.OpenDraft<AddonMapEntsDraft>();
        _working = _baseline.Clone();
        Rebuild();
        SelectedIndex = Objects.FirstOrDefault(o => o.Kind == "start")?.Index
            ?? Objects.FirstOrDefault(o => o.Scene != null)?.Index ?? (Objects.Count == 0 ? -1 : 0);
    }

    internal event Action? Changed;
    public bool HasUnappliedChanges => !_working.Matches(_baseline);
    internal string Heading => _working.Name ?? _session.Entry.OriginalName ?? "Mission scene";
    internal bool IsEditable => _session.CanEdit && _working.ParseError is null;
    internal string Status => _working.ParseError ?? _message ?? (!IsEditable ? "Read only" :
        HasUnappliedChanges ? "Unapplied changes" : _session.HasUnsavedChanges ? "Applied to workspace" : "Up to date");
    internal bool CanUndo => IsEditable && _undo.Count > 0;
    internal bool CanApply => IsEditable && HasUnappliedChanges;
    internal bool NeedsConfirmation => _confirm is not null;
    internal string? ConfirmationAction { get; private set; }
    internal string? ConfirmationText { get; private set; }
    internal IReadOnlyList<MissionObject> Objects { get; private set; } = [];
    internal IReadOnlyList<AddonEntitySceneObject> SceneObjects { get; private set; } = [];
    internal int SelectedIndex { get; private set; }
    internal MissionObject? Selected => (uint)SelectedIndex < (uint)Objects.Count ? Objects[SelectedIndex] : null;
    internal AddonEntitySceneMode Mode { get; private set; }
    internal AddonEntitySceneObject? Ghost { get; private set; }
    internal bool CanDuplicate => IsEditable && Selected is { Scene: not null, Kind: not "trigger", ModelIndex: < 0 };
    internal string Hint => Mode switch
    {
        AddonEntitySceneMode.Place => $"Click to place {Ghost?.Label.ToLowerInvariant()}.",
        AddonEntitySceneMode.Relink => "Choose a destination in the scene or object list.",
        _ when Objects.Count == 0 => "Add a marker or player start to begin.",
        _ when Selected?.Kind == "world" => "Map settings apply to the whole mission.",
        _ when !IsEditable => "Select an object to inspect it. Scroll to zoom.",
        _ when Selected?.Scene?.Box != null => "Drag the box to move it. Drag a corner to resize.",
        _ when Selected?.Scene?.Radius != null => "Drag the marker to move it. Drag its ring handle to change the radius.",
        _ when Selected?.Scene?.YawDegrees != null => "Drag the object to move it. In Top view, drag the round handle to turn it.",
        _ => "Drag an object to move it. Drag empty space to pan. Scroll to zoom."
    };

    private void Rebuild()
    {
        var triggers = _working.Trigger;
        var cache = new Dictionary<int, IReadOnlyList<AddonEntitySceneOutline>>();
        IReadOnlyList<AddonEntitySceneOutline> Outlines(int model)
        {
            if (!cache.TryGetValue(model, out var shapes))
                cache[model] = shapes = AddonEntitySceneGeometry.Outlines(triggers, model);
            return shapes;
        }
        Objects = _working.Rows.Select((row, i) => new MissionObject(i, row, triggers, IsEditable, _working.CanResizeTrigger, Outlines)).ToArray();
        SceneObjects = Objects.Select(o => o.Scene).OfType<AddonEntitySceneObject>().ToArray();
    }

    internal void Select(int index)
    {
        if ((uint)index >= (uint)Objects.Count) return;
        if (Mode == AddonEntitySceneMode.Relink) { ChooseTarget(index); return; }
        SelectedIndex = index;
        Cancel();
    }

    private void ClearMode()
    {
        Mode = AddonEntitySceneMode.Normal;
        Ghost = null;
        _template = null;
        _copyIndex = -1;
        _confirm = null;
        ConfirmationText = null;
        ConfirmationAction = null;
    }

    internal void Cancel()
    {
        ClearMode();
        _message = null;
        Changed?.Invoke();
    }

    internal void StartPlace(string template)
    {
        if (!IsEditable || template is not ("marker" or "start" or "copy") || template == "copy" && !CanDuplicate) return;
        ClearMode();
        _template = template;
        _copyIndex = template == "copy" ? SelectedIndex : -1;
        Ghost = template == "copy" ? Selected!.Scene! with { Label = Selected.Title + " copy", TargetName = null }
            : new(-1, template == "marker" ? "Script marker" : "Player start", template, Selected?.Scene?.Origin ?? Vector3.Zero,
                template == "marker" ? 32 : null, template == "start" ? 0 : null);
        Mode = AddonEntitySceneMode.Place;
        _message = null;
        Changed?.Invoke();
    }

    internal void Place(Vector3 position)
    {
        if (!IsEditable || Mode != AddonEntitySceneMode.Place) return;
        var template = _template;
        int copy = _copyIndex;
        Change(draft =>
        {
            int index;
            if (template == "copy")
            {
                index = draft.DuplicateEntity(copy);
                for (int p = draft.Rows[index].Properties.Count - 1; p >= 0; p--)
                    if (KeyEquals(draft.Rows[index].Properties[p].Key, "targetname")) draft.RemoveProperty(index, p);
                Put(draft, index, "origin", MissionObject.Vector(position));
            }
            else
            {
                var properties = new List<KeyValuePair<string,string>>
                {
                    new("classname", template == "marker" ? "script_struct" : "info_player_start_so"),
                    new("origin", MissionObject.Vector(position)), new("script_specialops", "1")
                };
                properties.Add(template == "marker" ? new("radius", "32") : new("angles", "0 0 0"));
                index = draft.CreateEntity(properties);
            }
            SelectedIndex = index;
        });
    }

    internal void CommitGesture(AddonEntitySceneEdit edit)
    {
        if ((uint)edit.Index >= (uint)Objects.Count || Objects[edit.Index].Scene is not { } old || !old.CanEdit) return;
        var obj = Objects[edit.Index];
        Change(draft =>
        {
            if (edit.Origin != old.Origin) Put(draft, edit.Index, "origin", MissionObject.Vector(edit.Origin));
            if (edit.Radius is float r && r != old.Radius) Put(draft, edit.Index, "radius", MissionObject.Number(r));
            if (edit.YawDegrees is float yaw && yaw != old.YawDegrees && MissionObject.ParseVector(obj.Get("angles")) is { } angles)
            {
                angles.Y = yaw;
                Put(draft, edit.Index, "angles", MissionObject.Vector(angles));
            }
            if (edit.BoxHalf is { } half && old.Box is { } box && half != box.Half)
                draft.ResizeTrigger(obj.ModelIndex, MissionObject.ToNative(box.Mid), MissionObject.ToNative(half));
        });
    }

    internal IEnumerable<MissionObject> Destinations(MissionObject source) => Matching("targetname", source.Get("target"));
    internal IEnumerable<MissionObject> Sources(MissionObject dest) => Matching("target", dest.Get("targetname"));
    private IEnumerable<MissionObject> Matching(string key, string? value) => string.IsNullOrEmpty(value) ? [] :
        Objects.Where(o => string.Equals(o.Get(key), value, StringComparison.Ordinal));

    internal void StartRelink()
    {
        if (!IsEditable || Selected is null) return;
        ClearMode();
        Mode = AddonEntitySceneMode.Relink;
        _message = null;
        Changed?.Invoke();
    }

    internal void ChooseTarget(int targetIndex)
    {
        if (!IsEditable || Mode != AddonEntitySceneMode.Relink || Selected is not { } source ||
            (uint)targetIndex >= (uint)Objects.Count) return;
        var target = Objects[targetIndex];
        string? name = target.Get("targetname");
        bool needsName = string.IsNullOrEmpty(name);
        if (needsName)
        {
            var names = Objects.SelectMany(o => new[] { o.Get("targetname"), o.Get("target") }).ToHashSet(StringComparer.Ordinal);
            int suffix = targetIndex + 1;
            do { name = $"mission_object_{suffix++}"; } while (names.Contains(name));
        }
        string destination = name!;
        void Connect() => Change(draft =>
        {
            if (needsName) Put(draft, targetIndex, "targetname", destination);
            Put(draft, source.Index, "target", destination);
        });
        ConfirmGroup(destination, Connect);
    }

    private void ConfirmGroup(string name, Action connect)
    {
        int count = Matching("targetname", name).Count();
        if (count < 2) { connect(); return; }
        ClearMode();
        _confirm = connect;
        ConfirmationAction = $"Link all {count}";
        ConfirmationText = $"“{name}” is shared by {count} objects. Link all {count}?";
        Changed?.Invoke();
    }

    internal void Confirm() => _confirm?.Invoke();

    internal void SetField(string key, string value, int? ordinal = null)
    {
        if (!IsEditable || Selected is not { } o) return;
        if ((ordinal.HasValue ? o.Properties[ordinal.Value].Value : o.Get(key)) == value) return;
        if ((KeyEquals(key, "origin") || KeyEquals(key, "angles")) && MissionObject.ParseVector(value) is null)
        { Message("Enter three finite numbers separated by spaces."); return; }
        if (KeyEquals(key, "radius") && (MissionObject.ParseFloat(value) is not float radius || radius < 0))
        { Message("Enter a radius of zero or more."); return; }
        void Set() => Change(d =>
        {
            if (ordinal is int p) d.SetProperty(o.Index, p, o.Properties[p].Key, value);
            else Put(d, o.Index, key, value);
        });
        if (KeyEquals(key, "target")) ConfirmGroup(value, Set); else Set();
    }

    internal void AddField(string key)
    {
        key = key.Trim();
        if (!IsEditable || Selected is not { } o) return;
        if (key.Length == 0 || o.Properties.Any(p => KeyEquals(p.Key, key))) { Message("Enter a new property name."); return; }
        Change(d => d.AddProperty(o.Index, key, ""));
    }

    internal void RemoveField(int ordinal)
    {
        if (Selected is { } o) Change(d => d.RemoveProperty(o.Index, ordinal));
    }

    internal void DeleteObject()
    {
        if (!IsEditable || Selected is not { } o || o.Kind == "world") return;
        int incoming = Sources(o).Count();
        ClearMode();
        ConfirmationAction = "Remove object";
        ConfirmationText = incoming == 0 ? $"Remove {o.Title} from this mission?" :
            $"Remove {o.Title}? {incoming} incoming connections use its target name.";
        _confirm = () => Change(d =>
        {
            d.DeleteEntity(o.Index);
            SelectedIndex = Math.Min(o.Index, d.Rows.Count - 1);
        });
        Changed?.Invoke();
    }

    private static bool KeyEquals(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static void Put(AddonMapEntsDraft draft, int index, string key, string value)
    {
        var properties = draft.Rows[index].Properties;
        for (var i = 0; i < properties.Count; i++)
            if (KeyEquals(properties[i].Key, key)) { draft.SetProperty(index, i, properties[i].Key, value); return; }
        draft.AddProperty(index, key, value);
    }

    internal void Message(string message) { _message = message; Changed?.Invoke(); }

    private void Change(Action<AddonMapEntsDraft> change)
    {
        if (!IsEditable) return;
        var candidate = _working.Clone();
        var previousIndex = SelectedIndex;
        ClearMode();
        try
        {
            change(candidate);
            _message = null;
            if (!_working.Matches(candidate))
            {
                _undo.Add((_working, previousIndex));
                if (_undo.Count > 32) _undo.RemoveAt(0);
                _working = candidate;
                Rebuild();
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or InvalidDataException or OverflowException)
        { SelectedIndex = previousIndex; _message = ex.Message; }
        Notify();
    }

    internal void Undo()
    {
        if (!CanUndo) return;
        ClearMode();
        (_working, SelectedIndex) = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        Rebuild();
        _message = null;
        RefreshWorkspace();
    }

    internal void Apply()
    {
        if (!CanApply) return;
        ClearMode();
        try
        {
            var current = _session.ApplyAndRead<AddonMapEntsDraft>(d => d.ApplyEdits(_baseline, _working));
            if (_session.Validation.HasErrors) _message = string.Join(" ", _session.Validation.Issues.Select(i => i.Message));
            else Load(current);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or InvalidDataException)
        { _message = ex.Message; }
        Notify();
    }

    internal void RefreshWorkspace()
    {
        if (!HasUnappliedChanges)
        {
            var current = _session.OpenDraft<AddonMapEntsDraft>();
            if (!_baseline.Matches(current)) Load(current);
        }
        Notify();
    }

    private void Load(AddonMapEntsDraft current)
    {
        ClearMode();
        _baseline = current;
        _working = current.Clone();
        _undo.Clear();
        _message = null;
        Rebuild();
        SelectedIndex = Objects.Count == 0 ? -1 : Math.Clamp(SelectedIndex, 0, Objects.Count - 1);
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(HasUnappliedChanges));
        Changed?.Invoke();
    }
}

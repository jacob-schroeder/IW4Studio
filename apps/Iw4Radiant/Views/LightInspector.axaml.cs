using System.Globalization;
using System.Numerics;
using Avalonia.Automation;
using Avalonia.Controls;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;
using Material.Icons;

namespace Iw4Radiant.Views;

public partial class LightInspector : UserControl
{
    private MapEntity? _displayedEntity;
    private bool _applying;
    private bool _updatingSweep;
    private EditorSession? _session;

    public LightInspector()
    {
        InitializeComponent();
        DetachedFromVisualTree += (_, _) => _session?.StopLightSweepPreview();
    }

    internal bool HasSourceError => IsVisible && !LightFields.IsVisible;

    internal void InitializeActions(EditorSession session, EditorDialogs dialogs, Action finishGestures,
        Func<bool> authoredPreviewAvailable, Action enablePreviewLighting)
    {
        _session = session;
        session.LightSweepPlaybackChanged += (_, _) => UpdateSweepAvailability();
        session.LightInfluencePreviewChanged += entity =>
        {
            if (ReferenceEquals(session.Selection.Active, entity) &&
                MapLightProperties.TryRead(entity, out MapLightProperties properties, out _))
            {
                SweepAngleBox.Text = properties.SweepAngle > 0 ? Number(properties.SweepAngle) : "";
                InnerFovBox.Text = Number(properties.InnerFov);
                OuterFovBox.Text = properties.OuterFov is { } outer ? Number(outer) : "";
            }
        };
        ApplyLightButton.Click += async (_, _) => { await ApplyAsync(session, dialogs, finishGestures, createTarget: false); };
        CreateTargetButton.Click += async (_, _) => { await ApplyAsync(session, dialogs, finishGestures, createTarget: true); };
        RevertLightButton.Click += (_, _) =>
        {
            if (dialogs.BlocksInput) return;
            _displayedEntity = null;
            RefreshSelection(session);
        };
        PrimarySpotBox.Click += (_, _) =>
        {
            if (PrimarySpotBox.IsChecked != true) session.StopLightSweepPreview();
            UpdateSweepAvailability();
        };
        PrimaryOmniBox.Click += (_, _) => UpdateSweepAvailability();
        SweepBox.Click += async (_, _) =>
        {
            if (_updatingSweep) return;
            bool enableSweep = SweepBox.IsChecked == true;
            if (!authoredPreviewAvailable())
            {
                RestoreSweepCheckbox(session);
                return;
            }
            if (enableSweep && string.IsNullOrWhiteSpace(SweepAngleBox.Text))
                SweepAngleBox.Text = "50";
            if (!await ApplyAsync(session, dialogs, finishGestures, createTarget: false,
                    requireMovingPreview: enableSweep))
            {
                RestoreSweepCheckbox(session);
                return;
            }
            if (enableSweep && session.Selection.Active is MapEntity { ClassName: "light" } entity)
            {
                enablePreviewLighting();
                if (session.LightSweepPreviewAvailable &&
                    MapLight.TryCreate(entity, session.Scene.ResolveTargets(entity), out MapLight light, out _) &&
                    light.IsMoving)
                    session.StartLightSweepPreview(entity);
            }
            UpdateSweepAvailability();
        };
        SweepPreviewButton.Click += (_, _) =>
        {
            if (dialogs.BlocksInput) return;
            if (session.LightSweepPreviewEntity is not null) session.StopLightSweepPreview();
            else if (session.LightSweepPreviewAvailable &&
                     session.Selection.Active is MapEntity { ClassName: "light" } entity &&
                     MapLight.TryCreate(entity, session.Scene.ResolveTargets(entity), out MapLight light, out _) &&
                     light.IsMoving)
                session.StartLightSweepPreview(entity);
            UpdateSweepAvailability();
        };
    }

    internal void RefreshSelection(EditorSession session)
    {
        if (_applying) return;
        if (session.Selection.Active is not MapEntity { ClassName: "light" } entity)
        {
            session.StopLightSweepPreview();
            IsVisible = false;
            _displayedEntity = null;
            return;
        }
        IsVisible = true;
        if (session.LightSweepPreviewEntity is not null && !ReferenceEquals(session.LightSweepPreviewEntity, entity))
            session.StopLightSweepPreview();
        UpdateSweepAvailability();
        if (ReferenceEquals(entity, _displayedEntity) && IsKeyboardFocusWithin) return;
        _displayedEntity = entity;
        bool valid = MapLightProperties.TryRead(entity, out MapLightProperties properties, out string? error);
        LightError.Text = valid ? "" : $"{error} Correct the source values in Entity properties.";
        LightError.IsVisible = !valid;
        LightFields.IsVisible = valid;
        if (!valid) return;
        ColorPicker.SelectedColor = properties.Color;
        RadiusBox.Text = Number(properties.Radius);
        IntensityBox.Text = Number(properties.Intensity);
        DefinitionBox.Text = properties.Definition;
        TargetBox.Text = properties.Target;
        OuterFovBox.Text = properties.OuterFov is { } outer ? Number(outer) : "";
        InnerFovBox.Text = Number(properties.InnerFov);
        ExponentBox.Text = Number(properties.Exponent);
        PrimaryOmniBox.IsChecked = (properties.SpawnFlags & MapLightDefaults.PrimaryOmni) != 0;
        PrimarySpotBox.IsChecked = (properties.SpawnFlags & MapLightDefaults.PrimarySpot) != 0;
        DynamicShadowsBox.IsChecked = properties.DynamicShadows;
        _updatingSweep = true;
        SweepBox.IsChecked = properties.SweepAngle > 0;
        SweepAngleBox.Text = properties.SweepAngle > 0 ? Number(properties.SweepAngle) : "";
        SweepSecondsBox.Text = Number(properties.SweepSeconds);
        _updatingSweep = false;
        UpdateSweepAvailability();
    }

    private async Task<bool> ApplyAsync(EditorSession session, EditorDialogs dialogs, Action finishGestures,
        bool createTarget, bool requireMovingPreview = false)
    {
        if (dialogs.BlocksInput) return false;
        try
        {
            _applying = true;
            finishGestures();
            if (session.Selection.Active is not MapEntity { ClassName: "light" } entity) return false;
            MapLightProperties properties = ReadControls(entity);
            MapEntity? target = null;
            if (createTarget)
            {
                Vector3 origin = EditorSession.EntityOrigin(entity);
                Vector3 targetOrigin = origin + new Vector3(128, 0, 0);
                if (!float.IsFinite(targetOrigin.X) || targetOrigin == origin)
                    throw new ArgumentException("The light origin is too large to create a target 128 units along X.");
                var names = session.Document.Entities.Select(item => item.Properties.GetValueOrDefault("targetname"))
                    .ToHashSet(StringComparer.Ordinal);
                int suffix = 1;
                while (names.Contains($"light_target_{suffix}")) suffix++;
                string name = $"light_target_{suffix}";
                target = new MapEntity();
                target.Properties.Add("classname", "info_null");
                target.Properties.Add("targetname", name);
                target.Properties.Add("origin", $"{Number(targetOrigin.X)} {Number(targetOrigin.Y)} {Number(targetOrigin.Z)}");
                properties = properties with { Target = name };
            }
            MapEntity proposed = entity.Clone();
            properties.ApplyTo(proposed);
            if (requireMovingPreview &&
                (!MapLight.TryCreate(proposed, session.Scene.ResolveTargets(proposed), out MapLight moving, out string? error) ||
                 !moving.IsMoving))
                throw new ArgumentException(error ?? "Sweep preview needs a light with positive radius, intensity, and color.");
            if (target is null && proposed.Properties.Count == entity.Properties.Count &&
                proposed.Properties.All(pair => entity.Properties.GetValueOrDefault(pair.Key) == pair.Value)) return true;
            session.StopLightSweepPreview();
            session.Edit(() =>
            {
                properties.ApplyTo(entity);
                if (target is not null) session.Document.Entities.Add(target);
            });
            _displayedEntity = null;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            TargetExpander.IsExpanded = DefinitionExpander.IsExpanded = true;
            await dialogs.MessageAsync("Light properties", exception.Message);
            return false;
        }
        finally
        {
            _applying = false;
            RefreshSelection(session);
        }
    }

    private MapLightProperties ReadControls(MapEntity entity)
    {
        if (!MapLightProperties.TryRead(entity, out MapLightProperties previous, out string? error))
            throw new ArgumentException(error);
        int flags = previous.SpawnFlags & ~(MapLightDefaults.PrimaryOmni | MapLightDefaults.PrimarySpot);
        if (PrimaryOmniBox.IsChecked == true) flags |= MapLightDefaults.PrimaryOmni;
        if (PrimarySpotBox.IsChecked == true) flags |= MapLightDefaults.PrimarySpot;
        return new MapLightProperties
        {
            Definition = DefinitionBox.Text ?? "", Radius = ReadNumber(RadiusBox, "radius"),
            Intensity = ReadNumber(IntensityBox, "intensity"), Color = ColorPicker.SelectedColor,
            Target = TargetBox.Text ?? "", OuterFov = string.IsNullOrWhiteSpace(OuterFovBox.Text)
                ? null : ReadNumber(OuterFovBox, "outer FOV"), InnerFov = ReadNumber(InnerFovBox, "inner FOV"),
            Exponent = ReadNumber(ExponentBox, "exponent"), SpawnFlags = flags,
            SweepAngle = PrimarySpotBox.IsChecked == true && SweepBox.IsChecked == true
                ? ReadNumber(SweepAngleBox, "sweep arc") : 0,
            SweepPlane = previous.SweepPlane,
            SweepSeconds = PrimarySpotBox.IsChecked == true && SweepBox.IsChecked == true
                ? ReadNumber(SweepSecondsBox, "sweep time") : previous.SweepSeconds,
            DynamicShadows = (flags is MapLightDefaults.PrimaryOmni or MapLightDefaults.PrimarySpot) &&
                DynamicShadowsBox.IsChecked == true
        };
    }

    private static float ReadNumber(TextBox input, string name)
    {
        if (!float.TryParse(input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || !float.IsFinite(value))
            throw new ArgumentException($"Enter a finite number for light {name}.");
        return value;
    }

    private static string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);

    private void RestoreSweepCheckbox(EditorSession session)
    {
        if (session.Selection.Active is not MapEntity entity ||
            !MapLightProperties.TryRead(entity, out MapLightProperties properties, out _)) return;
        _updatingSweep = true;
        SweepBox.IsChecked = properties.SweepAngle > 0;
        _updatingSweep = false;
        UpdateSweepAvailability();
    }

    private void UpdateSweepAvailability()
    {
        EditorSession? session = _session;
        bool primarySpot = PrimarySpotBox.IsChecked == true;
        bool primaryOmni = PrimaryOmniBox.IsChecked == true;
        DynamicShadowsBox.IsEnabled = primaryOmni != primarySpot;
        ToolTip.SetTip(DynamicShadowsBox, primaryOmni && !primarySpot
            ? "Primary omni shadows project downward within this profile's fixed 120-degree cone and fade toward the edge. The light still shines in all directions. The game limits active shadow maps. Requires a rebuild; preview shadows remain baked."
            : "Allow this Primary spot to cast shadows from moving objects in game. The game limits active shadow maps. Requires a rebuild; preview shadows remain baked.");
        SweepBox.IsEnabled = primarySpot;
        SweepFields.IsVisible = primarySpot && SweepBox.IsChecked == true;
        SweepPreviewButton.IsVisible = SweepFields.IsVisible;
        bool playing = session?.LightSweepPreviewEntity is not null;
        SweepPreviewIcon.Kind = playing ? MaterialIconKind.Stop : MaterialIconKind.Play;
        string previewAction = playing ? "Stop sweep preview" : "Play sweep preview";
        ToolTip.SetTip(SweepPreviewButton, previewAction);
        AutomationProperties.SetName(SweepPreviewButton, previewAction);
        if (!playing)
            SweepPreviewButton.IsEnabled = session?.LightSweepPreviewAvailable == true &&
                session.Selection.Active is MapEntity { ClassName: "light" } entity &&
                MapLight.TryCreate(entity, session.Scene.ResolveTargets(entity), out MapLight light, out _) &&
                light.IsMoving;
        else SweepPreviewButton.IsEnabled = true;
    }
}

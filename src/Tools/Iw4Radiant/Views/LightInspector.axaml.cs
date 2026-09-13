using System.Globalization;
using System.Numerics;
using Avalonia.Controls;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Views;

public partial class LightInspector : UserControl
{
    private MapEntity? _displayedEntity;
    private bool _applying;

    public LightInspector() => InitializeComponent();

    internal void InitializeActions(EditorSession session, EditorDialogs dialogs, Action finishGestures)
    {
        ApplyLightButton.Click += async (_, _) => await ApplyAsync(session, dialogs, finishGestures, createTarget: false);
        CreateTargetButton.Click += async (_, _) => await ApplyAsync(session, dialogs, finishGestures, createTarget: true);
    }

    internal void RefreshSelection(EditorSession session)
    {
        if (_applying) return;
        if (session.Selection.Active is not MapEntity { ClassName: "light" } entity)
        {
            IsVisible = false;
            _displayedEntity = null;
            return;
        }
        IsVisible = true;
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
    }

    private async Task ApplyAsync(EditorSession session, EditorDialogs dialogs, Action finishGestures, bool createTarget)
    {
        if (dialogs.BlocksInput) return;
        try
        {
            _applying = true;
            finishGestures();
            if (session.Selection.Active is not MapEntity { ClassName: "light" } entity) return;
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
            if (target is null && proposed.Properties.Count == entity.Properties.Count &&
                proposed.Properties.All(pair => entity.Properties.GetValueOrDefault(pair.Key) == pair.Value)) return;
            session.Edit(() =>
            {
                properties.ApplyTo(entity);
                if (target is not null) session.Document.Entities.Add(target);
            });
            _displayedEntity = null;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            await dialogs.MessageAsync("Light properties", exception.Message);
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
            Exponent = ReadNumber(ExponentBox, "exponent"), SpawnFlags = flags
        };
    }

    private static float ReadNumber(TextBox input, string name)
    {
        if (!float.TryParse(input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || !float.IsFinite(value))
            throw new ArgumentException($"Enter a finite number for light {name}.");
        return value;
    }

    private static string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);
}

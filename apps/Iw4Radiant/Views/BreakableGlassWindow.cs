using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Iw4Radiant.MapSource;
using Iw4Radiant.Materials;

namespace Iw4Radiant.Views;

internal sealed class BreakableGlassWindow : Window
{
    internal BreakableGlassWindow(int brushCount, string[] intactMaterials,
        MaterialPickerOption[] materials, string[] physicsPresets,
        string? shatteredMaterial, string? physPreset)
    {
        Title = "Breakable glass";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        const string standardMaterial = "m/com_glass_clear_shattered";
        const string standardPhysics = "glass_chunk";
        string? brokenName = shatteredMaterial ??
            (materials.Any(option => option.Name == standardMaterial) ? standardMaterial : null);
        string? physicsName = physPreset ??
            (physicsPresets.Contains(standardPhysics, StringComparer.Ordinal) ? standardPhysics : null);
        bool closed = false;
        int brokenRevision = 0;
        Bitmap? intactBitmap = null, brokenBitmap = null;

        var intactName = new TextBlock
        {
            Text = intactMaterials.Length == 1 ? intactMaterials[0] : "Multiple face materials",
            TextWrapping = TextWrapping.Wrap
        };
        var intactImage = new Image { Width = 100, Height = 100, Stretch = Stretch.Uniform };
        var intactPlaceholder = new TextBlock { Text = "Preview unavailable", TextWrapping = TextWrapping.Wrap };
        var brokenText = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var brokenImage = new Image { Width = 100, Height = 100, Stretch = Stretch.Uniform };
        var brokenPlaceholder = new TextBlock { Text = "Choose a material", TextWrapping = TextWrapping.Wrap };
        var choose = new Button { Content = "Choose material", MinWidth = 132, IsEnabled = materials.Length > 0 };
        var advanced = new CheckBox { Content = "Advanced physics",
            IsChecked = physicsName is not null && physicsName != standardPhysics };
        var physics = new ComboBox { MinWidth = 260 };
        foreach (string name in physicsPresets)
            physics.Items.Add(new ComboBoxItem
            {
                Tag = name,
                Content = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = name == standardPhysics ? "Standard glass" : FriendlyName(name) },
                        new TextBlock { Text = name, FontSize = 11, Foreground = Brushes.Gray }
                    }
                }
            });
        physics.SelectedItem = physics.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => (string?)item.Tag == physicsName);
        var physicsInfo = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var catalogInfo = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var apply = new Button { Content = "Make breakable", MinWidth = 130 };
        var cancel = new Button { Content = "Cancel", MinWidth = 76 };

        void Refresh()
        {
            bool materialReady = brokenName is not null && materials.Any(option => option.Name == brokenName);
            bool physicsReady = physicsName is not null && physicsPresets.Contains(physicsName, StringComparer.Ordinal);
            brokenText.Text = brokenName is null ? "No broken appearance selected" :
                materialReady ? brokenName : $"{brokenName} · missing from the loaded material library";
            physicsInfo.Text = physicsName is null ?
                "Standard glass physics is unavailable. Load a library with its preset, or choose one in Advanced physics." :
                physicsReady ? physicsName == standardPhysics ? "Standard glass" : FriendlyName(physicsName) :
                "The selected physics preset is unavailable. Choose an available preset in Advanced physics.";
            catalogInfo.Text = materials.Length == 0 ?
                "Load raw assets in the Materials tab to choose a broken appearance." :
                physicsPresets.Length == 0 ? "No valid native physics presets were found in the selected library or bundled assets." : "";
            physics.IsVisible = advanced.IsChecked == true;
            apply.IsEnabled = materialReady && physicsReady;
        }

        choose.Click += async (_, _) =>
        {
            string? selected = await MaterialPickerDialog.ShowAsync(this, "Choose broken appearance", materials,
                brokenName, brokenName is null || brokenName.Contains("glass", StringComparison.OrdinalIgnoreCase) ? "glass" : null);
            if (selected is null || closed) return;
            brokenName = selected;
            Refresh();
            _ = LoadPreviewAsync(materials.First(option => option.Name == selected).Material,
                brokenImage, brokenPlaceholder, isBroken: true);
        };
        physics.SelectionChanged += (_, _) =>
        {
            if (physics.SelectedItem is ComboBoxItem { Tag: string name }) physicsName = name;
            Refresh();
        };
        advanced.IsCheckedChanged += (_, _) => Refresh();
        apply.Click += (_, _) =>
        {
            if (apply.IsEnabled && brokenName is not null && physicsName is not null)
            {
                BrushGlass.ValidateNames(brokenName, physicsName);
                Close((brokenName, physicsName));
            }
        };
        cancel.Click += (_, _) => Close(null);
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(null); } };
        Closed += (_, _) =>
        {
            closed = true;
            brokenRevision++;
            intactImage.Source = brokenImage.Source = null;
            intactBitmap?.Dispose();
            brokenBitmap?.Dispose();
        };

        Content = new StackPanel
        {
            Margin = new Thickness(20), Spacing = 12,
            Children =
            {
                new TextBlock { Text = brushCount == 1 ? "Make this pane breakable." : $"Make these {brushCount} panes breakable.",
                    TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = "Current appearance", FontWeight = FontWeight.SemiBold },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12,
                    Children = { intactImage, new StackPanel { Width = 340, Children = { intactName, intactPlaceholder } } } },
                new TextBlock { Text = "Broken appearance", FontWeight = FontWeight.SemiBold },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12,
                    Children = { brokenImage, new StackPanel { Width = 340, Spacing = 8,
                        Children = { brokenText, brokenPlaceholder, choose } } } },
                new TextBlock { Text = "Shard physics", FontWeight = FontWeight.SemiBold },
                physicsInfo, advanced, physics, catalogInfo,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8, Children = { cancel, apply } }
            }
        };
        Refresh();
        if (intactMaterials.Length == 1)
            _ = LoadPreviewAsync(materials.FirstOrDefault(option => option.Name == intactMaterials[0])?.Material,
                intactImage, intactPlaceholder, isBroken: false);
        if (brokenName is not null)
            _ = LoadPreviewAsync(materials.FirstOrDefault(option => option.Name == brokenName)?.Material,
                brokenImage, brokenPlaceholder, isBroken: true);

        async Task LoadPreviewAsync(MaterialSource? source, Image image, TextBlock placeholder, bool isBroken)
        {
            int revision = isBroken ? ++brokenRevision : 0;
            image.Source = null;
            if (isBroken) { brokenBitmap?.Dispose(); brokenBitmap = null; }
            if (source is null) { placeholder.Text = "Preview unavailable in loaded library"; return; }
            placeholder.Text = "Loading preview…";
            Bitmap? bitmap = null;
            try
            {
                bitmap = await Task.Run(() => MaterialImages.Load(source, 100));
                if (closed || isBroken && revision != brokenRevision) return;
                image.Source = bitmap;
                placeholder.Text = "";
                if (isBroken) brokenBitmap = bitmap;
                else intactBitmap = bitmap;
                bitmap = null;
            }
            catch (Exception)
            {
                if (!closed && (!isBroken || revision == brokenRevision))
                    placeholder.Text = "Preview unavailable";
            }
            finally { bitmap?.Dispose(); }
        }
    }

    private static string FriendlyName(string name) =>
        System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.Replace('_', ' '));
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Iw4Radiant.Materials;

namespace Iw4Radiant.Views;

internal sealed record MaterialPickerOption(string Name, MaterialSource? Material, int? SurfaceCount);

internal static class MaterialPickerDialog
{
    private const int PageSize = 96;

    internal static async Task<string?> ShowAsync(Window owner, string title,
        IReadOnlyList<MaterialPickerOption> options, string? initialName)
    {
        MaterialPickerOption[] all = options.OrderBy(option => option.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        MaterialPickerOption[] matches = all;
        MaterialPickerOption? selected = all.FirstOrDefault(option =>
            option.Name.Equals(initialName, StringComparison.Ordinal));
        int page = selected is null ? 0 : Array.IndexOf(all, selected) / PageSize;
        bool closed = false;
        CancellationTokenSource? pageCancellation = null;
        CancellationTokenSource? detailCancellation = null;
        var pageImages = new List<Bitmap>();
        Bitmap? detailBitmap = null;
        var cards = new List<(MaterialPickerOption Option, Button Button,
            Image Image, TextBlock Placeholder)>();

        var search = new TextBox { PlaceholderText = "Search materials by name", HorizontalAlignment = HorizontalAlignment.Stretch };
        var count = new TextBlock { Foreground = Brush("#AEB6C2"), VerticalAlignment = VerticalAlignment.Center };
        var gallery = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(5) };
        var scroll = new ScrollViewer
        {
            Content = gallery,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        var previous = new Button { Content = "Previous", MinWidth = 86 };
        var next = new Button { Content = "Next", MinWidth = 70 };
        var pageInfo = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("#AEB6C2") };
        var detailImage = new Image { Stretch = Stretch.Uniform };
        var detailPlaceholder = new TextBlock
        {
            Text = "Select a material",
            Foreground = Brush("#AEB6C2"),
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        var detailName = new TextBlock { FontSize = 17, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
        var detailUsage = new TextBlock { Foreground = Brush("#AEB6C2"), TextWrapping = TextWrapping.Wrap };
        var cancel = new Button { Content = "Cancel", MinWidth = 86 };
        var choose = new Button { Content = "Choose material", MinWidth = 132, IsEnabled = selected is not null };
        var dialog = new Window
        {
            Title = title, Width = 940, Height = 570, MinWidth = 660, MinHeight = 550,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var header = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"), ColumnSpacing = 14 };
        header.Children.Add(search);
        Grid.SetColumn(count, 1);
        header.Children.Add(count);

        var galleryPanel = new Grid { RowDefinitions = RowDefinitions.Parse("*,Auto"), RowSpacing = 8 };
        galleryPanel.Children.Add(new Border
        {
            Background = Brush("#222427"), BorderBrush = Brush("#45484E"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Child = scroll
        });
        var pager = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 12, Children = { previous, pageInfo, next }
        };
        Grid.SetRow(pager, 1);
        galleryPanel.Children.Add(pager);

        var detailPreview = new Grid { Height = 278 };
        detailPreview.Children.Add(detailImage);
        detailPreview.Children.Add(detailPlaceholder);
        var detail = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = "PREVIEW", FontSize = 11, FontWeight = FontWeight.SemiBold,
                    Foreground = Brush("#AEB6C2") },
                new Border { Background = Brush("#303237"), CornerRadius = new CornerRadius(6),
                    BorderBrush = Brush("#45484E"), BorderThickness = new Thickness(1),
                    Padding = new Thickness(8), Child = detailPreview },
                detailName, detailUsage
            }
        };
        var detailPanel = new Border
        {
            Background = Brush("#292C31"), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16), Child = detail
        };
        var body = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,300"), ColumnSpacing = 14 };
        body.Children.Add(galleryPanel);
        Grid.SetColumn(detailPanel, 1);
        body.Children.Add(detailPanel);

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8, Children = { cancel, choose }
        };
        var root = new Grid
        {
            RowDefinitions = RowDefinitions.Parse("Auto,*,Auto"), RowSpacing = 14,
            Margin = new Thickness(18)
        };
        root.Children.Add(header);
        Grid.SetRow(body, 1);
        root.Children.Add(body);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        dialog.Content = root;

        search.TextChanged += (_, _) =>
        {
            string query = search.Text?.Trim() ?? "";
            matches = query.Length == 0 ? all : all.Where(option =>
                option.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (selected is not null && !matches.Contains(selected)) Select(null);
            page = selected is null ? 0 : Array.IndexOf(matches, selected) / PageSize;
            RenderPage();
        };
        previous.Click += (_, _) => { if (page > 0) { page--; RenderPage(); } };
        next.Click += (_, _) => { if ((page + 1) * PageSize < matches.Length) { page++; RenderPage(); } };
        cancel.Click += (_, _) => dialog.Close(null);
        choose.Click += (_, _) => { if (selected is not null) dialog.Close(selected.Name); };
        dialog.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { dialog.Close(null); e.Handled = true; }
            else if (e.Key == Key.Enter && selected is not null)
            {
                dialog.Close(selected.Name);
                e.Handled = true;
            }
        };
        dialog.Closed += (_, _) =>
        {
            closed = true;
            pageCancellation?.Cancel();
            detailCancellation?.Cancel();
            detailImage.Source = null;
            detailBitmap?.Dispose();
            gallery.Children.Clear();
            ReleasePageImages();
        };

        UpdateDetail();
        RenderPage();
        return await dialog.ShowDialog<string?>(owner);

        void Select(MaterialPickerOption? option)
        {
            if (ReferenceEquals(selected, option)) return;
            selected = option;
            choose.IsEnabled = option is not null;
            foreach (var card in cards)
            {
                bool isSelected = ReferenceEquals(card.Option, option);
                card.Button.BorderBrush = Brush(isSelected ? "#8BB8E8" : "#45484E");
                card.Button.BorderThickness = new Thickness(isSelected ? 2 : 1);
            }
            UpdateDetail();
        }

        void UpdateDetail()
        {
            detailCancellation?.Cancel();
            detailImage.Source = null;
            detailBitmap?.Dispose();
            detailBitmap = null;
            detailName.Text = selected?.Name ?? "Choose a material";
            detailUsage.Text = selected?.SurfaceCount is { } surfaces
                ? $"Used on {surfaces:N0} {(surfaces == 1 ? "surface" : "surfaces")}."
                : "";
            detailPlaceholder.Text = selected is null ? "Select a material"
                : selected.Material is null ? "Preview unavailable · source material is not loaded"
                : "Loading preview…";
            detailPlaceholder.IsVisible = true;
            if (selected?.Material is not { } material || closed) return;
            var cancellation = new CancellationTokenSource();
            detailCancellation = cancellation;
            _ = LoadDetailAsync(material, cancellation);
        }

        async Task LoadDetailAsync(MaterialSource material, CancellationTokenSource cancellation)
        {
            Bitmap? bitmap = null;
            try
            {
                bitmap = await Task.Run(() => MaterialImages.Load(material, 384), cancellation.Token);
                if (closed || cancellation.IsCancellationRequested) return;
                detailBitmap = bitmap;
                detailImage.Source = bitmap;
                bitmap = null;
                detailPlaceholder.IsVisible = false;
            }
            catch (OperationCanceledException) { }
            catch (Exception exception) when (FileOperationErrors.IsExpected(exception))
            {
                if (!closed && !cancellation.IsCancellationRequested)
                    detailPlaceholder.Text = $"Preview unavailable · {exception.Message}";
            }
            finally
            {
                bitmap?.Dispose();
                if (ReferenceEquals(detailCancellation, cancellation)) detailCancellation = null;
                cancellation.Dispose();
            }
        }

        void RenderPage()
        {
            pageCancellation?.Cancel();
            gallery.Children.Clear();
            cards.Clear();
            ReleasePageImages();
            scroll.Offset = new Vector(0, 0);
            int first = page * PageSize;
            int shown = Math.Min(PageSize, matches.Length - first);
            count.Text = $"{matches.Length:N0} {(matches.Length == 1 ? "material" : "materials")}";
            pageInfo.Text = matches.Length == 0 ? "No matches"
                : $"{first + 1:N0}–{first + shown:N0} of {matches.Length:N0}";
            previous.IsEnabled = page > 0;
            next.IsEnabled = first + shown < matches.Length;
            for (int index = first; index < first + shown; index++)
            {
                MaterialPickerOption option = matches[index];
                var image = new Image { Width = 92, Height = 92, Stretch = Stretch.Uniform };
                var placeholder = new TextBlock
                {
                    Text = option.Material is null ? "Unavailable" : "Loading…",
                    FontSize = 11, Foreground = Brush("#AEB6C2"),
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var picture = new Grid { Width = 96, Height = 96, Background = Brush("#222427") };
                picture.Children.Add(image);
                picture.Children.Add(placeholder);
                var label = new TextBlock
                {
                    Text = option.Name, FontSize = 11, TextAlignment = TextAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxLines = 2, TextWrapping = TextWrapping.Wrap
                };
                ToolTip.SetTip(label, option.Name);
                var content = new StackPanel { Spacing = 5, Children = { picture, label } };
                var card = new Button
                {
                    Content = content, Width = 122, Height = 139, Padding = new Thickness(8),
                    Margin = new Thickness(4), VerticalContentAlignment = VerticalAlignment.Top,
                    Background = Brush("#303237"),
                    BorderBrush = Brush(ReferenceEquals(option, selected) ? "#8BB8E8" : "#45484E"),
                    BorderThickness = new Thickness(ReferenceEquals(option, selected) ? 2 : 1),
                    CornerRadius = new CornerRadius(6)
                };
                card.Click += (_, _) => Select(option);
                gallery.Children.Add(card);
                cards.Add((option, card, image, placeholder));
            }
            var cancellation = new CancellationTokenSource();
            pageCancellation = cancellation;
            _ = LoadPageAsync(cards.Select(entry =>
                (entry.Option, entry.Image, entry.Placeholder)).ToArray(), cancellation);
        }

        async Task LoadPageAsync(
            (MaterialPickerOption Option, Image Image, TextBlock Placeholder)[] visible,
            CancellationTokenSource cancellation)
        {
            try
            {
                await Task.Delay(120, cancellation.Token);
                foreach (var item in visible)
                {
                    if (item.Option.Material is not { } material) continue;
                    Bitmap? bitmap = null;
                    try
                    {
                        bitmap = await Task.Run(() => MaterialImages.Load(material, 96), cancellation.Token);
                        if (closed || cancellation.IsCancellationRequested) return;
                        item.Image.Source = bitmap;
                        item.Placeholder.IsVisible = false;
                        pageImages.Add(bitmap);
                        bitmap = null;
                    }
                    catch (Exception exception) when (FileOperationErrors.IsExpected(exception))
                    {
                        if (!closed && !cancellation.IsCancellationRequested)
                            item.Placeholder.Text = "Unavailable";
                    }
                    finally { bitmap?.Dispose(); }
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                if (ReferenceEquals(pageCancellation, cancellation)) pageCancellation = null;
                cancellation.Dispose();
            }
        }

        void ReleasePageImages()
        {
            foreach (Bitmap bitmap in pageImages) bitmap.Dispose();
            pageImages.Clear();
        }
    }

    private static SolidColorBrush Brush(string color) => new(Color.Parse(color));
}

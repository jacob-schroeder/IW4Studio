using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace IW4.Studio.Desktop.Editors.AssetReferences;

internal sealed record AssetReferencePickerResult(
    string Name,
    bool IsMissing);

public sealed partial class AssetReferencePickerWindow : Window
{
    public AssetReferencePickerWindow()
    {
        InitializeComponent();
        Icon = AppIcon.Create();
        Opened += (_, _) => SearchTextBox.Focus();
        Closed += (_, _) =>
            (DataContext as AssetReferencePickerViewModel)?.Dispose();
    }

    internal AssetReferencePickerWindow(AssetReferencePickerViewModel viewModel)
        : this()
    {
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }

    private void SelectButton_Click(
        object? sender,
        RoutedEventArgs e) =>
        AcceptSelection();

    private void CandidateList_DoubleTapped(
        object? sender,
        TappedEventArgs e) =>
        AcceptSelection();

    private void CancelButton_Click(
        object? sender,
        RoutedEventArgs e) =>
        Close((AssetReferencePickerResult?)null);

    private void CandidateRow_AttachedToVisualTree(
        object? sender,
        Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control
            {
                DataContext: AssetReferenceCandidateViewModel candidate
            })
        {
            candidate.EnsureMaterialPreview();
        }
    }

    private void AcceptSelection()
    {
        if (DataContext is not AssetReferencePickerViewModel
            {
                SelectedCandidate: { } selected
            })
        {
            return;
        }

        Close(new AssetReferencePickerResult(
            selected.Name,
            IsMissing: !selected.IsResolved));
    }
}

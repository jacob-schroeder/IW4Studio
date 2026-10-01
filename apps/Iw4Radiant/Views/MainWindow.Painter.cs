using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Iw4Radiant.Editing;
using Material.Icons;

namespace Iw4Radiant.Views;

public partial class MainWindow
{
    private readonly DispatcherTimer _painterHoldTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private bool _mistModeSelected, _painterHoldOpened;

    private void InitializePainterTools()
    {
        _painterHoldTimer.Tick += (_, _) =>
        {
            _painterHoldTimer.Stop();
            if (_dialogs.BlocksInput) return;
            _painterHoldOpened = true;
            FlyoutBase.ShowAttachedFlyout(PainterButton);
        };
        PainterButton.AddHandler(PointerPressedEvent, (_, e) =>
        {
            _painterHoldTimer.Stop();
            _painterHoldOpened = false;
            if (!_dialogs.BlocksInput && e.GetCurrentPoint(PainterButton).Properties.IsLeftButtonPressed)
                _painterHoldTimer.Start();
        }, RoutingStrategies.Tunnel);
        PainterButton.AddHandler(PointerReleasedEvent, (_, _) => _painterHoldTimer.Stop(), RoutingStrategies.Tunnel);
        PainterButton.PointerExited += (_, _) => _painterHoldTimer.Stop();
        PainterButton.PointerCaptureLost += (_, _) => _painterHoldTimer.Stop();
        Deactivated += (_, _) => _painterHoldTimer.Stop();
        Closed += (_, _) => _painterHoldTimer.Stop();
        Inspector.MistPainter.Changed += () =>
        {
            Workspace.Camera.FinishGesture();
            bool needsPreset = Workspace.Camera.MistPaintingEnabled && Workspace.Camera.MistErasing &&
                !Inspector.MistPainter.IsErasing;
            ApplyMistBrushSettings();
            if (needsPreset)
            {
                StopMistPainting();
                StartMistPainting();
            }
            RefreshPainterToolState();
        };
        Inspector.MistPainter.LibraryRequested += () => Workspace.ShowFxSounds(isSound: false);
    }

    private void PainterTools_Click(object? sender, RoutedEventArgs e)
    {
        if (!_dialogs.BlocksInput) FlyoutBase.ShowAttachedFlyout(PainterButton);
    }

    private void ModelPainter_Click(object? sender, RoutedEventArgs e) => SelectPainter(mist: false);
    private void MistPainter_Click(object? sender, RoutedEventArgs e) => SelectPainter(mist: true);

    private void SelectPainter(bool mist)
    {
        if (_dialogs.BlocksInput) return;
        FlyoutBase.GetAttachedFlyout(PainterButton)?.Hide();
        FinishGestures();
        StopPainters();
        _mistModeSelected = mist;
        Inspector.SetPainterMode(mist);
        ShowInspectorSection(Inspector.ShowPainter);
        if (mist) StartMistPainting();
        else Inspector.Painter.StartPainting(Workspace.Models.SelectedModel);
        RefreshPainterToolState();
    }

    private void StartMistPainting()
    {
        if (_previewBspPath is not null)
        {
            SetStatus("Return to the source map to paint mist.");
            return;
        }
        // Erasing is a map edit and does not require the effect's asset library.
        if (!Inspector.MistPainter.IsErasing)
        {
            string? library = Workspace.FxBrowser.SourceDirectory;
            if (library is null)
            {
                Workspace.ShowFxSounds(isSound: false);
                SetStatus("Choose an FX library, then activate the mist painter.");
                return;
            }
            try
            {
                MistFxPreset.Ensure(library);
                Workspace.FxBrowser.IncludeFx(MistPainting.AssetName);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                ArgumentException or JsonException or NotSupportedException or OverflowException)
            {
                SetStatus($"Cannot prepare mist: {exception.Message}");
                return;
            }
        }
        FinishGestures();
        Inspector.Painter.StopPainting();
        _activatingFoliage = true;
        try { SetTool(EditorTool.Select); }
        finally { _activatingFoliage = false; }
        ApplyMistBrushSettings();
        Workspace.Camera.MistPaintingEnabled = true;
        _session.Scene.MistPaintingActive = true;
        RefreshMistGuides();
        RefreshPainterToolState();
        ShowInspectorSection(Inspector.ShowPainter);
        SetStatus(Inspector.MistPainter.IsErasing
            ? "Erase painted mist · drag over map surfaces · Esc cancels a stroke"
            : "Paint mist · click for one patch, drag for spaced patches · Esc cancels a stroke");
    }

    private void ApplyMistBrushSettings()
    {
        Workspace.Camera.MistSpacing = Inspector.MistPainter.BrushSpacing;
        Workspace.Camera.MistRadius = Inspector.MistPainter.EraseRadius;
        Workspace.Camera.MistErasing = Inspector.MistPainter.IsErasing;
    }

    private void StopMistPainting()
    {
        if (!Workspace.Camera.MistPaintingEnabled && !_session.Scene.MistPaintingActive) return;
        Workspace.Camera.MistPaintingEnabled = false;
        _session.Scene.MistPaintingActive = false;
        RefreshMistGuides();
    }

    private void StopPainters()
    {
        StopMistPainting();
        Inspector.Painter.StopPainting();
        RefreshPainterToolState();
    }

    private void RefreshMistGuides()
    {
        Workspace.Camera.RefreshScene();
        foreach (var view in Workspace.GridViews) view.InvalidateVisual();
    }

    private void RefreshPainterToolState()
    {
        PainterButton.IsChecked = Workspace.Camera.MistPaintingEnabled || Inspector.Painter.IsPainting;
        PainterIcon.Kind = _mistModeSelected ? MaterialIconKind.Cloud : MaterialIconKind.Spray;
        string name = _mistModeSelected ? "Paint local mist" : "Paint models and prefabs";
        ToolTip.SetTip(PainterButton, name + " · hold for painter tools");
        AutomationProperties.SetName(PainterButton, name);
        Inspector.SetPainterMode(_mistModeSelected);
        if (_ready) RefreshToolOptions();
    }
}

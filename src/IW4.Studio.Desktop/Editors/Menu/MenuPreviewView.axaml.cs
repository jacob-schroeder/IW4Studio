using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using IW4.Studio.Desktop.ViewModels.Menu;

namespace IW4.Studio.Desktop.Editors.Menu;

public sealed partial class MenuPreviewView : UserControl
{
    private static readonly TimeSpan SimulationClockInterval =
        TimeSpan.FromMilliseconds(16);

    private readonly DispatcherTimer _simulationClock =
        new(DispatcherPriority.Render)
        {
            Interval = SimulationClockInterval
        };
    private readonly ContextMenu _previewItemContextMenu;
    private MenuDesignerViewModel? _viewModel;
    private long _simulationClockStartTimestamp;
    private int _simulationClockStartMilliseconds;
    private bool _isAttached;
    private bool _isEditingSimulationTime;
    private bool _isUpdatingSimulationTime;

    public MenuPreviewView()
    {
        AvaloniaXamlLoader.Load(this);
        _previewItemContextMenu =
            (ContextMenu)Resources["PreviewItemContextMenu"]!;
        DataContextChanged += MenuPreviewView_DataContextChanged;
        _simulationClock.Tick += SimulationClock_Tick;
    }

    protected override void OnAttachedToVisualTree(
        VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        RebindViewModel();
        UpdateSimulationClock();
    }

    protected override void OnDetachedFromVisualTree(
        VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        _isEditingSimulationTime = false;
        _previewItemContextMenu.Close();
        StopSimulationClock();
        DetachViewModel();
        base.OnDetachedFromVisualTree(e);
    }

    private void PreviewControl_NodeSelected(
        object? sender,
        MenuPreviewNodeSelectedEventArgs e)
    {
        if (DataContext is MenuDesignerViewModel viewModel)
        {
            viewModel.SelectPreviewNode(e.NodeId);
            viewModel.RequestPropertiesReveal();
        }
    }

    private void PreviewControl_ContextRequested(
        object? sender,
        ContextRequestedEventArgs e)
    {
        e.Handled = true;
        if (sender is not MenuPreviewControl preview ||
            DataContext is not MenuDesignerViewModel viewModel ||
            !e.TryGetPosition(preview, out Point position) ||
            preview.HitTestNode(position) is not { } nodeId)
        {
            return;
        }

        viewModel.SelectPreviewNode(nodeId);
        if (viewModel.SelectedPreviewNodeId != nodeId)
            return;

        viewModel.RequestPropertiesReveal();
        _previewItemContextMenu.DataContext = viewModel;
        _previewItemContextMenu.Open(preview);
    }

    private void PreviewControl_MaterialResolutionCompleted(
        object? sender,
        MenuPreviewMaterialResolutionCompletedEventArgs e)
    {
        if (DataContext is MenuDesignerViewModel viewModel)
            viewModel.ReportMaterialPreviewStatus(e.Status);
    }

    private void PreviewControl_GeometryCommitted(
        object? sender,
        MenuPreviewGeometryCommittedEventArgs e)
    {
        if (DataContext is MenuDesignerViewModel viewModel)
        {
            _ = viewModel.CommitPreviewItemGeometry(
                e.NodeId,
                e.OriginalBounds,
                e.CandidateBounds);
        }
    }

    private void PreviewControl_TextResolutionCompleted(
        object? sender,
        MenuPreviewTextResolutionCompletedEventArgs e)
    {
        if (DataContext is MenuDesignerViewModel viewModel)
            viewModel.ReportTextPreviewStatus(e.Status);
    }

    private static void ScenarioStagedInput_LostFocus(
        object? sender,
        RoutedEventArgs e)
    {
        if (sender is Control
            {
                DataContext: MenuPreviewScenarioInputViewModel input
            })
        {
            _ = input.CommitPendingValue();
        }
    }

    private static void ScenarioStagedInput_KeyDown(
        object? sender,
        KeyEventArgs e)
    {
        if (sender is not Control
            {
                DataContext: MenuPreviewScenarioInputViewModel input
            })
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            _ = input.CommitPendingValue();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            input.ResetPendingValue();
            e.Handled = true;
        }
    }

    private void SimulationTime_GotFocus(
        object? sender,
        RoutedEventArgs e)
    {
        _isEditingSimulationTime = true;
        StopSimulationClock();
    }

    private void SimulationTime_LostFocus(
        object? sender,
        RoutedEventArgs e)
    {
        if (sender is not Control editor)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            if (editor.IsKeyboardFocusWithin)
                return;

            _isEditingSimulationTime = false;
            UpdateSimulationClock();
        });
    }

    private void MenuPreviewView_DataContextChanged(object? sender, EventArgs e) =>
        RebindViewModel();

    private void RebindViewModel()
    {
        MenuDesignerViewModel? viewModel = DataContext as
            MenuDesignerViewModel;
        if (viewModel?.IsDisposed == true)
            viewModel = null;
        if (ReferenceEquals(_viewModel, viewModel))
            return;

        DetachViewModel();
        _viewModel = viewModel;
        if (_viewModel is null)
            return;

        _viewModel.PreviewDebug.PropertyChanged +=
            PreviewDebug_PropertyChanged;
        _viewModel.PreviewDebug.Simulation.PropertyChanged +=
            Simulation_PropertyChanged;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _viewModel.Disposed += ViewModel_Disposed;
        UpdateSimulationClock();
    }

    private void DetachViewModel()
    {
        StopSimulationClock();
        _previewItemContextMenu.Close();
        _previewItemContextMenu.DataContext = null;
        if (_viewModel is null)
            return;

        _viewModel.PreviewDebug.PropertyChanged -=
            PreviewDebug_PropertyChanged;
        _viewModel.PreviewDebug.Simulation.PropertyChanged -=
            Simulation_PropertyChanged;
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        _viewModel.Disposed -= ViewModel_Disposed;
        _viewModel = null;
    }

    private void PreviewDebug_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MenuPreviewDebugViewModel.Mode) or
            nameof(MenuPreviewDebugViewModel.IsSimulating))
        {
            UpdateSimulationClock();
        }
    }

    private void Simulation_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MenuPreviewSimulationViewModel.Milliseconds) &&
            !_isUpdatingSimulationTime)
        {
            UpdateSimulationClock();
        }
    }

    private void ViewModel_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MenuDesignerViewModel.HasDocument) or
            nameof(MenuDesignerViewModel.IsComplete))
        {
            UpdateSimulationClock();
        }
    }

    private void ViewModel_Disposed(object? sender, EventArgs e) =>
        DetachViewModel();

    private void SimulationClock_Tick(object? sender, EventArgs e)
    {
        if (!ShouldAdvanceSimulation())
        {
            StopSimulationClock();
            return;
        }

        long elapsedMilliseconds = Stopwatch.GetElapsedTime(
            _simulationClockStartTimestamp).Ticks / TimeSpan.TicksPerMillisecond;
        long milliseconds = Math.Min(
            int.MaxValue,
            (long)_simulationClockStartMilliseconds + elapsedMilliseconds);
        if (milliseconds == _viewModel!.PreviewDebug.Simulation.Milliseconds)
            return;

        _isUpdatingSimulationTime = true;
        try
        {
            _viewModel.PreviewDebug.Simulation.Milliseconds = (int)milliseconds;
        }
        finally
        {
            _isUpdatingSimulationTime = false;
        }

        if (milliseconds == int.MaxValue)
            StopSimulationClock();
    }

    private void UpdateSimulationClock()
    {
        if (!ShouldAdvanceSimulation())
        {
            StopSimulationClock();
            return;
        }

        ResetSimulationClockOrigin();
        _simulationClock.Start();
    }

    private void ResetSimulationClockOrigin()
    {
        if (!ShouldAdvanceSimulation())
            return;

        _simulationClockStartTimestamp = Stopwatch.GetTimestamp();
        _simulationClockStartMilliseconds =
            _viewModel!.PreviewDebug.Simulation.Milliseconds;
    }

    private void StopSimulationClock()
    {
        _simulationClock.Stop();
        _simulationClockStartTimestamp = 0;
    }

    private bool ShouldAdvanceSimulation()
    {
        MenuDesignerViewModel? viewModel = _viewModel;
        return _isAttached &&
            !_isEditingSimulationTime &&
            viewModel is { HasDocument: true, IsComplete: true } &&
            viewModel.PreviewDebug.IsSimulating;
    }
}

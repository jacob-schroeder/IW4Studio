using System.Windows.Input;

namespace IW4.Studio.Desktop.ViewModels;

/// <summary>
/// Small command adapter for view-model-owned actions whose availability can
/// change independently of their binding.
/// </summary>
public sealed class ViewModelCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public ViewModelCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute ??
            throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) =>
        _canExecute?.Invoke() ?? true;

    public void Execute(object? parameter)
    {
        if (CanExecute(parameter))
            _execute();
    }

    public void RaiseCanExecuteChanged() =>
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

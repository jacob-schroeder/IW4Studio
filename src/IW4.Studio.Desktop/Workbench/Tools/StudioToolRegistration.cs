using Avalonia.Controls;
using IW4.Studio.Desktop.Workbench.Docking;

namespace IW4.Studio.Desktop.Workbench.Tools;

/// <summary>
/// Desktop composition entry pairing immutable docking metadata with one
/// reusable tool view. Placeholder registrations deliberately have no view.
/// </summary>
public sealed class StudioToolRegistration
{
    public StudioToolRegistration(
        DockToolDescriptor descriptor,
        Control? content,
        object? viewModel)
    {
        Descriptor = descriptor
            ?? throw new ArgumentNullException(nameof(descriptor));
        if (descriptor.IsImplemented != (content is not null && viewModel is not null))
        {
            throw new ArgumentException(
                "Implemented tools require content and a view model; placeholders require neither.",
                nameof(content));
        }

        Content = content;
        ViewModel = viewModel;
    }

    public DockToolDescriptor Descriptor { get; }

    public Control? Content { get; }

    public object? ViewModel { get; }
}

using System.Windows.Input;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using icsmooi.Models;

namespace icsmooi.ViewModels;

/// <summary>
/// Represents the in-progress connection being drawn on the canvas.
/// The Nodify PendingConnection control writes Source/Target/TargetLocation
/// back to this VM via TwoWay bindings.
/// </summary>
public partial class PendingConnectionViewModel : ObservableObject
{
    /// <summary>The pin the user started dragging from (set by PendingConnection via TwoWay).</summary>
    [ObservableProperty]
    private PinViewModel? _source;

    /// <summary>The pin the user is currently hovering over (null if over empty canvas).</summary>
    [ObservableProperty]
    private PinViewModel? _target;

    /// <summary>Controls visibility of the pending connection line.</summary>
    [ObservableProperty]
    private bool _isVisible;

    /// <summary>Current cursor position (used to open the node palette when dropping on canvas).</summary>
    [ObservableProperty]
    private Point _targetLocation;

    /// <summary>Injected from MainWindowViewModel so the template can bind directly.</summary>
    public ICommand? StartedCommand { get; set; }

    /// <summary>Injected from MainWindowViewModel so the template can bind directly.</summary>
    public ICommand? CompletedCommand { get; set; }
}

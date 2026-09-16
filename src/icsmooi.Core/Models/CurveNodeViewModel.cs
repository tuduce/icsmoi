using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace icsmooi.Models;

/// <summary>A single draggable control point on a <see cref="CurveNodeViewModel"/>.</summary>
public partial class CurvePoint : ObservableObject
{
    [ObservableProperty]
    private double _x;

    [ObservableProperty]
    private double _y;
}

/// <summary>
/// Multi-point piecewise-linear curve — the general-purpose replacement for a
/// hardcoded telemetry-to-force formula (e.g. an airspeed-to-spring-stiffness
/// sigmoid). <see cref="Engine.GraphEvaluator"/> linearly interpolates between
/// the two points bracketing the input value, sorted by <see cref="CurvePoint.X"/>,
/// and clamps to the first/last point's Y outside the point range.
/// </summary>
public partial class CurveNodeViewModel : NodeViewModel
{
    public ObservableCollection<CurvePoint> Points { get; set; } = [];

    public CurveNodeViewModel()
    {
        Name = "Curve";
        Inputs.Add(new PinViewModel { Title = "Value", IsInput = true });
        Outputs.Add(new PinViewModel { Title = "Result", IsInput = false });
        Points.Add(new CurvePoint { X = 0, Y = 0 });
        Points.Add(new CurvePoint { X = 1, Y = 1 });
    }
}

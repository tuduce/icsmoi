using CommunityToolkit.Mvvm.ComponentModel;

namespace icsmoi.Models;

public partial class RangeMapNodeViewModel : NodeViewModel
{
    /// <summary>When true, the mapped result is clamped to [OutMin, OutMax] (order-independent).</summary>
    [ObservableProperty]
    private bool _clamp = true;

    public RangeMapNodeViewModel()
    {
        Name = "Range Map";
        Inputs.Add(new PinViewModel { Title = "Value", IsInput = true });
        // Identity 0..1 -> 0..1 by default (an all-zero range would collapse every input to OutMin).
        Inputs.Add(new PinViewModel { Title = "InMin", IsInput = true });
        Inputs.Add(new PinViewModel { Title = "InMax", IsInput = true, Value = 1.0 });
        Inputs.Add(new PinViewModel { Title = "OutMin", IsInput = true });
        Inputs.Add(new PinViewModel { Title = "OutMax", IsInput = true, Value = 1.0 });
        Outputs.Add(new PinViewModel { Title = "Result", IsInput = false });
    }
}

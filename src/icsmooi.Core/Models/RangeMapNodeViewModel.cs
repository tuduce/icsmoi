using CommunityToolkit.Mvvm.ComponentModel;

namespace icsmooi.Models;

public partial class RangeMapNodeViewModel : NodeViewModel
{
    /// <summary>When true, the mapped result is clamped to [OutMin, OutMax] (order-independent).</summary>
    [ObservableProperty]
    private bool _clamp = true;

    public RangeMapNodeViewModel()
    {
        Name = "Range Map";
        Inputs.Add(new PinViewModel { Title = "Value", IsInput = true });
        Inputs.Add(new PinViewModel { Title = "InMin", IsInput = true });
        Inputs.Add(new PinViewModel { Title = "InMax", IsInput = true });
        Inputs.Add(new PinViewModel { Title = "OutMin", IsInput = true });
        Inputs.Add(new PinViewModel { Title = "OutMax", IsInput = true });
        Outputs.Add(new PinViewModel { Title = "Result", IsInput = false });
    }
}

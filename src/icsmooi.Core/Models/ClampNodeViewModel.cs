namespace icsmooi.Models;

public partial class ClampNodeViewModel : NodeViewModel
{
    public ClampNodeViewModel()
    {
        Name = "Clamp";
        Inputs.Add(new PinViewModel { Title = "Value", IsInput = true });
        Inputs.Add(new PinViewModel { Title = "Min", IsInput = true });
        Inputs.Add(new PinViewModel { Title = "Max", IsInput = true });
        Outputs.Add(new PinViewModel { Title = "Result", IsInput = false });
    }
}

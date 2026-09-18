namespace icsmoi.Models;

public partial class SelectNodeViewModel : NodeViewModel
{
    public SelectNodeViewModel()
    {
        Name = "Select";
        Inputs.Add(new PinViewModel { Title = "Condition", IsInput = true });
        Inputs.Add(new PinViewModel { Title = "IfTrue", IsInput = true });
        Inputs.Add(new PinViewModel { Title = "IfFalse", IsInput = true });
        Outputs.Add(new PinViewModel { Title = "Result", IsInput = false });
    }
}

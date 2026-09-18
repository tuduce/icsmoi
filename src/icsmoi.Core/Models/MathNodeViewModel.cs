using CommunityToolkit.Mvvm.ComponentModel;

namespace icsmoi.Models;

public partial class MathNodeViewModel : NodeViewModel
{
    [ObservableProperty]
    private string _expression = "[A] + [B]";

    public MathNodeViewModel()
    {
        Name = "Math Compute";
        Inputs.Add(new PinViewModel { Title = "A", IsInput = true });
        Inputs.Add(new PinViewModel { Title = "B", IsInput = true });
        Outputs.Add(new PinViewModel { Title = "Result", IsInput = false });
    }
}

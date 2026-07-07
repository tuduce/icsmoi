using CommunityToolkit.Mvvm.ComponentModel;

namespace icsmooi.Models;

public partial class MathNodeViewModel : NodeViewModel
{
    [ObservableProperty]
    private string _expression = "[A] + [B]";

    public MathNodeViewModel()
    {
        Name = "Math Compute";
        Inputs.Add(new PinViewModel { Name = "A" });
        Inputs.Add(new PinViewModel { Name = "B" });
        Outputs.Add(new PinViewModel { Name = "Result" });
    }
}

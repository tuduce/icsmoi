using CommunityToolkit.Mvvm.ComponentModel;

namespace icsmooi.Models;

public partial class FfbOutputNodeViewModel : NodeViewModel
{
    [ObservableProperty]
    private string _effectType = "Constant Force";

    public FfbOutputNodeViewModel()
    {
        Name = "FFB Output";
        Inputs.Add(new PinViewModel { Title = "Magnitude", IsInput = true });
    }
}

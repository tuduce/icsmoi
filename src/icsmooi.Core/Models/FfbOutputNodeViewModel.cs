using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace icsmooi.Models;

public partial class FfbOutputNodeViewModel : NodeViewModel
{
    [ObservableProperty]
    private string _effectType = "Constant Force";

    /// <summary>
    /// Last computed output magnitude from the evaluation engine.
    /// Runtime-only — never persisted to JSON.
    /// </summary>
    [JsonIgnore]
    [ObservableProperty]
    private double _lastMagnitude;

    public FfbOutputNodeViewModel()
    {
        Name = "FFB Output";
        Inputs.Add(new PinViewModel { Title = "Magnitude", IsInput = true });
    }
}

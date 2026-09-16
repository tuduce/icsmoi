using icsmooi.Models;

namespace icsmooi.ViewModels;

/// <summary>
/// A live, UI-layer connection between two PinViewModels.
/// Not serialised — rebuilt from ConnectionViewModel GUIDs on load.
/// </summary>
public sealed class UiConnectionViewModel
{
    /// <summary>The output (source) pin — right side of a node.</summary>
    public PinViewModel Output { get; }

    /// <summary>The input (target) pin — left side of a node.</summary>
    public PinViewModel Input { get; }

    public UiConnectionViewModel(PinViewModel output, PinViewModel input)
    {
        Output = output;
        Input = input;
    }
}

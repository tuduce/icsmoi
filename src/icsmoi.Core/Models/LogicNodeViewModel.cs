using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace icsmoi.Models;

public enum LogicOperator { And, Or, Not, Xor }

public partial class LogicNodeViewModel : NodeViewModel
{
    /// <summary>Bindable source for the operator picker's ItemsSource.</summary>
    public static readonly LogicOperator[] Operators = Enum.GetValues<LogicOperator>();

    [ObservableProperty]
    private LogicOperator _operator = LogicOperator.And;

    public LogicNodeViewModel()
    {
        Name = "Logic";
        // B is unused when Operator == Not — still present rather than
        // dynamically hidden, to keep pin/connection management simple.
        Inputs.Add(new PinViewModel { Title = "A", IsInput = true });
        Inputs.Add(new PinViewModel { Title = "B", IsInput = true });
        Outputs.Add(new PinViewModel { Title = "Result", IsInput = false });
    }
}

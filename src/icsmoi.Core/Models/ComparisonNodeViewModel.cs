using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace icsmoi.Models;

public enum ComparisonOperator { GreaterThan, LessThan, GreaterOrEqual, LessOrEqual, Equal, NotEqual }

public partial class ComparisonNodeViewModel : NodeViewModel
{
    /// <summary>Bindable source for the operator picker's ItemsSource.</summary>
    public static readonly ComparisonOperator[] Operators = Enum.GetValues<ComparisonOperator>();

    [ObservableProperty]
    private ComparisonOperator _operator = ComparisonOperator.GreaterThan;

    public ComparisonNodeViewModel()
    {
        Name = "Compare";
        Inputs.Add(new PinViewModel { Title = "A", IsInput = true });
        Inputs.Add(new PinViewModel { Title = "B", IsInput = true });
        Outputs.Add(new PinViewModel { Title = "Result", IsInput = false });
    }
}

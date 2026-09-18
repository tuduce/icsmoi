using System;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using NodifyM.Avalonia.ViewModelBase;

namespace icsmoi.Models;

[JsonDerivedType(typeof(SimConnectNodeViewModel), "simconnect")]
[JsonDerivedType(typeof(MathNodeViewModel), "math")]
[JsonDerivedType(typeof(ComparisonNodeViewModel), "comparison")]
[JsonDerivedType(typeof(LogicNodeViewModel), "logic")]
[JsonDerivedType(typeof(SelectNodeViewModel), "select")]
[JsonDerivedType(typeof(ClampNodeViewModel), "clamp")]
[JsonDerivedType(typeof(RangeMapNodeViewModel), "rangemap")]
[JsonDerivedType(typeof(CurveNodeViewModel), "curve")]
[JsonDerivedType(typeof(ConstantForceOutputNodeViewModel), "constantforce")]
[JsonDerivedType(typeof(ConditionOutputNodeViewModel), "condition")]
[JsonDerivedType(typeof(PeriodicOutputNodeViewModel), "periodic")]
[JsonDerivedType(typeof(RampForceOutputNodeViewModel), "rampforce")]
public abstract partial class NodeViewModel : ObservableObject, INodePosition
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [ObservableProperty]
    private string _name = "Base Node";

    [ObservableProperty]
    private double _x;

    [ObservableProperty]
    private double _y;

    /// <summary>Runtime-only: whether this node is currently selected on the canvas.</summary>
    [JsonIgnore]
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Maps Nodify's Point-based Location to the serialised X/Y doubles.</summary>
    [JsonIgnore]
    public Point Location
    {
        get => new(X, Y);
        set { X = value.X; Y = value.Y; }
    }

    partial void OnXChanged(double value) => OnPropertyChanged(nameof(Location));
    partial void OnYChanged(double value) => OnPropertyChanged(nameof(Location));

    public ObservableCollection<PinViewModel> Inputs { get; set; } = [];
    public ObservableCollection<PinViewModel> Outputs { get; set; } = [];
}

using System;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace icsmooi.Models;

[JsonDerivedType(typeof(SimConnectNodeViewModel), "simconnect")]
[JsonDerivedType(typeof(MathNodeViewModel), "math")]
[JsonDerivedType(typeof(FfbOutputNodeViewModel), "ffb")]
public abstract partial class NodeViewModel : ObservableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [ObservableProperty]
    private string _name = "Base Node";

    [ObservableProperty]
    private double _x;

    [ObservableProperty]
    private double _y;

    public ObservableCollection<PinViewModel> Inputs { get; set; } = [];
    public ObservableCollection<PinViewModel> Outputs { get; set; } = [];
}

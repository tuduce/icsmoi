using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace icsmooi.Models;

public partial class PinViewModel : ObservableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [ObservableProperty]
    private string _name = string.Empty;
}

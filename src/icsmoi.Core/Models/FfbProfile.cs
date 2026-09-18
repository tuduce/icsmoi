using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace icsmoi.Models;

public partial class FfbProfile : ObservableObject
{
    [ObservableProperty]
    private string _profileName = "New Profile";

    public ObservableCollection<NodeViewModel> Nodes { get; set; } = [];
    public ObservableCollection<ConnectionViewModel> Connections { get; set; } = [];
}

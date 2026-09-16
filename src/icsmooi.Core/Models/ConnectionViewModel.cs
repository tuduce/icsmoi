using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace icsmooi.Models;

public partial class ConnectionViewModel : ObservableObject
{
    public Guid SourceNodeId { get; set; }
    public Guid SourcePinId { get; set; }
    public Guid TargetNodeId { get; set; }
    public Guid TargetPinId { get; set; }
}

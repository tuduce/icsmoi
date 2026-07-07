using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using icsmooi.Models;
using icsmooi.Services;
using NodifyM.Avalonia.ViewModelBase;

namespace icsmooi.ViewModels;

public partial class MainWindowViewModel : NodifyEditorViewModelBase
{
    private readonly ProfileSerializerService _serializer = new();

    // The serialisable profile � holds profile name and GUID-keyed connection records.
    [ObservableProperty]
    private FfbProfile _profile = CreateDefaultProfile();

    public MainWindowViewModel()
    {
        // Seed the NodifyEditor Nodes collection from the initial profile
        foreach (var node in _profile.Nodes) Nodes.Add(node);
    }

    // -- Node palette --------------------------------------------------------

    [RelayCommand]
    private void AddSimConnectNode()
    {
        var node = new SimConnectNodeViewModel { X = 60, Y = 150 };
        Nodes.Add(node);
        Profile.Nodes.Add(node);
    }

    [RelayCommand]
    private void AddMathNode()
    {
        var node = new MathNodeViewModel { X = 300, Y = 150 };
        Nodes.Add(node);
        Profile.Nodes.Add(node);
    }

    [RelayCommand]
    private void AddFfbOutputNode()
    {
        var node = new FfbOutputNodeViewModel { X = 560, Y = 150 };
        Nodes.Add(node);
        Profile.Nodes.Add(node);
    }

    // -- Connection handling (overrides NodifyEditorViewModelBase) -----------

    public override void Connect(ConnectorViewModelBase source, ConnectorViewModelBase target)
    {
        if (source is not PinViewModel srcPin || target is not PinViewModel tgtPin) return;
        if (srcPin.IsInput == tgtPin.IsInput) return; // same direction � invalid

        var outputPin = srcPin.IsInput ? tgtPin : srcPin;
        var inputPin  = srcPin.IsInput ? srcPin  : tgtPin;

        // Locate owning nodes
        var sourceNode = Nodes.OfType<NodeViewModel>().FirstOrDefault(n => n.Outputs.Contains(outputPin));
        var targetNode = Nodes.OfType<NodeViewModel>().FirstOrDefault(n => n.Inputs.Contains(inputPin));
        if (sourceNode is null || targetNode is null) return;

        // Each input pin accepts only one wire � disconnect existing first
        var existing = Connections.FirstOrDefault(c => c.Target == inputPin);
        if (existing is not null) DisconnectConnector(inputPin);

        // UI connection
        Connections.Add(new ConnectionViewModelBase(this, outputPin, inputPin));
        outputPin.IsConnected = true;
        inputPin.IsConnected = true;

        // Serialised record
        Profile.Connections.Add(new ConnectionViewModel
        {
            SourceNodeId = sourceNode.Id, SourcePinId = outputPin.Id,
            TargetNodeId = targetNode.Id, TargetPinId = inputPin.Id
        });
    }

    public override void DisconnectConnector(ConnectorViewModelBase connector)
    {
        base.DisconnectConnector(connector); // updates Connections + IsConnected flags

        if (connector is PinViewModel pin)
        {
            var toRemove = Profile.Connections
                .Where(c => c.SourcePinId == pin.Id || c.TargetPinId == pin.Id)
                .ToList();
            foreach (var c in toRemove) Profile.Connections.Remove(c);
        }
    }

    // -- Delete selected nodes -----------------------------------------------

    [RelayCommand]
    private void DeleteSelection()
    {
        var selected = Nodes.OfType<NodeViewModel>().Where(n => n.IsSelected).ToList();
        foreach (var node in selected)
        {
            foreach (var pin in node.Inputs.Concat(node.Outputs).ToList())
                DisconnectConnector(pin);
            Nodes.Remove(node);
            Profile.Nodes.Remove(node);
        }
    }

    // -- Save / Load ---------------------------------------------------------

    [RelayCommand]
    private async Task SaveAsync()
    {
        var dir = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
        Directory.CreateDirectory(dir);
        await _serializer.SaveAsync(Profile, Path.Combine(dir, $"{Profile.ProfileName}.icsmooi.json"));
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        var dir = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
        var path = Path.Combine(dir, $"{Profile.ProfileName}.icsmooi.json");
        if (File.Exists(path))
            LoadProfile(await _serializer.LoadAsync(path));
    }

    // -- Internal helpers ----------------------------------------------------

    private void LoadProfile(FfbProfile loaded)
    {
        Nodes.Clear();
        Connections.Clear();
        Profile = loaded;

        foreach (var node in loaded.Nodes) Nodes.Add(node);

        var nodeById = loaded.Nodes.ToDictionary(n => n.Id);
        foreach (var conn in loaded.Connections)
        {
            if (!nodeById.TryGetValue(conn.SourceNodeId, out var srcNode)) continue;
            if (!nodeById.TryGetValue(conn.TargetNodeId, out var tgtNode)) continue;
            var output = srcNode.Outputs.FirstOrDefault(p => p.Id == conn.SourcePinId);
            var input  = tgtNode.Inputs.FirstOrDefault(p => p.Id == conn.TargetPinId);
            if (output is null || input is null) continue;
            output.IsConnected = true;
            input.IsConnected  = true;
            Connections.Add(new ConnectionViewModelBase(this, output, input));
        }
    }

    private static FfbProfile CreateDefaultProfile()
    {
        var p = new FfbProfile { ProfileName = "My Profile" };
        p.Nodes.Add(new SimConnectNodeViewModel { X = 60,  Y = 150 });
        p.Nodes.Add(new MathNodeViewModel       { X = 300, Y = 150 });
        p.Nodes.Add(new FfbOutputNodeViewModel  { X = 560, Y = 150 });
        return p;
    }
}


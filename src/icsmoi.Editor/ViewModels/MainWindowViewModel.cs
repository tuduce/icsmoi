using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using icsmoi.Engine;
using icsmoi.Models;
using icsmoi.Services;
using icsmoi.Services.DirectInput;
using NodifyM.Avalonia.ViewModelBase;

namespace icsmoi.ViewModels;

public partial class MainWindowViewModel : NodifyEditorViewModelBase
{
    private readonly ProfileSerializerService _serializer = new();

    // ── DirectInput device picker (enumeration only — the Editor never
    // acquires a device or creates a real effect; see icsmoi.Runtime for that) ──

    private readonly FfbDeviceManager _deviceManager = new();

    public ObservableCollection<FfbDeviceInfo> AvailableDevices { get; } = [];

    [RelayCommand]
    private void RefreshDevices()
    {
        AvailableDevices.Clear();
        foreach (var device in _deviceManager.EnumerateDevices())
            AvailableDevices.Add(device);
    }

    // ── Evaluation engine ────────────────────────────────────────────────────

    public FfbEngineService Engine { get; } = new();

    [ObservableProperty]
    private bool _engineRunning;

    // ── SimConnect telemetry (test-run only — the Editor never drives real
    // FFB hardware; see icsmoi.Runtime for that) ───────────────────────────

    private SimConnectTelemetryService? _simConnectService;
    private IntPtr _windowHandle;

    /// <summary>The user has switched the sim connection on (it may still be connecting/retrying).</summary>
    [ObservableProperty]
    private bool _simConnectActive;

    /// <summary>SimConnect's handshake has completed — telemetry is really flowing.</summary>
    [ObservableProperty]
    private bool _simConnected;

    // ── Status line ──────────────────────────────────────────────────────────
    // One fixed-size field in the toolbar showing only the *latest* message from
    // any source (sim, engine, file). Sim/engine *state* is shown by their toolbar
    // buttons' icon and tint, so a later message replacing an earlier one loses nothing.

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private bool _statusIsError;

    private void SetStatus(string message, bool isError = false)
    {
        StatusMessage = message;
        StatusIsError = isError;
    }

    // ── Profile name editing (toolbar) ───────────────────────────────────────

    [ObservableProperty]
    private bool _isEditingProfileName;

    /// <summary>Text in the name box while editing; only written back to the profile on commit.</summary>
    [ObservableProperty]
    private string _profileNameDraft = "";

    [RelayCommand]
    private void BeginEditProfileName()
    {
        ProfileNameDraft = Profile.ProfileName;
        IsEditingProfileName = true;
    }

    [RelayCommand]
    private void CommitProfileName()
    {
        if (!IsEditingProfileName) return; // Enter, focus loss and the ✓ button can all fire — commit once
        var name = ProfileNameDraft.Trim();
        if (name.Length > 0) Profile.ProfileName = name; // an empty name keeps the old one
        IsEditingProfileName = false;
    }

    [RelayCommand]
    private void CancelEditProfileName() => IsEditingProfileName = false;

    // The serialisable profile � holds profile name and GUID-keyed connection records.
    [ObservableProperty]
    private FfbProfile _profile = CreateDefaultProfile();

    public MainWindowViewModel()
    {
        // Seed the NodifyEditor Nodes collection from the initial profile
        foreach (var node in _profile.Nodes) Nodes.Add(node);
        RefreshDevices();
    }

    // ── Engine commands ──────────────────────────────────────────────────────

    [RelayCommand]
    private void ToggleEngine()
    {
        if (EngineRunning)
        {
            Engine.Stop();
            EngineRunning = false;
            SetStatus("Engine: Stopped");
        }
        else
        {
            Engine.OutputsUpdated += OnEngineOutputsUpdated;
            Engine.Start(Profile);
            EngineRunning = true;
            SetStatus("Engine: Running");
        }
    }

    // FfbEngineService fires this on its own timer thread — marshal to the UI thread
    // before touching bound view-model state.
    private void OnEngineOutputsUpdated(IReadOnlyDictionary<Guid, double> outputs)
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var cf in Nodes.OfType<ConstantForceOutputNodeViewModel>())
                if (outputs.TryGetValue(cf.Id, out var mag))
                    cf.LastMagnitude = mag;

            foreach (var cond in Nodes.OfType<ConditionOutputNodeViewModel>())
                if (outputs.TryGetValue(cond.Id, out var coeff))
                    cond.LastPositiveCoefficient = coeff;

            foreach (var periodic in Nodes.OfType<PeriodicOutputNodeViewModel>())
                if (outputs.TryGetValue(periodic.Id, out var pMag))
                    periodic.LastMagnitude = pMag;

            foreach (var ramp in Nodes.OfType<RampForceOutputNodeViewModel>())
                if (outputs.TryGetValue(ramp.Id, out var rMag))
                    ramp.LastStartMagnitude = rMag;
        });
    }

    // ── SimConnect commands ──────────────────────────────────────────────────

    /// <summary>Called by the View once it has a real native window handle to hand to SimConnect.</summary>
    public void AttachWindowHandle(IntPtr handle) => _windowHandle = handle;

    [RelayCommand]
    private void ToggleSimConnection()
    {
        if (SimConnectActive)
        {
            _simConnectService!.StateChanged -= OnSimConnectStateChanged;
            _simConnectService.Dispose();
            _simConnectService = null;
            SimConnectActive = false;
            SimConnected = false;
            SetStatus("Sim: Disconnected");
        }
        else
        {
            _simConnectService = new SimConnectTelemetryService(Engine.SimData, _windowHandle);
            _simConnectService.StateChanged += OnSimConnectStateChanged;
            _simConnectService.Start();
            SimConnectActive = true;
            SetStatus("Sim: Connecting…");
        }
    }

    // SimConnectTelemetryService fires this on its own background thread.
    private void OnSimConnectStateChanged(SimConnectionState state)
    {
        Dispatcher.UIThread.Post(() =>
        {
            // A state change queued just before the user disconnected must not
            // resurrect a "Connected" status/tint after the fact.
            if (!SimConnectActive) return;

            SimConnected = state == SimConnectionState.Connected;
            SetStatus(state switch
            {
                SimConnectionState.Connected => "Sim: Connected",
                SimConnectionState.Connecting => "Sim: Connecting…",
                _ => "Sim: Disconnected (retrying…)",
            });
        });
    }

    // -- Node palette --------------------------------------------------------

    [RelayCommand]
    private void AddSimConnectNode()
    {
        DeselectAll();
        var node = new SimConnectNodeViewModel { X = 60, Y = 150 };
        node.IsSelected = true;
        Nodes.Add(node);
        Profile.Nodes.Add(node);
    }

    [RelayCommand]
    private void AddMathNode()
    {
        DeselectAll();
        var node = new MathNodeViewModel { X = 300, Y = 150 };
        node.IsSelected = true;
        Nodes.Add(node);
        Profile.Nodes.Add(node);
    }

    [RelayCommand]
    private void AddComparisonNode()
    {
        DeselectAll();
        var node = new ComparisonNodeViewModel { X = 300, Y = 300 };
        node.IsSelected = true;
        Nodes.Add(node);
        Profile.Nodes.Add(node);
    }

    [RelayCommand]
    private void AddLogicNode()
    {
        DeselectAll();
        var node = new LogicNodeViewModel { X = 300, Y = 300 };
        node.IsSelected = true;
        Nodes.Add(node);
        Profile.Nodes.Add(node);
    }

    [RelayCommand]
    private void AddSelectNode()
    {
        DeselectAll();
        var node = new SelectNodeViewModel { X = 300, Y = 300 };
        node.IsSelected = true;
        Nodes.Add(node);
        Profile.Nodes.Add(node);
    }

    [RelayCommand]
    private void AddClampNode()
    {
        DeselectAll();
        var node = new ClampNodeViewModel { X = 300, Y = 300 };
        node.IsSelected = true;
        Nodes.Add(node);
        Profile.Nodes.Add(node);
    }

    [RelayCommand]
    private void AddRangeMapNode()
    {
        DeselectAll();
        var node = new RangeMapNodeViewModel { X = 300, Y = 300 };
        node.IsSelected = true;
        Nodes.Add(node);
        Profile.Nodes.Add(node);
    }

    [RelayCommand]
    private void AddCurveNode()
    {
        DeselectAll();
        var node = new CurveNodeViewModel { X = 300, Y = 300 };
        node.IsSelected = true;
        Nodes.Add(node);
        Profile.Nodes.Add(node);
    }

    [RelayCommand]
    private void AddConstantForceOutputNode()
    {
        DeselectAll();
        var node = new ConstantForceOutputNodeViewModel { X = 560, Y = 300 };
        node.IsSelected = true;
        Nodes.Add(node);
        Profile.Nodes.Add(node);
    }

    [RelayCommand]
    private void AddConditionOutputNode()
    {
        DeselectAll();
        var node = new ConditionOutputNodeViewModel { X = 560, Y = 300 };
        node.IsSelected = true;
        Nodes.Add(node);
        Profile.Nodes.Add(node);
    }

    [RelayCommand]
    private void AddPeriodicOutputNode()
    {
        DeselectAll();
        var node = new PeriodicOutputNodeViewModel { X = 560, Y = 300 };
        node.IsSelected = true;
        Nodes.Add(node);
        Profile.Nodes.Add(node);
    }

    [RelayCommand]
    private void AddRampForceOutputNode()
    {
        DeselectAll();
        var node = new RampForceOutputNodeViewModel { X = 560, Y = 300 };
        node.IsSelected = true;
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
    // The OS file pickers live in MainWindow.axaml.cs (only a Window has a
    // StorageProvider); it hands the chosen paths to these methods.

    public const string ProfileFileExtension = ".icsmoi.json";

    /// <summary>Path of the file this profile was last loaded from / saved to; seeds the pickers' start folder.</summary>
    [ObservableProperty]
    private string? _currentFilePath;

    /// <summary>Default file name offered by the Save dialog, derived from the profile name.</summary>
    public string SuggestedFileName
    {
        get
        {
            var name = string.Concat(Profile.ProfileName.Select(c =>
                Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c)).Trim();
            return (name.Length == 0 ? "profile" : name) + ProfileFileExtension;
        }
    }

    public async Task SaveToPathAsync(string path)
    {
        path = NormalizeProfilePath(path);
        try
        {
            await _serializer.SaveAsync(Profile, path);
            CurrentFilePath = path;
            SetStatus($"Saved {Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            SetStatus($"Save failed: {ex.Message}", isError: true);
        }
    }

    public async Task LoadFromPathAsync(string path)
    {
        try
        {
            LoadProfile(await _serializer.LoadAsync(path));
            CurrentFilePath = path;
            SetStatus($"Loaded {Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            // Arbitrary files can now be picked, so a wrong/older/corrupt file is expected
            // (e.g. a profile still containing the removed "FFB Output" node).
            SetStatus($"Load failed: {ex.Message}", isError: true);
        }
    }

    // Runtime's picker only shows *.icsmoi.json, so make sure saved files carry that suffix
    // even when the user typed a bare name or plain ".json".
    private static string NormalizeProfilePath(string path)
    {
        if (path.EndsWith(ProfileFileExtension, StringComparison.OrdinalIgnoreCase)) return path;
        if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) path = path[..^".json".Length];
        return path + ProfileFileExtension;
    }

    // -- Internal helpers ----------------------------------------------------

    private void LoadProfile(FfbProfile loaded)
    {
        IsEditingProfileName = false; // a half-typed name belongs to the profile being replaced
        Nodes.Clear();
        Connections.Clear();
        Profile = loaded;

        foreach (var node in loaded.Nodes)
        {
            // IsConnected is persisted with each pin, but the connections below are the
            // source of truth — a stale flag would wrongly hide an input pin's value field.
            foreach (var pin in node.Inputs.Concat(node.Outputs)) pin.IsConnected = false;
            Nodes.Add(node);
        }

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
        p.Nodes.Add(new MathNodeViewModel       { X = 340, Y = 150 });
        p.Nodes.Add(new ConstantForceOutputNodeViewModel { X = 640, Y = 150 });
        return p;
    }

    private void DeselectAll()
    {
        foreach (var n in Nodes.OfType<NodeViewModel>())
            n.IsSelected = false;
    }
}


using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
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

        // Joystick Input nodes read *any* game controller, not just force-feedback ones.
        AvailableJoysticks.Clear();
        foreach (var device in JoystickDevices.EnumerateDevices())
            AvailableJoysticks.Add(device);

        foreach (var node in Nodes.OfType<HardwareOutputNodeViewModel>())
            UpdateAxisChoices(node);
    }

    public ObservableCollection<InputDeviceInfo> AvailableJoysticks { get; } = [];

    // ── Hardware-output axis dropdowns ───────────────────────────────────────
    // Each hardware-output node's dropdown lists "All axes" plus the force axes of *its* device, so
    // the choices have to follow the node's device: they're refreshed when a node is added/loaded,
    // when its device changes, and when the device list is re-read.

    private void OnNodesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (var node in e.OldItems.OfType<HardwareOutputNodeViewModel>())
                node.PropertyChanged -= OnHardwareNodePropertyChanged;

        if (e.NewItems is not null)
            foreach (var node in e.NewItems.OfType<HardwareOutputNodeViewModel>())
            {
                node.PropertyChanged += OnHardwareNodePropertyChanged;
                UpdateAxisChoices(node);
            }
    }

    private void OnHardwareNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HardwareOutputNodeViewModel.DeviceInstanceGuid) && sender is HardwareOutputNodeViewModel node)
            UpdateAxisChoices(node);
    }

    private void UpdateAxisChoices(HardwareOutputNodeViewModel node)
    {
        var axes = AvailableDevices.FirstOrDefault(d => d.InstanceGuid == node.DeviceInstanceGuid)?.Axes ?? [];
        node.AxisChoices =
        [
            new AxisChoice(HardwareOutputNodeViewModel.AllAxes, "All axes"),
            .. axes.Select((axis, index) => new AxisChoice(index, axis.Name)),
        ];
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
        RefreshDevices(); // before seeding, so the initial nodes' axis dropdowns can be filled
        Nodes.CollectionChanged += OnNodesCollectionChanged;
        // Seed the NodifyEditor Nodes collection from the initial profile
        foreach (var node in _profile.Nodes) Nodes.Add(node);
    }

    // ── Engine commands ──────────────────────────────────────────────────────

    [RelayCommand]
    private void ToggleEngine()
    {
        if (EngineRunning)
        {
            Engine.Stop();
            StopJoystickInput();
            EngineRunning = false;
            SetStatus("Engine: Stopped");
        }
        else
        {
            StartJoystickInput();
            Engine.OutputsUpdated += OnEngineOutputsUpdated;
            Engine.Start(Profile);
            EngineRunning = true;
            SetStatus("Engine: Running");
        }
    }

    // ── Joystick input (read-only test-run, alongside the engine) ────────────
    // Joysticks are only polled while the engine runs, like SimConnect telemetry is only
    // live while connected. Reading a stick is harmless — the Editor's no-hardware-*output*
    // rule is about force feedback.

    private JoystickInputService? _joystickService;
    private DispatcherTimer? _joystickTrackingTimer;

    private void StartJoystickInput()
    {
        _joystickService = new JoystickInputService(Engine.SimData, _windowHandle);
        _joystickService.SetTrackedDevices(JoystickInputService.DevicesUsedBy(Nodes.OfType<NodeViewModel>()));
        _joystickService.Start();

        // The poll thread must not touch the (UI-thread) node graph, so re-derive the set of
        // devices in use here whenever it might have changed (node added/removed, device picked).
        _joystickTrackingTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background,
            (_, _) => _joystickService?.SetTrackedDevices(JoystickInputService.DevicesUsedBy(Nodes.OfType<NodeViewModel>())));
        _joystickTrackingTimer.Start();
    }

    private void StopJoystickInput()
    {
        _joystickTrackingTimer?.Stop();
        _joystickTrackingTimer = null;
        _joystickService?.Dispose();
        _joystickService = null;
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
    private void AddJoystickInputNode()
    {
        DeselectAll();
        var node = new JoystickInputNodeViewModel { X = 60, Y = 300 };
        node.IsSelected = true;
        Nodes.Add(node);
        Profile.Nodes.Add(node);
    }

    // -- Joystick Input node: rows are pins, so adding/removing one is a graph edit (the
    // dual-update rule: a removed pin's connections must go from both Connections and Profile).

    /// <summary>Raised after a Joystick node's pins change, so the view can re-measure pin anchors (see MainWindow.axaml.cs).</summary>
    public event Action? PinLayoutChanged;

    [RelayCommand]
    private void AddJoystickInput(JoystickInputNodeViewModel node)
    {
        node.Outputs.Add(new JoystickPinViewModel());
        PinLayoutChanged?.Invoke();
    }

    [RelayCommand]
    private void RemoveJoystickInput(JoystickPinViewModel pin)
    {
        var node = Nodes.OfType<JoystickInputNodeViewModel>().FirstOrDefault(n => n.Outputs.Contains(pin));
        if (node is null) return;

        if (ReferenceEquals(_capturingPin, pin)) _captureCts?.Cancel();
        DisconnectConnector(pin);
        node.Outputs.Remove(pin);
        PinLayoutChanged?.Invoke();
    }

    private static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(15);
    private CancellationTokenSource? _captureCts;
    private JoystickPinViewModel? _capturingPin;

    /// <summary>
    /// Click an input field, then move an axis/slider or press a button on the node's joystick:
    /// whichever the user actuates first is bound to that pin. Clicking the field again cancels.
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task BeginJoystickCapture(JoystickPinViewModel pin)
    {
        if (ReferenceEquals(_capturingPin, pin)) { _captureCts?.Cancel(); return; }
        _captureCts?.Cancel(); // switching to another field abandons the previous capture

        var node = Nodes.OfType<JoystickInputNodeViewModel>().FirstOrDefault(n => n.Outputs.Contains(pin));
        if (node?.DeviceInstanceGuid is not { } device)
        {
            SetStatus("Pick a joystick in the node first", isError: true);
            return;
        }

        using var cts = new CancellationTokenSource(CaptureTimeout);
        _captureCts = cts;
        _capturingPin = pin;
        pin.IsListening = true;
        SetStatus("Move an axis or press a button…");

        try
        {
            var handle = _windowHandle;
            var input = await Task.Run(() => JoystickDevices.Capture(device, handle, cts.Token));
            if (input is { } captured)
            {
                pin.Assign(captured.Kind, captured.Index);
                SetStatus($"Assigned {pin.Title}");
            }
            else if (ReferenceEquals(_capturingPin, pin)) // not superseded by a newer capture
            {
                SetStatus("Input assignment cancelled");
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Couldn't read the joystick: {ex.Message}", isError: true);
        }
        finally
        {
            pin.IsListening = false;
            if (ReferenceEquals(_capturingPin, pin))
            {
                _capturingPin = null;
                _captureCts = null;
            }
        }
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
    private void AddEdgeDetectorNode()
    {
        DeselectAll();
        var node = new EdgeDetectorNodeViewModel { X = 300, Y = 300 };
        node.IsSelected = true;
        Nodes.Add(node);
        Profile.Nodes.Add(node);
    }

    [RelayCommand]
    private void AddIntegratorNode()
    {
        DeselectAll();
        var node = new IntegratorNodeViewModel { X = 300, Y = 300 };
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

    /// <summary>Profile to open once the window is up (set from the Runtime's <c>--profile</c> argument).</summary>
    public string? StartupProfilePath { get; set; }

    /// <summary>
    /// Saves over <see cref="CurrentFilePath"/> without a dialog. Returns <c>false</c> when the profile has no
    /// file yet, so the caller can fall back to Save As.
    /// </summary>
    public async Task<bool> TrySaveInPlaceAsync()
    {
        if (CurrentFilePath is not { } path) return false;
        await SaveToPathAsync(path);
        return true;
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
        _captureCts?.Cancel();        // as does a pending joystick input assignment
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
        p.Nodes.Add(new ConstantForceOutputNodeViewModel { X = 720, Y = 150 }); // clear of the Math node, which now carries A/B fields
        return p;
    }

    private void DeselectAll()
    {
        foreach (var n in Nodes.OfType<NodeViewModel>())
            n.IsSelected = false;
    }
}


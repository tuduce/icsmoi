using System;
using System.Collections.Generic;
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

namespace icsmoi.Runtime.ViewModels;

/// <summary>
/// Drives the Runtime app's status window: loads a saved profile, connects to
/// SimConnect, runs the engine, and — only when the user explicitly opts in
/// via <see cref="EffectsEnabled"/> — drives real DirectInput hardware.
///
/// <para><b>Effects safety gate:</b> loading a profile and running the engine
/// only ever produces simulated magnitude readouts. Real hardware is never
/// touched (no device acquisition, no effect creation) until the user flips
/// <see cref="EffectsEnabled"/> on — default off. Flipping it off immediately
/// tears down every live effect via <see cref="FfbEffectManager.Dispose"/>.</para>
/// </summary>
public sealed partial class StatusWindowViewModel : ObservableObject, IDisposable
{
    private readonly ProfileSerializerService _serializer = new();
    private readonly FfbEngineService _engine = new();
    private readonly RuntimeSettingsService _settings = new();

    private SimConnectTelemetryService? _simConnectService;
    private JoystickInputService? _joystickService;
    private volatile FfbEffectManager? _effectManager;
    private IntPtr _windowHandle;
    private FfbProfile? _profile;

    [ObservableProperty]
    private string _profileName = "(no profile loaded)";

    [ObservableProperty]
    private string _simConnectStatus = "Sim: Disconnected";

    [ObservableProperty]
    private string _engineStatus = "Engine: Stopped";

    [ObservableProperty]
    private bool _effectsEnabled;

    [ObservableProperty]
    private string _effectsStatus = "Effects: Disabled";

    public void AttachWindowHandle(IntPtr handle)
    {
        _windowHandle = handle;

        var lastProfilePath = _settings.Load().LastProfilePath;
        if (lastProfilePath is not null && File.Exists(lastProfilePath))
            _ = LoadProfileFromPathAsync(lastProfilePath);
    }

    [RelayCommand]
    private async Task LoadProfileAsync(string path)
    {
        await LoadProfileFromPathAsync(path);
    }

    private async Task LoadProfileFromPathAsync(string path)
    {
        _profile = await _serializer.LoadAsync(path);
        ProfileName = _profile.ProfileName;
        _settings.Save(new RuntimeSettings(path));

        StartTelemetryAndEngine();
    }

    private void StartTelemetryAndEngine()
    {
        if (_profile is null) return;

        _simConnectService?.Dispose();
        _simConnectService = new SimConnectTelemetryService(_engine.SimData, _windowHandle);
        _simConnectService.StateChanged += OnSimConnectStateChanged;
        _simConnectService.Start();

        // Joystick Input nodes: read-only polling of whichever joysticks the profile uses.
        // Independent of the Effects gate below — it never drives hardware.
        _joystickService?.Dispose();
        _joystickService = new JoystickInputService(_engine.SimData, _windowHandle);
        _joystickService.SetTrackedDevices(JoystickInputService.DevicesUsedBy(_profile.Nodes));
        _joystickService.Start();

        _engine.OutputsUpdated -= OnEngineOutputsUpdated;
        _engine.OutputsUpdated += OnEngineOutputsUpdated;
        _engine.HardwareOutputsUpdated -= OnHardwareOutputsUpdated;
        _engine.HardwareOutputsUpdated += OnHardwareOutputsUpdated;
        _engine.Start(_profile);
        EngineStatus = "Engine: Running";
    }

    partial void OnEffectsEnabledChanged(bool value)
    {
        if (value)
        {
            _effectManager = new FfbEffectManager(new FfbDeviceManager(), _windowHandle);
            EffectsStatus = "Effects: Enabled";
        }
        else
        {
            _effectManager?.Dispose();
            _effectManager = null;
            EffectsStatus = "Effects: Disabled";
        }
    }

    private void OnEngineOutputsUpdated(IReadOnlyDictionary<Guid, double> outputs)
    {
        // No per-node UI list yet (see Phase 9) — magnitudes aren't consumed here today,
        // but the subscription exists so future status UI can bind to them.
    }

    // Fired on FfbEngineService's timer thread. _effectManager is volatile and
    // captured once here so a concurrent UI-thread toggle-off can't null it out
    // mid-call — Dispose() on the old instance may still race a SyncTick call
    // already in flight, which is an accepted narrow risk for this pass.
    private void OnHardwareOutputsUpdated(IReadOnlyDictionary<Guid, EffectOutputParams> effectParams)
    {
        var manager = _effectManager;
        if (manager is null || _profile is null) return;
        manager.SyncTick(_profile.Nodes.ToList(), effectParams);
    }

    // Fired on SimConnectTelemetryService's own background thread.
    private void OnSimConnectStateChanged(SimConnectionState state)
    {
        Dispatcher.UIThread.Post(() =>
        {
            SimConnectStatus = state switch
            {
                SimConnectionState.Connected => "Sim: Connected",
                SimConnectionState.Connecting => "Sim: Connecting…",
                _ => "Sim: Disconnected (retrying…)",
            };
        });
    }

    public void Dispose()
    {
        _engine.Stop();
        _engine.Dispose();
        _simConnectService?.Dispose();
        _joystickService?.Dispose();
        _effectManager?.Dispose();
    }
}

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
/// Drives the Runtime app's status window: holds up to <see cref="RuntimeSettings.SlotCount"/> profiles,
/// runs the active one against SimConnect telemetry, remembers which aircraft uses which profile, hot-reloads
/// a profile when the Editor saves it, and — only when the user explicitly opts in via
/// <see cref="EffectsEnabled"/> — drives real DirectInput hardware.
///
/// <para><b>Effects safety gate:</b> loading a profile and running the engine
/// only ever produces simulated magnitude readouts. Real hardware is never
/// touched (no device acquisition, no effect creation) until the user flips
/// <see cref="EffectsEnabled"/> on — default off. Flipping it off immediately
/// tears down every live effect via <see cref="FfbEffectManager.Dispose"/>.
/// Switching profile (by hand or because the aircraft changed) never touches that switch.</para>
/// </summary>
public sealed partial class StatusWindowViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private readonly ProfileSerializerService _serializer = new();
    private readonly FfbEngineService _engine = new();
    private readonly RuntimeSettingsService _settingsService = new();
    private readonly EditorLauncher _editor = new();

    private RuntimeSettings _settings = new();
    private SimConnectTelemetryService? _simConnectService;
    private JoystickInputService? _joystickService;
    private DispatcherTimer? _pollTimer;
    private volatile FfbEffectManager? _effectManager;
    // Read on the engine's timer thread (OnHardwareOutputsUpdated), written on the UI thread.
    private volatile FfbProfile? _profile;
    private AircraftAssociation? _missingAssociation;
    private IntPtr _windowHandle;
    private bool _started;
    private bool _polling;

    public StatusWindowViewModel()
    {
        Slots = Enumerable.Range(0, RuntimeSettings.SlotCount).Select(i => new ProfileSlotViewModel(this, i)).ToArray();
    }

    /// <summary>The running build's version (Major.Minor of the assembly version, e.g. "v26.1"), shown next to
    /// the "Runtime" caption so a bug report can include which release it came from.</summary>
    public string VersionText { get; } = FormatVersion();

    private static string FormatVersion()
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        return version is null ? "" : $"v{version.Major}.{version.Minor}";
    }

    // ── Profile slots ────────────────────────────────────────────────────────

    public IReadOnlyList<ProfileSlotViewModel> Slots { get; }

    /// <summary>The slot whose profile the engine is running, or <c>null</c> when none is.</summary>
    [ObservableProperty]
    private ProfileSlotViewModel? _activeSlot;

    /// <summary>A short problem message under the slot list (Editor couldn't start, a picked file wouldn't load…).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSlotMessage))]
    private string? _slotMessage;

    public bool HasSlotMessage => SlotMessage is not null;

    /// <summary>Raised when a slot wants a profile file chosen for it; the window shows the file picker.</summary>
    public event Action<ProfileSlotViewModel>? LoadRequested;

    // ── Aircraft ─────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAircraft), nameof(AircraftDisplay))]
    private string? _aircraftTitle;

    public bool HasAircraft => AircraftTitle is not null;

    public string AircraftDisplay => AircraftTitle ?? "No aircraft — waiting for the sim";

    /// <summary>Set while the current aircraft is tied to a profile that isn't loaded in any slot.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAircraftWarning))]
    private string? _aircraftWarning;

    public bool HasAircraftWarning => AircraftWarning is not null;

    // ── Status ───────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SimStatusText))]
    private bool _simConnected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SimStatusText))]
    private bool _simConnecting;

    public string SimStatusText => SimConnected ? "Sim connected" : SimConnecting ? "Sim connecting…" : "Sim offline";

    [ObservableProperty]
    private string _engineStatus = "Engine: Stopped";

    [ObservableProperty]
    private bool _effectsEnabled;

    [ObservableProperty]
    private string _effectsStatus = "Effects: Disabled";

    // ── Startup ──────────────────────────────────────────────────────────────

    public void AttachWindowHandle(IntPtr handle)
    {
        if (_started) return;
        _started = true;
        _windowHandle = handle;
        _ = StartAsync();
    }

    private async Task StartAsync()
    {
        _settings = _settingsService.Load();

        for (var i = 0; i < Slots.Count; i++)
            Slots[i].FilePath = _settings.Slots[i];

        // Load every slot before SimConnect starts, so an aircraft title that arrives straight away is
        // resolved against the real slots rather than against empty ones.
        foreach (var slot in Slots.Where(s => !s.IsEmpty))
            await ReloadSlotAsync(slot);

        _engine.OutputsUpdated += OnEngineOutputsUpdated;
        _engine.HardwareOutputsUpdated += OnHardwareOutputsUpdated;

        // Joystick Input nodes: read-only polling of whichever joysticks the active profile uses.
        // Independent of the Effects gate below — it never drives hardware.
        _joystickService = new JoystickInputService(_engine.SimData, _windowHandle);
        _joystickService.Start();

        // One telemetry connection for the whole run — switching profile only swaps the graph.
        _simConnectService = new SimConnectTelemetryService(_engine.SimData, _windowHandle);
        _simConnectService.StateChanged += OnSimConnectStateChanged;
        _simConnectService.AircraftChanged += OnAircraftChanged;
        _simConnectService.Start();

        if (_settings.ActiveSlot is { } index && Slots[index].IsLoaded)
            ActivateSlot(Slots[index], switching: false);
        else
            EngineStatus = "Engine: Stopped — pick a profile";

        _pollTimer = new DispatcherTimer(PollInterval, DispatcherPriority.Background, OnPollTimerTick);
        _pollTimer.Start();
    }

    // ── Slot operations (called from ProfileSlotViewModel's commands) ────────

    /// <summary>The user picked this slot's profile: run it, and — if an aircraft is loaded — remember it for that aircraft.</summary>
    public void SelectSlot(ProfileSlotViewModel slot)
    {
        if (!slot.IsLoaded) return;

        if (!slot.IsActive) ActivateSlot(slot, switching: true);
        RememberAircraft(slot);
        SlotMessage = null;
    }

    public void RequestLoadInto(ProfileSlotViewModel slot) => LoadRequested?.Invoke(slot);

    public void EditSlot(ProfileSlotViewModel slot)
    {
        if (slot.FilePath is not { } path) return;
        SlotMessage = _editor.Open(path);
    }

    public void ClearSlot(ProfileSlotViewModel slot)
    {
        var wasActive = slot.IsActive;

        slot.IsActive = false;
        slot.FilePath = null;
        slot.Stamp = null;
        slot.SetLoaded(null, null);

        if (wasActive)
        {
            _engine.Stop();
            _profile = null;
            ActiveSlot = null;
            ClearEffects();
            EngineStatus = "Engine: Stopped — pick a profile";
        }

        SlotMessage = null;
        SaveSettings();
        ApplyAircraftAssociation(switchAllowed: HasAircraftWarning);
    }

    /// <summary>Loads <paramref name="path"/> into <paramref name="slot"/> and runs it (the user just chose it).</summary>
    public async Task LoadIntoSlotAsync(ProfileSlotViewModel slot, string path)
    {
        path = Path.GetFullPath(path);

        var duplicate = Slots.FirstOrDefault(s => s != slot && s.FilePath is { } other && AircraftProfileResolver.SamePath(other, path));
        if (duplicate is not null)
        {
            SlotMessage = $"“{duplicate.Name}” is already loaded in slot {duplicate.Index + 1}.";
            return;
        }

        FfbProfile profile;
        (DateTime, long)? stamp;
        try
        {
            stamp = TryStamp(path); // before reading: a save that lands mid-read is picked up by the next poll
            profile = await _serializer.LoadAsync(path);
        }
        catch (Exception ex)
        {
            // Leave the slot as it was — a bad pick must not wipe a working profile.
            SlotMessage = $"Couldn't load {Path.GetFileName(path)}: {ex.Message}";
            return;
        }

        slot.FilePath = path;
        slot.Stamp = stamp;
        slot.SetLoaded(profile, null);

        ActivateSlot(slot, switching: true);
        RememberAircraft(slot);
        SlotMessage = null;
        SaveSettings();
    }

    [RelayCommand]
    private async Task LoadMissingProfileAsync()
    {
        if (_missingAssociation is not { } missing) return;

        if (!File.Exists(missing.Path))
        {
            SlotMessage = $"“{missing.Name}” no longer exists at {missing.Path}.";
            return;
        }

        var free = Slots.FirstOrDefault(s => s.IsEmpty);
        if (free is null)
        {
            SlotMessage = "All slots are in use — clear or replace one to load it.";
            return;
        }

        await LoadIntoSlotAsync(free, missing.Path);
    }

    // ── Activating a profile ─────────────────────────────────────────────────

    /// <param name="switching">A different profile than the one running (vs. the same one reloaded after an Editor save).
    /// Switching first tears down the old profile's hardware effects.</param>
    private void ActivateSlot(ProfileSlotViewModel slot, bool switching)
    {
        if (slot.Profile is not { } profile) return;

        foreach (var s in Slots) s.IsActive = ReferenceEquals(s, slot);
        ActiveSlot = slot;

        _engine.Stop();
        _profile = profile;
        if (switching) ClearEffects();
        _joystickService?.SetTrackedDevices(JoystickInputService.DevicesUsedBy(profile.Nodes));
        _engine.Start(profile);

        EngineStatus = $"Engine: Running — {slot.Name}";
        SaveSettings(); // records ActiveSlot
    }

    /// <summary>
    /// Stops every live effect of the outgoing profile. The next tick would do it anyway (the effect manager
    /// drops effects whose node is gone), but that tick is skipped if the new graph doesn't evaluate.
    /// </summary>
    private void ClearEffects()
    {
        try
        {
            _effectManager?.SyncTick([], new Dictionary<Guid, EffectOutputParams>());
        }
        catch (Exception ex)
        {
            LogError("clearing effects on profile switch", ex);
        }
    }

    // ── Aircraft → profile ───────────────────────────────────────────────────

    // Fired on SimConnectTelemetryService's own background thread.
    private void OnAircraftChanged(string? title)
    {
        Dispatcher.UIThread.Post(() =>
        {
            AircraftTitle = title;
            ApplyAircraftAssociation(switchAllowed: true);
        });
    }

    /// <summary>
    /// Applies the remembered profile for the current aircraft. <paramref name="switchAllowed"/> is false when
    /// only the warning should be re-evaluated (a slot changed), so nothing changes profile behind the user's back.
    /// </summary>
    private void ApplyAircraftAssociation(bool switchAllowed)
    {
        AircraftWarning = null;
        _missingAssociation = null;

        var loadedPaths = Slots.Select(s => s.IsLoaded ? s.FilePath : null).ToList();
        var decision = AircraftProfileResolver.Resolve(AircraftTitle, _settings.AircraftProfiles, loadedPaths);

        switch (decision.Kind)
        {
            case AircraftDecisionKind.Switch when switchAllowed:
                var slot = Slots[decision.SlotIndex];
                if (!slot.IsActive) ActivateSlot(slot, switching: true);
                break;

            case AircraftDecisionKind.WarnMissing:
                // The choice is deliberately left where it is: the user is told, not overridden.
                _missingAssociation = decision.Association;
                var keeping = ActiveSlot is { } active ? $"Keeping “{active.Name}”." : "No profile is active.";
                AircraftWarning = $"{AircraftTitle} is set to “{decision.Association!.Name}”, which isn't loaded in a slot. {keeping}";
                break;
        }
    }

    private void RememberAircraft(ProfileSlotViewModel slot)
    {
        AircraftWarning = null;
        _missingAssociation = null;

        if (AircraftTitle is not { } title || slot.FilePath is not { } path) return;

        _settings.AircraftProfiles[title] = new AircraftAssociation(path, slot.Name);
        SaveSettings();
    }

    // A profile renamed in the Editor keeps its associations (they're keyed by file) — refresh the
    // remembered display name too, since the warning text shows it.
    private void RefreshAssociationNames(ProfileSlotViewModel slot)
    {
        if (slot.FilePath is not { } path) return;

        var changed = false;
        foreach (var (title, association) in _settings.AircraftProfiles.ToList())
        {
            if (AircraftProfileResolver.SamePath(association.Path, path) && association.Name != slot.Name)
            {
                _settings.AircraftProfiles[title] = association with { Name = slot.Name };
                changed = true;
            }
        }
        if (changed) SaveSettings();
    }

    // ── Reloading profiles when their file changes ───────────────────────────

    private static (DateTime LastWriteUtc, long Length)? TryStamp(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? (info.LastWriteTimeUtc, info.Length) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Re-reads a slot's file. On failure the previously loaded profile (if any) keeps running and the error
    /// is shown on the card; the stamp is left alone so the next poll tries again.
    /// </summary>
    private async Task<bool> ReloadSlotAsync(ProfileSlotViewModel slot)
    {
        if (slot.FilePath is not { } path) return false;

        try
        {
            var stamp = TryStamp(path);
            var profile = await _serializer.LoadAsync(path);
            slot.Stamp = stamp;
            slot.SetLoaded(profile, null);
            return true;
        }
        catch (Exception ex)
        {
            slot.SetLoaded(slot.Profile, ex is FileNotFoundException ? "File not found" : $"Couldn't read: {ex.Message}");
            return false;
        }
    }

    private async void OnPollTimerTick(object? sender, EventArgs e)
    {
        if (_polling) return;
        _polling = true;
        try
        {
            foreach (var slot in Slots)
            {
                if (slot.FilePath is null) continue;
                if (Equals(TryStamp(slot.FilePath), slot.Stamp)) continue;

                if (!await ReloadSlotAsync(slot)) continue;

                RefreshAssociationNames(slot);
                // The Editor saved the profile that is running: swap in the new graph (same profile, so effects stay).
                if (slot.IsActive) ActivateSlot(slot, switching: false);
                ApplyAircraftAssociation(switchAllowed: HasAircraftWarning);
            }
        }
        catch (Exception ex)
        {
            LogError("polling profile files", ex);
        }
        finally
        {
            _polling = false;
        }
    }

    private void SaveSettings()
    {
        try
        {
            for (var i = 0; i < Slots.Count; i++)
                _settings.Slots[i] = Slots[i].FilePath;
            _settings.ActiveSlot = ActiveSlot?.Index;
            _settingsService.Save(_settings);
        }
        catch (Exception ex)
        {
            LogError("saving runtime settings", ex);
        }
    }

    // ── Effects gate (unchanged) ─────────────────────────────────────────────

    partial void OnEffectsEnabledChanged(bool value)
    {
        if (value)
        {
            var manager = new FfbEffectManager(new FfbDeviceManager(), _windowHandle);
            manager.EffectFailed += OnEffectFailed;
            _effectManager = manager;
            EffectsStatus = "Effects: Enabled";
        }
        else
        {
            // Order matters: hide the manager from new ticks first, stop listening to it, only then dispose.
            var manager = _effectManager;
            _effectManager = null;
            if (manager is not null) manager.EffectFailed -= OnEffectFailed;
            manager?.Dispose();
            EffectsStatus = "Effects: Disabled";
        }
    }

    // Fired on the engine's timer thread when one node's effect can't be created/updated (device
    // unavailable, parameters the driver rejected…). The manager has already torn that effect down and
    // will retry in a few seconds; the rest keeps running — this just makes the failure visible.
    private void OnEffectFailed(HardwareOutputNodeViewModel node, Exception error)
    {
        LogError($"effect '{node.Name}' ({node.Id})", error);
        Dispatcher.UIThread.Post(() =>
        {
            // The switch may have been turned off since this was raised — don't overwrite "Disabled".
            if (_effectManager is null) return;
            EffectsStatus = $"Effects: {node.Name} failed — {error.GetType().Name}: {error.Message.Trim()}";
        });
    }

    /// <summary>Appends the full exception to %LOCALAPPDATA%\icsmoi\runtime-errors.log (the status line only fits a fragment).</summary>
    private static void LogError(string what, Exception error)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "icsmoi");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "runtime-errors.log"), $"{DateTime.Now:s}  {what}\n{error}\n\n");
        }
        catch
        {
            // logging must never be the thing that fails
        }
    }

    private void OnEngineOutputsUpdated(IReadOnlyDictionary<Guid, double> outputs)
    {
        // No per-node UI list yet (see Phase 9) — magnitudes aren't consumed here today,
        // but the subscription exists so future status UI can bind to them.
    }

    // Fired on FfbEngineService's timer thread. _effectManager and _profile are volatile and each captured
    // once here so a concurrent UI-thread change (effects toggled off, profile switched) can't null them out
    // mid-call. A tick that straddles a profile switch just finds no parameters for the new nodes and
    // does nothing; the next tick is consistent.
    private void OnHardwareOutputsUpdated(IReadOnlyDictionary<Guid, EffectOutputParams> effectParams)
    {
        var manager = _effectManager;
        var profile = _profile;
        if (manager is null || profile is null) return;
        manager.SyncTick(profile.Nodes.ToList(), effectParams);
    }

    // Fired on SimConnectTelemetryService's own background thread.
    private void OnSimConnectStateChanged(SimConnectionState state)
    {
        Dispatcher.UIThread.Post(() =>
        {
            SimConnected = state == SimConnectionState.Connected;
            SimConnecting = state == SimConnectionState.Connecting;
        });
    }

    public void Dispose()
    {
        _pollTimer?.Stop();
        _engine.Stop();
        _engine.Dispose();
        _simConnectService?.Dispose();
        _joystickService?.Dispose();
        _effectManager?.Dispose();
    }
}

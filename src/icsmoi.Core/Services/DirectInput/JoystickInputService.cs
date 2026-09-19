using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using icsmoi.Models;
using Vortice.DirectInput;

namespace icsmoi.Services.DirectInput;

/// <summary>
/// Polls the joysticks referenced by Joystick Input nodes on its own background thread and
/// publishes every axis (normalized to [-1, 1]) and button (1.0 / 0.0) of each into a shared
/// dictionary — in practice <c>FfbEngineService.SimData</c>, next to the SimConnect telemetry —
/// under <see cref="JoystickInputKey.For"/> keys, where <c>GraphEvaluator</c> reads them.
/// Read-only and non-exclusive, so both apps may use it (see <see cref="JoystickDevices"/>).
///
/// <para>The host tells it which devices to poll via <see cref="SetTrackedDevices"/> (from the UI
/// thread, when nodes/devices change) — the poll thread never touches the node graph itself.
/// A device that can't be opened (unplugged, held exclusively elsewhere) is retried every
/// couple of seconds, and its values read as 0 meanwhile.</para>
/// </summary>
public sealed class JoystickInputService : IDisposable
{
    private readonly ConcurrentDictionary<string, double> _sink;
    private readonly IntPtr _windowHandle;
    private readonly CancellationTokenSource _cancellation = new();
    private volatile Guid[] _tracked = [];
    private Task? _task;

    public JoystickInputService(ConcurrentDictionary<string, double> sink, IntPtr windowHandle)
    {
        _sink = sink;
        _windowHandle = windowHandle;
    }

    public void Start() =>
        _task ??= Task.Factory.StartNew(Run, TaskCreationOptions.LongRunning);

    /// <summary>Replaces the set of devices to poll. Cheap; safe to call from any thread.</summary>
    public void SetTrackedDevices(IEnumerable<Guid> devices) => _tracked = devices.Distinct().ToArray();

    /// <summary>The devices a set of nodes reads from — what a host passes to <see cref="SetTrackedDevices"/>.</summary>
    public static IEnumerable<Guid> DevicesUsedBy(IEnumerable<NodeViewModel> nodes) =>
        nodes.OfType<JoystickInputNodeViewModel>()
             .Select(n => n.DeviceInstanceGuid)
             .OfType<Guid>();

    private sealed class OpenDevice(IDirectInputDevice8 device, string[] axisKeys, string[] buttonKeys) : IDisposable
    {
        public IDirectInputDevice8 Device { get; } = device;
        public string[] AxisKeys { get; } = axisKeys;
        public string[] ButtonKeys { get; } = buttonKeys;

        public void Dispose()
        {
            try { Device.Unacquire(); } catch { }
            Device.Dispose();
        }
    }

    private void Run()
    {
        var ct = _cancellation.Token;
        using var directInput = DInput.DirectInput8Create();
        var open = new Dictionary<Guid, OpenDevice>();
        var retryAt = new Dictionary<Guid, long>();
        var axes = new double[JoystickInputKey.AxisCount];
        var buttons = new bool[JoystickInputKey.ButtonCount];

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var tracked = _tracked;

                foreach (var stale in open.Keys.Where(g => !tracked.Contains(g)).ToList())
                    Close(open, stale);

                foreach (var guid in tracked)
                {
                    if (!open.TryGetValue(guid, out var device))
                    {
                        if (retryAt.TryGetValue(guid, out var due) && Environment.TickCount64 < due) continue;
                        try
                        {
                            device = new OpenDevice(
                                JoystickDevices.Open(directInput, guid, _windowHandle),
                                Enumerable.Range(0, JoystickInputKey.AxisCount).Select(i => JoystickInputKey.For(guid, JoystickInputKind.Axis, i)).ToArray(),
                                Enumerable.Range(0, JoystickInputKey.ButtonCount).Select(i => JoystickInputKey.For(guid, JoystickInputKind.Button, i)).ToArray());
                            open[guid] = device;
                            retryAt.Remove(guid);
                        }
                        catch
                        {
                            retryAt[guid] = Environment.TickCount64 + 2000;
                            continue;
                        }
                    }

                    try
                    {
                        JoystickDevices.Read(device.Device, axes, buttons);
                        for (var i = 0; i < axes.Length; i++) _sink[device.AxisKeys[i]] = axes[i];
                        for (var i = 0; i < buttons.Length; i++) _sink[device.ButtonKeys[i]] = buttons[i] ? 1.0 : 0.0;
                    }
                    catch
                    {
                        // Lost the device mid-read (unplugged?) — zero it and reopen shortly.
                        Close(open, guid);
                        retryAt[guid] = Environment.TickCount64 + 1000;
                    }
                }

                ct.WaitHandle.WaitOne(4);
            }
        }
        finally
        {
            foreach (var guid in open.Keys.ToList())
                Close(open, guid);
        }
    }

    // Publishes 0 for everything the device provided, so a removed joystick doesn't leave stuck values behind.
    private void Close(Dictionary<Guid, OpenDevice> open, Guid guid)
    {
        if (!open.Remove(guid, out var device)) return;

        foreach (var key in device.AxisKeys) _sink[key] = 0.0;
        foreach (var key in device.ButtonKeys) _sink[key] = 0.0;
        device.Dispose();
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        try { _task?.Wait(TimeSpan.FromSeconds(1)); } catch { }
        _cancellation.Dispose();
    }
}

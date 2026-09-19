using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using icsmoi.Models;
using Microsoft.FlightSimulator.SimConnect;

namespace icsmoi.Services;

public enum SimConnectionState { Disconnected, Connecting, Connected }

/// <summary>
/// Background MSFS SimConnect client. Connects to MSFS2020/2024, subscribes to
/// the telemetry catalog in <see cref="SimVariableCatalog"/>, and writes live
/// values into a shared <see cref="ConcurrentDictionary{TKey,TValue}"/> — in
/// practice, <see cref="Engine.FfbEngineService.SimData"/> — keyed by variable
/// name exactly as <see cref="SimConnectNodeViewModel.VariableName"/> expects.
///
/// <para>Modeled on TDX-Air-Mechanics' <c>SimConnectService.cs</c> polling-loop
/// pattern (manual <c>ReceiveMessage()</c> + <see cref="Thread.Sleep(int)"/> on a
/// dedicated <see cref="TaskCreationOptions.LongRunning"/> task — no Win32
/// message-pump dispatch needed even though the SimConnect constructor takes a
/// window handle), with two deliberate deltas: this service auto-retries the
/// connection on a timer instead of requiring a manual "Connect" click, and
/// requests <see cref="SIMCONNECT_PERIOD.VISUAL_FRAME"/> instead of TDX's
/// <c>SECOND</c> — FFB effects are latency-sensitive in a way TDX's slower
/// hardcoded UI reactions were not.</para>
/// </summary>
public sealed class SimConnectTelemetryService : IDisposable
{
    private const int WmUserSimConnect = 0x0402;
    private const string AppName = "icsmoi";
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(5);

    // Some SimConnect versions construct successfully even when the sim isn't
    // running yet and simply never raise OnRecvOpen — without this timeout that
    // looks identical to "still connecting" forever instead of retrying.
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    private enum DataDefinition { Telemetry, Aircraft }
    private enum DataRequest { Telemetry, Aircraft }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private unsafe struct TelemetryData
    {
        public fixed double Values[SimVariableCatalog.Count];
    }

    // A string can't live in TelemetryData's all-double buffer, so the aircraft title has its own
    // definition/request — same ByValTStr pattern as the sibling TDX projects.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    private struct AircraftInfo
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Title;
    }

    private readonly ConcurrentDictionary<string, double> _simData;
    private readonly IntPtr _windowHandle;

    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private SimConnect? _simConnect;
    private DateTime _connectAttemptStartUtc;

    private volatile SimConnectionState _state = SimConnectionState.Disconnected;

    /// <summary>Current connection state — safe to read from any thread.</summary>
    public SimConnectionState State => _state;

    /// <summary>Fired on this service's own background thread on every state transition.</summary>
    public event Action<SimConnectionState>? StateChanged;

    private volatile string? _aircraftTitle;

    /// <summary>
    /// SimConnect <c>TITLE</c> of the user's aircraft (the aircraft.cfg title, e.g. "Cessna 172 Skyhawk G1000 Asobo"),
    /// or <c>null</c> while not connected / not yet reported. Safe to read from any thread.
    /// </summary>
    public string? AircraftTitle => _aircraftTitle;

    /// <summary>
    /// Fired on this service's own background thread when <see cref="AircraftTitle"/> changes — including
    /// to <c>null</c> when the sim disconnects.
    /// </summary>
    public event Action<string?>? AircraftChanged;

    /// <param name="simData">Written into on every telemetry update — pass <see cref="Engine.FfbEngineService.SimData"/>.</param>
    /// <param name="windowHandle">A real native window handle. SimConnect's constructor requires one even though
    /// this service never pumps Win32 messages for it — it polls <c>ReceiveMessage()</c> manually instead.</param>
    public SimConnectTelemetryService(ConcurrentDictionary<string, double> simData, IntPtr windowHandle)
    {
        _simData = simData;
        _windowHandle = windowHandle;
    }

    public void Start()
    {
        if (_loopTask is not null) return;

        _cts = new CancellationTokenSource();
        _loopTask = Task.Factory.StartNew(
            () => Loop(_cts.Token),
            _cts.Token,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    public void Stop()
    {
        if (_loopTask is null) return;

        try
        {
            _cts?.Cancel();
            try { _loopTask.Wait(); }
            catch (AggregateException ex) { ex.Handle(e => e is OperationCanceledException); }
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            _loopTask = null;
            Disconnect();
        }
    }

    public void Dispose() => Stop();

    private void Loop(CancellationToken token)
    {
        var nextRetryUtc = DateTime.MinValue;

        while (!token.IsCancellationRequested)
        {
            if (_simConnect is null)
            {
                if (DateTime.UtcNow >= nextRetryUtc)
                {
                    TryConnect();
                    nextRetryUtc = DateTime.UtcNow + RetryInterval;
                }
                Thread.Sleep(200);
                continue;
            }

            if (_state == SimConnectionState.Connecting && DateTime.UtcNow - _connectAttemptStartUtc > ConnectTimeout)
            {
                // Constructed without throwing but never reached OnRecvOpen — give up and retry.
                Disconnect();
                continue;
            }

            try
            {
                _simConnect.ReceiveMessage();
            }
            catch (COMException)
            {
                // Pipe broken / sim closed — drop back to the retry loop.
                Disconnect();
            }

            Thread.Sleep(16);
        }
    }

    private void TryConnect()
    {
        SetState(SimConnectionState.Connecting);
        _connectAttemptStartUtc = DateTime.UtcNow;
        try
        {
            var sc = new SimConnect(AppName, _windowHandle, WmUserSimConnect, null, 0);
            sc.OnRecvOpen += OnRecvOpen;
            sc.OnRecvQuit += OnRecvQuit;
            sc.OnRecvException += OnRecvException;
            sc.OnRecvSimobjectData += OnRecvSimobjectData;
            _simConnect = sc;
        }
        catch (COMException)
        {
            // Sim not running yet — stay Disconnected, the loop retries on its own timer.
            _simConnect = null;
            SetState(SimConnectionState.Disconnected);
        }
    }

    private void OnRecvOpen(SimConnect sender, SIMCONNECT_RECV_OPEN data)
    {
        foreach (var variable in SimVariableCatalog.KnownVariables)
        {
            sender.AddToDataDefinition(DataDefinition.Telemetry, variable.Name, variable.Units,
                SIMCONNECT_DATATYPE.FLOAT64, 0f, SimConnect.SIMCONNECT_UNUSED);
        }

        sender.RegisterDataDefineStruct<TelemetryData>(DataDefinition.Telemetry);
        sender.RequestDataOnSimObject(DataRequest.Telemetry, DataDefinition.Telemetry,
            SimConnect.SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_PERIOD.VISUAL_FRAME, 0, 0, 0, 0);

        // Aircraft title: checked once a second, but only delivered when it actually changed.
        sender.AddToDataDefinition(DataDefinition.Aircraft, "TITLE", null,
            SIMCONNECT_DATATYPE.STRING256, 0f, SimConnect.SIMCONNECT_UNUSED);
        sender.RegisterDataDefineStruct<AircraftInfo>(DataDefinition.Aircraft);
        sender.RequestDataOnSimObject(DataRequest.Aircraft, DataDefinition.Aircraft,
            SimConnect.SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_PERIOD.SECOND,
            SIMCONNECT_DATA_REQUEST_FLAG.CHANGED, 0, 0, 0);

        SetState(SimConnectionState.Connected);
    }

    private unsafe void OnRecvSimobjectData(SimConnect sender, SIMCONNECT_RECV_SIMOBJECT_DATA data)
    {
        if (data.dwRequestID == (uint)DataRequest.Aircraft)
        {
            var title = ((AircraftInfo)data.dwData[0]).Title?.Trim();
            SetAircraftTitle(string.IsNullOrEmpty(title) ? null : title);
            return;
        }

        if (data.dwRequestID != (uint)DataRequest.Telemetry) return;

        var telemetry = (TelemetryData)data.dwData[0];
        var variables = SimVariableCatalog.KnownVariables;
        for (var i = 0; i < variables.Count; i++)
            _simData[variables[i].Name] = telemetry.Values[i];
    }

    private void SetAircraftTitle(string? title)
    {
        if (string.Equals(_aircraftTitle, title, StringComparison.Ordinal)) return;
        _aircraftTitle = title;
        AircraftChanged?.Invoke(title);
    }

    private void OnRecvQuit(SimConnect sender, SIMCONNECT_RECV data) => Disconnect();

    private void OnRecvException(SimConnect sender, SIMCONNECT_RECV_EXCEPTION data)
    {
        System.Diagnostics.Debug.WriteLine($"SimConnect exception: {data.dwException}");
    }

    private void Disconnect()
    {
        if (_simConnect is not null)
        {
            _simConnect.Dispose();
            _simConnect = null;
        }
        SetAircraftTitle(null); // no sim, no aircraft
        SetState(SimConnectionState.Disconnected);
    }

    private void SetState(SimConnectionState state)
    {
        _state = state;
        StateChanged?.Invoke(state);
    }
}

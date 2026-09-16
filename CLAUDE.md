# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

icsmooi is an Avalonia (.NET 10) system for building and running force-feedback (FFB) profiles for flight simulators (MSFS2020/2024). Users wire together a node graph — SimConnect telemetry inputs, math/logic/curve nodes, FFB effect outputs — on a visual canvas, and an evaluation engine runs that graph in real time to drive real force-feedback hardware.

There is no README; this project is being built in phases (see commit history and `Docs`-free in-code phase comments). A detailed implementation plan for the current body of work (real SimConnect telemetry, real DirectInput FFB output, new node types, shared visual design) lives at `C:\Users\crist\.claude\plans\msfs2020-and-msfs2024-do-golden-meerkat.md` — read it before starting work on any phase beyond 4, since it records the reasoning behind decisions below, not just the "what."

## Solution structure

Four projects under `/src`, referenced from `icsmooi.slnx`:

- **`icsmooi.Core`** (class library) — `Models/`, `Engine/`, `Services/`. All graph/profile/evaluation logic and (from Phase 5 onward) the SimConnect telemetry client and DirectInput hardware-output services. References `Avalonia` (base only, not `Avalonia.Desktop`) and `NodifyM.Avalonia` directly on the node/pin model types (`NodeViewModel : INodePosition`, `PinViewModel : ConnectorViewModelBase`) — this is a deliberate, accepted trade-off: `icsmooi.Runtime` inherits an unused `NodifyM.Avalonia.dll` in its output folder (inert disk weight, never instantiated/rendered) rather than the larger refactor of introducing plain DTOs + an Editor-side translation layer. Do not "clean this up" unprompted.
- **`icsmooi.Editor`** (WinExe) — the node-graph authoring app (`Views/`, `ViewModels/`, `ViewLocator.cs`, `App.axaml`). NodifyM.Avalonia-dependent canvas UI. **Never acquires real FFB hardware** — it only shows simulated magnitude/parameter readouts (the `LastMagnitude`-style pattern) while authoring. Real telemetry ("Connect to Sim" toggle, from Phase 5) is fine here; real DirectInput hardware output is not.
- **`icsmooi.Runtime`** (WinExe) — the lightweight background app: tray icon + a small status window (`Views/StatusWindow.axaml`), loads a saved profile and actually drives telemetry + FFB hardware. No `NodifyM.Avalonia` reference in its own code (only inherited transitively via Core, per above) — never render a graph canvas here. This is the *only* app that wires up the DirectInput effect manager to `FfbEngineService`'s hardware-output event.
- **`icsmooi.Shared.Ui`** (resource-only class library) — shared Avalonia design tokens (`Styles/Tokens.axaml`), merged into both exe projects' `App.axaml` so they present a consistent look despite being separate processes. Currently a placeholder; populated in the "visual design system" phase.

Namespaces were left as `icsmooi.Models`/`icsmooi.Engine`/`icsmooi.Services`/`icsmooi.ViewModels`/`icsmooi.Views` regardless of which project a file lives in — they don't need to match assembly names, and keeping them unchanged avoided a mechanical rename across every file during the project split.

## Commands

```powershell
dotnet build icsmooi.slnx
dotnet run --project src/icsmooi.Editor/icsmooi.Editor.csproj
dotnet run --project src/icsmooi.Runtime/icsmooi.Runtime.csproj
```

No test project exists yet.

## Architecture

### Node graph editor (NodifyM.Avalonia)

The canvas is built on the `NodifyM.Avalonia` library. `MainWindowViewModel` (in `icsmooi.Editor`) derives from `NodifyEditorViewModelBase`, which supplies the `Nodes`/`Connections` collections and drag/connect gestures used directly by the XAML in `Views/MainWindow.axaml`.

There are **two parallel representations of the graph** that must be kept in sync manually:
- `MainWindowViewModel.Nodes` / `.Connections` — the live UI-facing collections NodifyEditor renders and manipulates.
- `MainWindowViewModel.Profile` (an `FfbProfile`) — the serializable model (`Profile.Nodes` / `Profile.Connections`), persisted to disk.

Every mutation (add node, connect, disconnect, delete, load) touches both sides. See `Connect`, `DisconnectConnector`, `DeleteSelection`, and `LoadProfile` in `src/icsmooi.Editor/ViewModels/MainWindowViewModel.cs` — when adding new graph-mutating operations, follow this same dual-update pattern rather than assuming one collection drives the other.

### Node model (icsmooi.Core/Models/)

`NodeViewModel` is the abstract base (`Id`, `X`/`Y`, `Inputs`/`Outputs` pin collections). Concrete node types (growing set — see the plan doc for the full catalog being added: logic/comparison, curve/shaping, and typed FFB effect-output nodes):
- `SimConnectNodeViewModel` — one output pin, reads a named variable from sim data.
- `MathNodeViewModel` — two input pins (`A`, `B`), one output; evaluates an NCalc `Expression` string (e.g. `[A] + [B]`) where pin `Title`s become NCalc parameter names.
- `FfbOutputNodeViewModel` — one input pin (`Magnitude`); today a placeholder terminal node (its `EffectType` dropdown is cosmetic) being replaced by typed effect-output node types (`ConstantForceOutputNodeViewModel`, `ConditionOutputNodeViewModel`, `PeriodicOutputNodeViewModel`, `RampForceOutputNodeViewModel`).

`PinViewModel` wraps `NodifyM.Avalonia`'s `ConnectorViewModelBase`; `IsInput` is set once via `init` and mirrors the `Flow` enum. `Anchor` (screen position) is `[JsonIgnore]` — runtime-only, never persisted.

Adding a new node type means: create a `NodeViewModel` subclass with its pins wired up in the constructor, register it with `[JsonDerivedType(...)]` on `NodeViewModel`, add a case to `GraphEvaluator.Evaluate`'s switch, and add an `Add<X>Node` command + palette entry in `MainWindowViewModel`/`MainWindow.axaml` (Editor only — Runtime never adds nodes, it only loads/runs saved profiles).

### Evaluation engine (icsmooi.Core/Engine/)

`GraphEvaluator` is a stateless, pure evaluator: given a snapshot of nodes/connections plus a `simData` dictionary, it topologically sorts the graph with Kahn's algorithm (returning `null` if a cycle is detected — the engine skips that tick rather than throwing), then evaluates nodes in dependency order, propagating values through a `pin ID → value` map after each node runs.

`FfbEngineService` drives this on a `System.Threading.Timer` (default 10ms / 100Hz). Key points:
- The graph snapshot is held in a `volatile` field, swapped atomically via `RefreshSnapshot` whenever the profile's nodes/connections change — the timer thread never sees a torn/partial graph.
- `SimData` is a `ConcurrentDictionary` — from Phase 5 onward, `SimConnectTelemetryService` writes into it continuously from its own background thread.
- **`OutputsUpdated` fires on the timer thread itself, not the UI thread** — `FfbEngineService` has no Avalonia-UI-thread dependency by design, since it's shared between the Editor and the headless Runtime app. Each host marshals to its own UI thread in its own subscriber (see `MainWindowViewModel.OnEngineOutputsUpdated`'s `Dispatcher.UIThread.Post` wrapper in the Editor). Don't reintroduce a `Dispatcher` call inside `FfbEngineService` itself.

When editing the graph while the engine is running, call `Engine.RefreshSnapshot(Profile)` — the engine won't otherwise see topology changes until the next explicit refresh.

### Serialization

`ProfileSerializerService` persists `FfbProfile` to `<MyDocuments>/{ProfileName}.icsmooi.json` via `System.Text.Json`. Polymorphism for node types is handled purely through `[JsonDerivedType]` attributes on `NodeViewModel` (no custom converter) — new node types must be registered there or they'll fail to round-trip. Connections are serialized as plain node/pin GUID pairs (`ConnectionViewModel`), not as live pin references; `MainWindowViewModel.LoadProfile` re-resolves GUIDs back into `PinViewModel` instances and rebuilds the live `Connections` collection on load.

### View binding

`ViewLocator` (in `icsmooi.Editor`) resolves views by naming convention: `Foo.ViewModels.BarViewModel` → `Foo.Views.BarView` (string replace on the full type name), falling back to a "Not Found" `TextBlock`. New view models need a matching view class following this convention to render.

### SimConnect telemetry (icsmooi.Core/Services/SimConnectTelemetryService.cs)

Vendored from the MSFS SDK — proprietary, not on NuGet: `lib/SimConnect/Microsoft.FlightSimulator.SimConnect.dll` (managed) + `lib/SimConnect/SimConnect.dll` (native), referenced only from `icsmooi.Core.csproj` via `HintPath`/`CopyToOutputDirectory`; both exe projects get the native DLL automatically through `ProjectReference` content propagation. Sourced from the user's sibling `TDX-Air-Mechanics` project, which already has a working SimConnect integration for MSFS.

`SimConnectTelemetryService` runs on its own `TaskCreationOptions.LongRunning` task: constructs `SimConnect` with a real native window handle (required by its constructor) but never pumps Win32 messages for it — it polls `ReceiveMessage()` manually in a `Thread.Sleep(16)` loop instead, exactly like TDX's own `SimConnectService.cs`. Two deltas from TDX: it auto-retries the connection on a timer (`Disconnected → Connecting → Connected` state machine, retried every 5s) instead of requiring a manual "Connect" click, and it requests `SIMCONNECT_PERIOD.VISUAL_FRAME` instead of TDX's `SECOND` for lower-latency FFB-relevant updates. **Known quirk**: constructing `SimConnect` when the sim isn't running doesn't always throw — it can also silently never raise `OnRecvOpen`. A `ConnectTimeout` (5s) treats "still Connecting after N seconds" the same as a thrown exception and retries, rather than getting stuck.

Telemetry lands directly in `FfbEngineService.SimData`, keyed by SimConnect variable name exactly as `SimConnectNodeViewModel.VariableName`/`SimVariableCatalog` use it — no intermediate DTO. `SimVariableCatalog.KnownVariables`' order is load-bearing: it must match `SimConnectTelemetryService`'s internal `TelemetryData` struct field order exactly, since SimConnect maps registered variables to struct fields positionally, not by name (the struct uses a `fixed double Values[SimVariableCatalog.Count]` buffer specifically to make this positional mapping explicit rather than relying on reflection's unordered `GetFields()`).

The Editor's "Connect to Sim" toolbar toggle (`MainWindowViewModel.ToggleSimConnectionCommand`) is test-run telemetry only, per the Editor/Runtime hardware boundary above — it feeds live values into `LastMagnitude`-style readouts, nothing more.

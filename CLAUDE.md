# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

icsmooi is an Avalonia (.NET 10) system for building and running force-feedback (FFB) profiles for flight simulators (MSFS2020/2024). Users wire together a node graph — SimConnect telemetry inputs, math/logic/curve nodes, FFB effect outputs — on a visual canvas, and an evaluation engine runs that graph in real time to drive real force-feedback hardware.

There is no README; this project is being built in phases (see commit history and `Docs`-free in-code phase comments). A detailed implementation plan for the current body of work (real SimConnect telemetry, real DirectInput FFB output, new node types, shared visual design) lives at `C:\Users\crist\.claude\plans\msfs2020-and-msfs2024-do-golden-meerkat.md` — read it before starting work on any phase beyond 4, since it records the reasoning behind decisions below, not just the "what."

## Solution structure

Four projects under `/src`, referenced from `icsmooi.slnx`:

- **`icsmooi.Core`** (class library) — `Models/`, `Engine/`, `Services/`. All graph/profile/evaluation logic and (from Phase 5 onward) the SimConnect telemetry client and DirectInput hardware-output services. References `Avalonia` (base only, not `Avalonia.Desktop`) and `NodifyM.Avalonia` directly on the node/pin model types (`NodeViewModel : INodePosition`, `PinViewModel : ConnectorViewModelBase`) — this is a deliberate, accepted trade-off: `icsmooi.Runtime` inherits an unused `NodifyM.Avalonia.dll` in its output folder (inert disk weight, never instantiated/rendered) rather than the larger refactor of introducing plain DTOs + an Editor-side translation layer. Do not "clean this up" unprompted.
- **`icsmooi.Editor`** (WinExe) — the node-graph authoring app (`Views/`, `ViewModels/`, `ViewLocator.cs`, `App.axaml`). NodifyM.Avalonia-dependent canvas UI. **Never acquires real FFB hardware** — it only shows simulated magnitude/parameter readouts (the `LastMagnitude`-style pattern) while authoring. Real telemetry ("Connect to Sim" toggle, from Phase 5) is fine here; real DirectInput hardware output is not.
- **`icsmooi.Runtime`** (WinExe) — the lightweight background app: tray icon + a small status window (`Views/StatusWindow.axaml` + `ViewModels/StatusWindowViewModel.cs`), loads a saved profile and actually drives telemetry + FFB hardware. No `NodifyM.Avalonia` reference in its own code (only inherited transitively via Core, per above) — never render a graph canvas here. This is the *only* app that wires up `FfbEffectManager` to `FfbEngineService.HardwareOutputsUpdated`, and only once the user explicitly opts in — see the "Effects safety gate" note below.
- **`icsmooi.Shared.Ui`** (resource-only class library) — shared Avalonia design tokens (`Styles/Tokens.axaml`), merged into both exe projects' `App.axaml` so they present a consistent look despite being separate processes — see "Visual design system" below.

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

`NodeViewModel` is the abstract base (`Id`, `X`/`Y`, `Inputs`/`Outputs` pin collections). Concrete node types:
- `SimConnectNodeViewModel` — one output pin, reads a named variable from sim data.
- `MathNodeViewModel` — two input pins (`A`, `B`), one output; evaluates an NCalc `Expression` string (e.g. `[A] + [B]`) where pin `Title`s become NCalc parameter names.
- `FfbOutputNodeViewModel` — one input pin (`Magnitude`); a legacy placeholder terminal node kept for backward compatibility and simple test profiles (its `EffectType` dropdown is cosmetic, it never touches real hardware). New profiles wanting real hardware output should use the typed nodes below instead.
- `ConstantForceOutputNodeViewModel` (`Magnitude` → real DirectInput Constant Force), `ConditionOutputNodeViewModel` (`PositiveCoefficient`,`NegativeCoefficient`,`Offset`,`DeadBand`,`Saturation` → real DirectInput Spring/Damper/Inertia/Friction, picked via a `ConditionKind` property), `PeriodicOutputNodeViewModel` (`Magnitude`,`Period`,`Phase`,`Offset` → real Sine/Square/Triangle/SawtoothUp/SawtoothDown, picked via a `Waveform` property — there is no single "Periodic" DirectInput effect GUID, each waveform is its own GUID), `RampForceOutputNodeViewModel` (`StartMagnitude`,`EndMagnitude`,`Duration` → real DirectInput Ramp Force, the one hardware-output node whose `Duration` is finite rather than "play until stopped"). All four carry `DeviceInstanceGuid`/`AxisIndex`/`Gain` properties for hardware targeting and a `[JsonIgnore]` live readout.
- `ComparisonNodeViewModel` (`A`,`B` → `Result`, `Operator` enum `>`/`<`/`>=`/`<=`/`==`/`!=`), `LogicNodeViewModel` (`A`,`B` → `Result`, `Operator` enum AND/OR/NOT/XOR — `B` stays present but unused for NOT rather than being dynamically hidden), `SelectNodeViewModel` (`Condition`,`IfTrue`,`IfFalse` → `Result`) — all keep the graph's all-`double` wire convention (nonzero = true, 1.0/0.0 out) rather than introducing a separate bool pin type.
- `ClampNodeViewModel` (`Value`,`Min`,`Max` → `Result`), `RangeMapNodeViewModel` (`Value`,`InMin`,`InMax`,`OutMin`,`OutMax` → `Result`, plus a `Clamp` bool property), `CurveNodeViewModel` (`Value` → `Result`, backed by an `ObservableCollection<CurvePoint>` piecewise-linear curve) — the general-purpose way to express a telemetry-to-effect formula (e.g. an airspeed-to-stiffness curve) visually instead of hardcoding it in C#. The Curve node's control points are edited via `Views/CurveEditorControl.cs`, a fully custom-rendered Avalonia `Control` (no XAML template) embedded directly in its `DataTemplate` — double-click empty space to add a point, double-click a point to remove it, drag to reshape. Its view bounds are fit to the point set once on attach/collection-change, not recomputed every render, so dragging a point doesn't make the view rescale out from under the pointer.

`PinViewModel` wraps `NodifyM.Avalonia`'s `ConnectorViewModelBase`; `IsInput` is set once via `init` and mirrors the `Flow` enum. `Anchor` (screen position) is `[JsonIgnore]` — runtime-only, never persisted.

Adding a new node type means: create a `NodeViewModel` subclass with its pins wired up in the constructor, register it with `[JsonDerivedType(...)]` on `NodeViewModel`, add a case to `GraphEvaluator.Evaluate`'s switch, and add an `Add<X>Node` command + palette entry in `MainWindowViewModel`/`MainWindow.axaml` (Editor only — Runtime never adds nodes, it only loads/runs saved profiles). For a node with fixed-name input pins and a single output (i.e. everything except `MathNodeViewModel`, which maps *all* its inputs generically into NCalc parameters), reuse `GraphEvaluator`'s `GetPinValue(node, title, wireValues)`/`SetOutputValue(node, value, wireValues)` helpers rather than re-deriving pin lookup.

### Evaluation engine (icsmooi.Core/Engine/)

`GraphEvaluator` is a stateless, pure evaluator: given a snapshot of nodes/connections plus a `simData` dictionary, it topologically sorts the graph with Kahn's algorithm (returning `null` if a cycle is detected — the engine skips that tick rather than throwing), then evaluates nodes in dependency order, propagating values through a `pin ID → value` map after each node runs.

`FfbEngineService` drives this on a `System.Threading.Timer` (default 10ms / 100Hz). Key points:
- The graph snapshot is held in a `volatile` field, swapped atomically via `RefreshSnapshot` whenever the profile's nodes/connections change — the timer thread never sees a torn/partial graph.
- `SimData` is a `ConcurrentDictionary` — from Phase 5 onward, `SimConnectTelemetryService` writes into it continuously from its own background thread.
- **`OutputsUpdated` fires on the timer thread itself, not the UI thread** — `FfbEngineService` has no Avalonia-UI-thread dependency by design, since it's shared between the Editor and the headless Runtime app. Each host marshals to its own UI thread in its own subscriber (see `MainWindowViewModel.OnEngineOutputsUpdated`'s `Dispatcher.UIThread.Post` wrapper in the Editor). Don't reintroduce a `Dispatcher` call inside `FfbEngineService` itself.
- **`HardwareOutputsUpdated`** fires alongside `OutputsUpdated` on the same tick, carrying `IReadOnlyDictionary<Guid, EffectOutputParams>` — richer per-effect-type parameters (e.g. a Condition node's 5 coefficients) that a single scalar magnitude can't express. Only `icsmooi.Runtime`'s `FfbEffectManager` is meant to subscribe to this (not yet wired — that's Phase 8); the Editor only ever consumes `OutputsUpdated`.

`GraphEvaluator.Evaluate` returns `NodeOutputResult(Magnitudes, EffectParams)` rather than a bare dictionary — `Magnitudes` is populated for every output-shaped node (including the legacy `FfbOutputNodeViewModel`) for UI readouts, while `EffectParams` is populated only for node types that actually drive hardware (currently `ConstantForceOutputNodeViewModel`, `ConditionOutputNodeViewModel`).

When editing the graph while the engine is running, call `Engine.RefreshSnapshot(Profile)` — the engine won't otherwise see topology changes until the next explicit refresh.

### DirectInput hardware output (icsmooi.Core/Services/DirectInput/)

`FfbDeviceManager` wraps one `Vortice.DirectInput.IDirectInput8`. `EnumerateDevices()` is safe to call from the Editor (read-only, used to populate the device picker's dropdown) — `AcquireDevice`/effect creation is Runtime-exclusive per the hardware boundary above. Each device's FFB axis offsets are discovered once at acquisition and cached, rather than re-queried by every effect (a deliberate fix versus TDX-Air-Mechanics, where every effect class re-ran axis discovery independently).

`FfbEffectManager` keeps one long-lived `IDirectInputEffect` per hardware-output node (keyed by node `Id`), created once via `device.CreateEffect(...)` and updated in place every tick via `effect.SetParameters(params, EffectParameterFlags.TypeSpecificParameters | EffectParameterFlags.Start)` — never recreated per tick.

**Vortice.DirectInput API notes** (discovered by reflecting the installed package — its exact shape isn't obvious from SharpDX-parity claims alone):
- No bare `Effect` class — effect handles are `IDirectInputEffect`, created via `IDirectInputDevice8.CreateEffect(Guid, EffectParameters)`.
- `EffectParameters.Parameters` is typed `TypeSpecificParameters`, an abstract base class — the concrete types you actually construct are its public subclasses `ConstantForce` (`Magnitude` int), `ConditionSet` (`Conditions` — a `Condition[]`, one struct per axis, each with `Offset`/`PositiveCoefficient`/`NegativeCoefficient`/`PositiveSaturation`/`NegativeSaturation`/`DeadBand` int fields), `PeriodicForce` (`Magnitude`/`Offset`/`Phase`/`Period` int properties), `RampForce` (`Start`/`End` int properties), `CustomForce` (unused so far). There is **no** separate `Conditions` property on `EffectParameters` itself — assign the `ConditionSet` (or any of the others) through `.Parameters` like any other effect type.
- `EffectParameters.Gain` (not `ForceFeedbackGain`), `.Flags`, `.Duration` etc. are plain fields, not properties.
- **`EffectParameters.Duration` must be set inside each `Build*` method, not centrally in `FfbEffectManager.TryUpdate`** — continuous effects (ConstantForce/Condition/Periodic) use `InfiniteDuration` ("play until stopped"), but Ramp Force needs its own finite duration. An earlier draft set `Duration = int.MaxValue` unconditionally after calling the builder, silently stomping Ramp's real duration — if you add a new effect type, decide its duration inside its own `Build*` method.
- Device acquisition uses `CooperativeLevel` (not `CooperativeLevelFlags`) for `SetCooperativeLevel`'s flags argument.
- All ±10000-range DirectInput integer units are produced by `FfbEffectManager.ToDirectInputUnits(double)` from the graph's normalized `[-1, 1]`-ish doubles — keep new effect types going through this same helper rather than hand-rolling the scaling. `Period`/`Duration` (seconds → microseconds) and `Phase` (normalized `[0,1)` cycle → hundredths-of-a-degree) have their own dedicated helpers for the same reason.
- Verified against real hardware: `FfbDeviceManager.EnumerateDevices()` correctly detects an attached FFB device (product name + instance GUID) with zero acquisition. `AcquireDevice`/`CreateEffect` have not yet been exercised against real hardware in this repo — deliberately: the "Effects" safety gate below has been kept off throughout development.
- Verified against a live running MSFS instance: the Editor's "Connect to Sim" toggle reaches `SimConnectionState.Connected` (real `OnRecvOpen` handshake, all 16 `AddToDataDefinition` calls + `RegisterDataDefineStruct`/`RequestDataOnSimObject` succeeding), and a `SimConnectNodeViewModel` wired directly to an `FfbOutputNodeViewModel` showed real, continuously-updating `AIRSPEED INDICATED` values end-to-end through `GraphEvaluator` into the UI readout.

### Runtime app (icsmooi.Runtime)

`StatusWindowViewModel` drives the whole lightweight app: on window open it checks `RuntimeSettingsService` (a tiny `%LOCALAPPDATA%\icsmooi\runtime-settings.json` file) for a last-used profile path and auto-loads it — no user action needed on a normal (re)start. Loading a profile immediately starts `SimConnectTelemetryService` and `FfbEngineService` (unlike the Editor, Runtime has no manual "Connect"/"Start Engine" toggles — it's meant to just run).

**Effects safety gate**: loading a profile and running the engine only ever produces simulated magnitude readouts — `FfbEffectManager` is not created, and no device is acquired, until the user flips the status window's "Effects" `ToggleSwitch` on (default **off**). Flipping it off disposes `FfbEffectManager` immediately, tearing down every live effect. `FfbEngineService.HardwareOutputsUpdated` fires unconditionally every tick regardless of this toggle; `StatusWindowViewModel.OnHardwareOutputsUpdated` is what actually gates hardware access, by checking whether `_effectManager` (a `volatile` field) is non-null. Don't wire real effect creation anywhere that bypasses this toggle.

`_effectManager` is read on `FfbEngineService`'s timer thread and written on the UI thread (when the toggle flips) — it's `volatile` and captured into a local before use specifically to avoid a null-reference race; a narrow dispose-while-in-flight race is an accepted, documented limitation rather than something solved with locking in this pass.

Profile loading (`Views/StatusWindow.axaml.cs`'s `OnLoadProfileClicked`) uses Avalonia's `IStorageProvider.OpenFilePickerAsync`, filtered to `*.icsmooi.json`, defaulting to the Documents folder — the same location `ProfileSerializerService` saves to from the Editor.

### Serialization

`ProfileSerializerService` persists `FfbProfile` to `<MyDocuments>/{ProfileName}.icsmooi.json` via `System.Text.Json`. Polymorphism for node types is handled purely through `[JsonDerivedType]` attributes on `NodeViewModel` (no custom converter) — new node types must be registered there or they'll fail to round-trip. Connections are serialized as plain node/pin GUID pairs (`ConnectionViewModel`), not as live pin references; `MainWindowViewModel.LoadProfile` re-resolves GUIDs back into `PinViewModel` instances and rebuilds the live `Connections` collection on load.

### View binding

`ViewLocator` (in `icsmooi.Editor`) resolves views by naming convention: `Foo.ViewModels.BarViewModel` → `Foo.Views.BarView` (string replace on the full type name), falling back to a "Not Found" `TextBlock`. New view models need a matching view class following this convention to render.

### SimConnect telemetry (icsmooi.Core/Services/SimConnectTelemetryService.cs)

Vendored from the MSFS SDK — proprietary, not on NuGet: `lib/SimConnect/Microsoft.FlightSimulator.SimConnect.dll` (managed) + `lib/SimConnect/SimConnect.dll` (native), referenced only from `icsmooi.Core.csproj` via `HintPath`/`CopyToOutputDirectory`; both exe projects get the native DLL automatically through `ProjectReference` content propagation. Sourced from the user's sibling `TDX-Air-Mechanics` project, which already has a working SimConnect integration for MSFS.

`SimConnectTelemetryService` runs on its own `TaskCreationOptions.LongRunning` task: constructs `SimConnect` with a real native window handle (required by its constructor) but never pumps Win32 messages for it — it polls `ReceiveMessage()` manually in a `Thread.Sleep(16)` loop instead, exactly like TDX's own `SimConnectService.cs`. Two deltas from TDX: it auto-retries the connection on a timer (`Disconnected → Connecting → Connected` state machine, retried every 5s) instead of requiring a manual "Connect" click, and it requests `SIMCONNECT_PERIOD.VISUAL_FRAME` instead of TDX's `SECOND` for lower-latency FFB-relevant updates. **Known quirk**: constructing `SimConnect` when the sim isn't running doesn't always throw — it can also silently never raise `OnRecvOpen`. A `ConnectTimeout` (5s) treats "still Connecting after N seconds" the same as a thrown exception and retries, rather than getting stuck.

Telemetry lands directly in `FfbEngineService.SimData`, keyed by SimConnect variable name exactly as `SimConnectNodeViewModel.VariableName`/`SimVariableCatalog` use it — no intermediate DTO. `SimVariableCatalog.KnownVariables`' order is load-bearing: it must match `SimConnectTelemetryService`'s internal `TelemetryData` struct field order exactly, since SimConnect maps registered variables to struct fields positionally, not by name (the struct uses a `fixed double Values[SimVariableCatalog.Count]` buffer specifically to make this positional mapping explicit rather than relying on reflection's unordered `GetFields()`).

The Editor's "Connect to Sim" toolbar toggle (`MainWindowViewModel.ToggleSimConnectionCommand`) is test-run telemetry only, per the Editor/Runtime hardware boundary above — it feeds live values into `LastMagnitude`-style readouts, nothing more.

### Visual design system (icsmooi.Shared.Ui/Styles/Tokens.axaml)

A single dark navy/slate palette, not an OS-theme-following one: both apps set `RequestedThemeVariant="Dark"` explicitly and there is no light-theme variant. `Tokens.axaml` defines two families of resources, merged into both `App.axaml`s:

- **App-chrome tokens** — `Icsmooi.Background`/`Surface`/`SurfaceAlt`/`Border`/`Accent`/`Text`/`TextMuted`/`Success`/`Warning`/`Error` (brushes; each has a matching `*Color` Color resource). Used throughout `MainWindow.axaml` and `StatusWindow.axaml` instead of hardcoded hex — if you add new chrome, use these rather than introducing another hex literal.
- **Per-node-category accent tokens** — `Icsmooi.Category.Input` (blue) / `MathLogic` (green) / `Shaping` (amber) / `HardwareOutput` (red). Every node `DataTemplate` in `Views/NodeTemplates.axaml` leads its body with a 3px `Border` tinted by its category's token — NodifyM.Avalonia's `Node` control has no built-in per-instance accent-color property, so this is hand-rolled per template rather than a shared style setter. Keep new node types' stripes consistent with their category when adding them.

`Tokens.axaml` also **overrides a handful of NodifyM.Avalonia's own `Dark.axaml` color keys** (`Node.BackgroundColor`, `Node.HeaderColor`, `Node.FooterColor`, `Node.BorderColor`, `NodifyEditor.BackgroundColor`, `GridLinesColor`, `ItemContainer.SelectedColor`) so the node canvas matches this palette instead of NodifyM's generic charcoal default — everything else from NodifyM's `Dark.axaml`/`Brushes.axaml` (connector colors, connection stroke, selection rectangle, etc.) is kept as-is. **Merge order matters**: `App.axaml` merges `NodifyM.Avalonia`'s `Dark.axaml` and `Brushes.axaml` *before* `icsmooi.Shared.Ui`'s `Tokens.axaml`, so these overrides win (last-declared-key-wins in Avalonia's merged-dictionary resolution). Before this phase, `App.axaml` only referenced NodifyM's `Styles/ControlStyles.axaml` — the `Dark`/`Brushes` `ResourceDictionary` files (found by checking the actual GitHub source tree at `Maklith/NodifyM.Avalonia/NodifyM.Avalonia/ResourceDictionaries/`, since the compiled package only exposes a single opaque `!AvaloniaResources` blob at runtime, not enumerable individual paths) weren't loaded at all, which is why nodes previously rendered with mismatched light Fluent-theme colors floating on the app's dark canvas.

All 12 node `DataTemplate`s live in `Views/NodeTemplates.axaml` (a `ResourceDictionary`, merged into `MainWindow.axaml` via `<ResourceInclude Source="avares://icsmooi.Editor/Views/NodeTemplates.axaml">`), not inline in `MainWindow.axaml` — moved here once the node catalog grew past a handful of types. **Gotcha**: bindings like `{Binding $parent[Window].DataContext.AvailableDevices}` (used by the four hardware-output node templates' device pickers) relied on `MainWindow.axaml`'s root `x:DataType` for Avalonia's compiled-bindings feature to resolve the ambient `Window.DataContext` type; once those templates moved to a separate file with no such root type context, compilation failed with `AVLN2000: Unable to resolve property ... on type 'System.Object'`. Fixed by adding `x:CompileBindings="False"` on those specific `ComboBox` elements, falling back to classic reflection bindings — a smaller, more contained fix than threading a `DataType` hint through the cross-file binding path.

The sidebar (`MainWindow.axaml`) is a `ScrollViewer`-wrapped, category-grouped list (260px felt too narrow once every button had to fit an emoji + label — settled on 320px) rather than the original flat non-scrolling 480px list — narrower than before despite the wider category headers, since the categorized layout is denser.

<!-- TODO: add a screenshot of the node editor and the Runtime status window here -->

# icsmoi

A visual node-graph editor and runtime for building custom force-feedback (FFB)
profiles for Microsoft Flight Simulator (2020/2024) — wire together simulator
telemetry, math/logic, and shaping nodes on a canvas, connect them to real
DirectInput force-feedback effects, and run the result in real time on your
own hardware. No coding required to build a profile.

> **⚠️ This software drives real force-feedback hardware.** A misconfigured
> effect can push your controller hard. Read
> [`user_manual/08-safety.md`](user_manual/08-safety.md) before your first
> session, and always start at low gain. icsmoi is provided **as is, with no
> warranty** — see [`LICENSE`](LICENSE).

## What it does

- **Editor** — a node-graph canvas: drop down SimConnect telemetry, joystick
  input, math/logic, curve-shaping, and hardware-output nodes, wire them
  together, and save the result as a profile.
- **Runtime** — the lightweight app you actually run: a tray icon + small
  status window holding up to 4 profile slots, hot-reloading them as you edit,
  and remembering which aircraft/livery should load which profile.
- **Real hardware output** — Constant Force, Condition (spring/damper/inertia/
  friction), Periodic (sine/square/triangle/sawtooth), and Ramp Force effects
  via DirectInput, targeting a specific force axis or all of them.
- **Real MSFS telemetry** — live simulation variables (airspeed, accelerations,
  control positions, and more) via SimConnect.
- **A safety gate** — the Runtime only ever produces simulated readouts until
  you explicitly flip the "Effects" toggle on.

See the [Changelog](CHANGELOG.md) for what shipped in each release.

## Requirements

- Windows 10/11 (x64).
- A DirectInput game controller — a force-feedback device to feel real
  effects, or any controller if you only want to read its axes/buttons into a
  profile.
- Microsoft Flight Simulator 2020 or 2024, if you want telemetry-driven
  profiles (not required for controller-only profiles).

## Installing

Download the latest installer (`icsmoi-<version>-win-x64.msi`) from the
[Releases](../../releases) page and run it. See
[`user_manual/01-installation.md`](user_manual/01-installation.md) for details,
including the first-run "unknown publisher" prompt (the installer isn't
code-signed) and where icsmoi keeps its data.

Then read the [Quick Start](user_manual/02-quick-start.md) to load an example
profile and feel it work.

## Building from source

```powershell
dotnet build icsmoi.slnx
dotnet run --project src/icsmoi.Runtime/icsmoi.Runtime.csproj
```

Building `icsmoi.Core` requires two proprietary MSFS SDK files that aren't
included in this repository — see
[`lib/SimConnect/README.md`](lib/SimConnect/README.md) for how to obtain your
own copy before building. Full contributor setup is in
[`CONTRIBUTING.md`](CONTRIBUTING.md).

## Learn more

The full [user manual](user_manual/README.md) covers installation, the
Runtime app, the Editor, a complete node reference, and hands-on tutorials for
building your own effects from the [example profiles](examples/README.md).

## License

icsmoi is licensed under the [MIT License](LICENSE). It bundles or depends on
several open-source packages and one proprietary SDK — see
[`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md) for details.

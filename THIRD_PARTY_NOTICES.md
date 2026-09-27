# Third-party notices

icsmoi is licensed under the [MIT License](LICENSE). It builds on the open-source
packages below, each under its own license, and on one proprietary SDK that is
**not** covered by icsmoi's license and is **not** distributed with this repository.

## Open-source dependencies

| Package | License | Used for |
|---|---|---|
| [Avalonia](https://github.com/AvaloniaUI/Avalonia) | MIT | Cross-platform UI framework (windows, controls, rendering) |
| [NodifyM.Avalonia](https://github.com/Maklith/NodifyM.Avalonia) | MIT | Node-graph canvas control the Editor's node/pin model is built on |
| [Vortice.DirectInput](https://github.com/amerkoleci/Vortice.Windows) | MIT | DirectInput bindings — real FFB hardware output and joystick/game-controller input |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | MIT | MVVM helpers (`ObservableObject`, `[ObservableProperty]`, etc.) |
| [NCalc](https://github.com/ncalc/ncalc) | MIT | Expression evaluation for the Math node |

Each package's full license text is available at its project page above, and is
also reproduced (or fetched) via NuGet's package license metadata when you
restore the solution.

## Proprietary SDK — not covered by this license, not redistributed here

`icsmoi.Core` references two files from the **Microsoft Flight Simulator SDK**:

- `Microsoft.FlightSimulator.SimConnect.dll` (managed client)
- `SimConnect.dll` (native client)

These are Microsoft/Asobo software governed by the Microsoft Flight Simulator
SDK's own license terms (the SDK EULA), not by icsmoi's MIT license. **This
source repository does not contain these files** — see
[`lib/SimConnect/README.md`](lib/SimConnect/README.md) for how to obtain your
own copy from the SDK if you're building icsmoi from source. Any compiled
release build of icsmoi that includes real SimConnect telemetry support was
built with a copy of these files obtained separately, under the SDK's own
terms, by the person who produced that build.

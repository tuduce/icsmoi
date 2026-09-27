# SimConnect SDK files (not included)

`icsmoi.Core` reads real MSFS telemetry through Microsoft's SimConnect client,
which is part of the Microsoft Flight Simulator SDK — proprietary, EULA-governed,
and not distributed on NuGet or in this repository (see
[`THIRD_PARTY_NOTICES.md`](../../THIRD_PARTY_NOTICES.md)).

To build icsmoi from source, place these two files in this folder yourself:

- `Microsoft.FlightSimulator.SimConnect.dll` (managed client)
- `SimConnect.dll` (native client)

## Where to get them

1. In Microsoft Flight Simulator (2020 or 2024): **Options → General → Developers**
   → enable Developer Mode, which installs the SDK, **or** install the standalone
   SDK from the in-sim Developer menu / Microsoft's flight simulator developer
   site.
2. In the installed SDK, look under a path similar to:
   - `...\MSFS SDK\SimConnect SDK\lib\managed\Microsoft.FlightSimulator.SimConnect.dll`
   - `...\MSFS SDK\SimConnect SDK\lib\SimConnect.dll`
   (the exact folder layout has shifted slightly between SDK versions — search
   the installed SDK folder for these two filenames if the path above doesn't
   match).
3. Copy both files into this folder (`lib/SimConnect/`).

Once both files are present, `dotnet build icsmoi.slnx` picks them up
automatically via the `<Reference>`/`<None>` entries in
`src/icsmoi.Core/icsmoi.Core.csproj`.

Without these files, `icsmoi.Core` (and therefore the Editor and Runtime) will
fail to build — SimConnect telemetry is not optional at compile time in the
current codebase.

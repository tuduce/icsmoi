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

## On the release self-hosted runner specifically

Do **not** place the files directly in this folder on the runner machine.
`actions/checkout` runs with its default `clean: true`, which deletes
gitignored files (this folder included) before every checkout — anything
placed here would be wiped out on the very next release run.

Instead, place both files once in a fixed folder outside any repo checkout,
then point the release workflow at it via a **repository variable** (not
hardcoded in the workflow, so this public file doesn't reveal a path on your
machine): **Settings → Secrets and variables → Actions → Variables → New
repository variable**, name `SIMCONNECT_SDK_DIR`, value = that folder's path
on the runner (e.g. `C:\actions-runner\simconnect-sdk`).
`.github/workflows/release.yml` copies the two files from there into this
folder at the start of each release build, and fails with a clear message if
the variable isn't set or the files aren't found.

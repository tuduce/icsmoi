# Contributing to icsmoi

## Building from source

Prerequisites:

- Windows, with the .NET 10 SDK.
- The two proprietary MSFS SimConnect SDK files, which are **not** included in
  this repository — see [`lib/SimConnect/README.md`](lib/SimConnect/README.md)
  for how to obtain and place them. `icsmoi.Core` will not build without them.

```powershell
dotnet build icsmoi.slnx
dotnet run --project src/icsmoi.Editor/icsmoi.Editor.csproj
dotnet run --project src/icsmoi.Runtime/icsmoi.Runtime.csproj
```

Building the Runtime also builds and bundles the Editor into
`<Runtime output>\Editor\`, so `dotnet run --project src/icsmoi.Runtime/...`
is enough for its "Edit" buttons to work locally.

There is no automated test suite yet — please exercise the app manually
(build, run, load a profile, and if you touched anything effect-related,
read [`user_manual/08-safety.md`](user_manual/08-safety.md) before testing
against real hardware).

## Architecture and conventions

[`.claude/CLAUDE.md`](.claude/CLAUDE.md) is the authoritative internal
reference for this codebase: solution structure, the node/pin model, the
evaluation engine, DirectInput/SimConnect integration details, and the shared
visual design system. Read the relevant section before touching an area you
haven't worked in before — it captures a lot of hard-won detail (hardware
quirks, gotchas, why things are structured the way they are) that isn't
obvious from the code alone.

## Regenerating the example profiles

If you change a node/pin model shape, the files under [`examples/`](examples/)
may need regenerating:

```powershell
dotnet run --project tools/ExampleProfileGenerator
```

This uses the real model classes and serializer, so the output is guaranteed
to load correctly rather than being hand-edited JSON.

## Pull requests

- Keep changes focused; unrelated cleanup makes a PR harder to review.
- If you're adding a new node type, follow the existing pattern described in
  `.claude/CLAUDE.md`'s "Node model" section (register it with
  `[JsonDerivedType]`, add it to `GraphEvaluator.Evaluate`, add an Editor
  command + palette entry + template).
- Describe what you tested, especially for anything touching real hardware
  output.

## Cutting a release

Release builds are maintainer-only: the tag-triggered GitHub Actions workflow
runs on a self-hosted runner that has the proprietary SimConnect SDK files and
WiX Toolset installed, since GitHub-hosted runners can't have either. If
you're not the maintainer, open a PR and let them handle tagging a release.

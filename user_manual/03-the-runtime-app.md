# 3. The Runtime app

The Runtime is the app you actually run day to day — a tray icon plus a small
status window. Unlike the Editor, it never needs you to click "Connect" or
"Start": once it's open, it connects to the sim and your controllers on its
own and keeps running.

## The status window

From top to bottom:

- **Header** — the icsmoi wordmark and a colored dot showing the simulator
  connection: grey (not connected), amber (trying to connect), green
  (connected).
- **Aircraft strip** — once connected to MSFS, shows the current aircraft's
  title/livery.
- **Warning banner** (only appears when relevant) — see "Aircraft
  association" below.
- **Four profile slots** — each is a card showing the loaded profile's name,
  or "Load profile…" if empty. The active slot (the one actually running) is
  marked with an accent ring and a filled dot.
- **Effects panel** — the safety-gated hardware toggle, described below.

## Slots

icsmoi holds up to **4 profiles at once**, one per slot, but only ever runs
one at a time (the *active* slot). Each slot card has small buttons:

- **Load profile…** (empty slots) — browse to an `.icsmoi.json` file.
- **Edit** (pencil) — opens that profile in the Editor for changes.
- **Replace** — swap in a different file.
- **Clear** — empty the slot.
- Clicking anywhere else on a card **activates** that slot.

## Hot reload

If you edit and save a profile in the Editor while it's loaded in a slot,
the Runtime picks up the change automatically within about a second — no
restart needed. If your edit introduces a mistake, the previous, working
version keeps running and the slot shows a load-error message instead of
silently breaking.

## Aircraft → profile association

Once connected to MSFS, icsmoi notices which aircraft (including the specific
livery) you're flying. If you manually pick a slot while flying a given
aircraft, icsmoi remembers "this aircraft → this profile" for next time:

- Next time you fly that same aircraft/livery and its profile is loaded into
  one of your 4 slots, icsmoi **switches to it automatically**.
- If it's associated but isn't currently loaded into any slot, icsmoi shows a
  **warning banner** instead of guessing — click its **Load it** button to
  load that profile into a free slot and switch to it.
- Flying an aircraft with no association does nothing — your active slot
  stays as it is.

## The Effects toggle — the safety gate

This is the single most important control in icsmoi. **By default, and every
time you start icsmoi, hardware output is off.** With Effects off, everything
runs — telemetry, your graph, the numbers — but nothing ever reaches your
controller; you'd only see this in the Editor's simulated readouts.

Flipping **Effects** on is the one action that lets icsmoi create real
DirectInput effects on your real hardware. Flipping it back off immediately
tears every one of them down. If effect creation fails for some reason (wrong
device, driver issue, etc.), the panel shows the error message instead of
silently doing nothing.

**Always start a new or unfamiliar profile with Effects off, understand what
it will do, and only then switch Effects on** — see
[8. Safety](08-safety.md).

## If something goes wrong

Errors that don't fit in the status window's short message line are written
in full to:

```
%LOCALAPPDATA%\icsmoi\runtime-errors.log
```

Include the relevant lines from this file if you report a bug — see
[7. Troubleshooting](07-troubleshooting.md).

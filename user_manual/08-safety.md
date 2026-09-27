# 8. Safety

**icsmoi drives real force-feedback hardware.** A force-feedback stick can
push, resist, or vibrate with real physical force — enough to be startling,
and a badly configured effect (a huge magnitude, a runaway trim, a spring
with no limits) can push harder than you expect. Please read this before your
first session with real hardware, especially with a profile you didn't build
yourself.

## Before you switch Effects on

- **Start at low gain / low magnitude**, especially with any new or unfamiliar
  profile. It's much easier to notice "this should push harder" than to be
  surprised by a strong force.
- **Know what the profile does** before you trust it — skim its nodes in the
  Editor first, or start from one of the [example profiles](../examples/README.md)
  you understand.
- **Keep a light, ready grip** on the stick the first time you enable a new
  effect, rather than gripping tightly or letting go completely.

## How to stop it immediately

- Switch the status window's **Effects** toggle off — this tears down every
  active effect right away.
- Unplugging the device also stops everything, if that's faster to reach.

## Known limitations that affect safety

- A fast-changing effect value is throttled slightly for connection
  stability (see [7. Troubleshooting](07-troubleshooting.md)) — don't rely on
  icsmoi reacting to an *extremely* rapid input change (e.g. a very fast
  double-tap) within the same instant.
- The Editor never drives real hardware, by design — but that also means the
  first time a profile touches real force is when you load it into the
  Runtime and switch Effects on. Treat that moment with the same caution as
  the first time, even if you tested the logic extensively in the Editor.

## License and liability

icsmoi is provided under the MIT License, **"as is", without warranty of any
kind** — see [`LICENSE`](../LICENSE). You are responsible for testing any
profile (yours or someone else's) at low force before relying on it, and for
your own and others' safety while using real force-feedback hardware.

# Changelog

All notable changes to icsmoi are documented here. Versions follow a `YY.N`
scheme (the first release of 2026 is `26.1`, the second `26.2`, and so on),
tagged in git as `v26.1`, `v26.2`, etc.

## 26.1 - 2026-09-27

Initial public release.

### Added

- **Runtime app** — the app you actually run day to day: lives in the tray,
  holds 4 profile slots, hot-reloads a profile when you save it from the
  Editor, and remembers which aircraft (by title/livery) should load which
  profile.
- **Editor app** — a visual node-graph canvas for building force-feedback
  profiles: wire together inputs, math/logic, shaping, and hardware-output
  nodes without writing code.
- **Real MSFS telemetry** (MSFS 2020/2024, via SimConnect) — read live
  simulation variables (airspeed, accelerations, control surface positions,
  and more) into a profile.
- **Real DirectInput force-feedback output** — Constant Force, Condition
  (spring/damper/inertia/friction), Periodic (sine/square/triangle/sawtooth),
  and Ramp Force effects, driven onto real hardware axes.
- **Joystick / game-controller input** — read any DirectInput controller's
  axes and buttons into a profile, independent of whether it has force
  feedback.
- **A full node catalog** for shaping signals: Math, Comparison, Logic,
  Select, Clamp, Range Map, Curve (a draggable piecewise-linear curve
  editor), Integrator (for trim), and Edge Detector (for button presses).
- **A safety gate**: the Runtime never touches real FFB hardware until you
  explicitly turn on the "Effects" toggle — everything before that is
  simulated readouts only.
- **Example profiles** (see [`examples/`](examples/)) and a full
  [user manual](user_manual/README.md) covering installation, the node
  reference, and step-by-step tutorials.

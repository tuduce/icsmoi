# 4. The Editor

The Editor is where you build and edit profiles: a canvas where you place
**nodes** and wire them together. It opens either from a Runtime slot's pencil
(Edit) button, or standalone from the Start Menu to work on a profile that
isn't loaded anywhere yet.

**The Editor never drives real hardware**, even with a real force-feedback
device plugged in. Hardware-output nodes show a live readout of what *would*
be sent, so you can verify a profile's logic safely before trusting it to the
Runtime. This is deliberate — you can experiment freely here with zero risk
to yourself or your hardware.

## Layout

- **Toolbar** — the icsmoi wordmark, the profile name (click the pencil next
  to it to rename), a status message field (shows the latest save/load/sim
  message), and a few round icon toggles on the right:
  - **Connect to Sim** — makes the Editor request real MSFS telemetry, purely
    so SimConnect Input nodes show live numbers while you work. It does not
    touch any hardware.
  - **Start/Stop Engine** — runs your graph so node readouts update live
    (again, simulated output only).
- **Sidebar palette** — every node type, grouped by category (Input, Math &
  Logic, Shaping, Hardware Output). Click a node to add it to the canvas.
- **Canvas** — drag nodes around, drag from an output pin (right side of a
  node) to an input pin (left side) to wire them, click a node/wire and press
  Delete to remove it.

## Wiring and constants

Every input pin that isn't wired to anything shows a small editable number
field next to it — this is the constant value used while nothing feeds that
pin. The moment you wire something into that pin, the field disappears (the
wire's live value takes over); disconnect the wire and your stored constant
reappears. This means you can build and test a node in isolation (typing in
plausible numbers) before wiring it to a real input.

The one exception is **Math Compute**: its inputs have no field, because its
constants belong directly in the expression text (e.g. `[A] + 2`).

## The Curve node

Curve nodes are edited directly on the canvas: **double-click empty space**
inside the curve to add a point, **double-click an existing point** to remove
it, and **drag a point** to reshape the curve. The curve is a straight-line
(piecewise-linear) interpolation between points, sorted left to right, and
holds flat beyond the first/last point.

## Joystick Input rows

Each row on a Joystick Input node is one axis or button binding. Click a
row's field to start capturing: move the axis or press the button on your
controller within about 15 seconds and it's assigned; click again to cancel a
capture in progress. Use the **+ Add input** button to add more rows, and the
✕ next to a row to remove it.

## Hardware-output node settings

Every hardware-output node (Constant Force, Condition, Periodic, Ramp Force)
shares one settings block:

- **Device** — which physical DirectInput device this effect plays on.
- **Axis** — a specific force axis, or **All axes**.
- **Direction dial** — only shown for effect types where direction matters
  (not Condition — see [5. Node reference](05-node-reference.md)) and only
  when Axis is "All axes": click or drag on the dial to set the push
  direction (0° = straight ahead, clockwise); hold Shift to snap to 15°
  steps.
- **Gain** — an overall volume (0–10000) for that one effect, independent of
  the values flowing into it.

## Saving

- **Save** writes over the file the profile was last loaded/saved from (no
  dialog, once it has a path).
- **Save As** and **Open** always show the normal Windows file picker.
- The suggested filename always ends in `.icsmoi.json` — the Runtime's file
  picker only shows files with that exact suffix, so keep it.

# 5. Node reference

Every value in icsmoi's graph is a plain number. There's no separate "true/
false" type: by convention, **non-zero counts as true** and logic/comparison
nodes output exactly `1.0` (true) or `0.0` (false). Ranges noted below (like
`[-1, 1]`) describe what a well-behaved profile keeps a wire within — icsmoi
doesn't stop you from going outside them, but hardware-output nodes clamp or
saturate at their limits.

## Inputs

### SimConnect Input
Reads one live variable from a running, connected Microsoft Flight Simulator.
Pick the variable from the built-in list (each has its own natural unit —
airspeed in knots, angles in degrees, etc.; hover/check the picker for the
exact unit of the one you chose). One output: **Value**.

Available variables: indicated airspeed, barber-pole (max safe) airspeed,
engine 1 throttle lever position, stall warning, on-ground flag, surface
type, ground velocity, gear handle position, G-force, aileron/elevator/rudder
position, bank angle, pitch angle, radar altitude, flaps handle percent, and
three body-axis accelerations (pitch rate, roll rate, vertical G).

### Joystick Input
Reads one physical DirectInput controller. Pick the device, then add one row
per axis or button you want available as a wire. Axes output **[-1, 1]**
(centered at 0 — a lever pulled to its minimum is **−1**, not 0); buttons
output **1** while held, **0** otherwise. An unassigned row, or a device
that's unplugged, reads **0**.

## Math & Logic

### Math Compute
Evaluates a text expression (e.g. `[A] + [B]`, `([A]-[B])*0.02`) over its two
inputs, **A** and **B**. One output: **Result**.

### Compare
Compares **A** and **B** with a chosen operator (`>`, `<`, `>=`, `<=`, `==`,
`!=`). Output: **Result** (1 or 0).

### Logic
Combines **A** and **B** with AND / OR / XOR, or inverts **A** with NOT (B is
ignored but still shown). Output: **Result** (1 or 0).

### Select
A wire-level if/else: outputs **IfTrue** when **Condition** is non-zero,
otherwise **IfFalse**.

## Shaping

### Clamp
Restricts **Value** to **[Min, Max]** (defaults −1/1).

### Range Map
Rescales **Value** from **[InMin, InMax]** to **[OutMin, OutMax]** (defaults
to an identity 0..1 → 0..1 map). The optional **Clamp** setting keeps the
result inside the output range instead of extrapolating past it.

### Curve
A draggable, piecewise-linear function from **Value** to **Result** — see
[4. The Editor](04-the-editor.md#the-curve-node) for how to edit the points on
the canvas. This is the go-to way to turn a raw telemetry value into a
force-feeling curve (e.g. spring stiffness that ramps up with airspeed)
without any expression syntax.

### Integrator
Keeps a running total over time — the graph's only node with memory.
**Input** is added every tick, scaled by elapsed time (so a speed feeds a
distance); turn on **AddPerTick** to instead add **Input** exactly once per
tick, which is what you want when **Input** comes from an Edge Detector's
one-tick pulse (a fixed step per button press, rather than a per-second
rate). **Wrap** > 0 wraps the total into `[0, Wrap)`, ignoring Min/Max — for a
repeating pattern; otherwise the total clamps to **[Min, Max]** (default
−1/1). While **Reset** is non-zero, the total is held at 0. Output:
**Result**.

*Typical use — press-to-step trim:* two buttons' Edge Detector **Rising**
outputs → Math `([A]-[B])*step` → Integrator (**AddPerTick** on) → a
Condition node's **Offset**. See the bundled
[`03-button-trim.icsmoi.json`](../examples/README.md#03-button-trimicsmoijson)
example.

### Edge Detector
Turns a level into a one-tick pulse: **Rising** fires (briefly reads 1) the
instant **Input** goes from 0 to non-zero, **Falling** the instant it goes
back to 0, **Either** on both. Nothing fires on the very first read after
icsmoi starts or a profile switches — a button already held at that moment
doesn't count as a fresh press. Because the pulse only lasts one tick
(roughly 10–16 ms), feed it into an Integrator or Logic node rather than
straight into a force — a very short tap can otherwise be missed entirely.

## Hardware Output

These four node types are the only ones that create a real DirectInput
effect. Each shares the **device / axis / direction / gain** settings block
described in [4. The Editor](04-the-editor.md#hardware-output-node-settings).

### Constant Force
A steady push. **Magnitude** in **[-1, 1]** — the sign is the direction
when a single axis is chosen, or feeds the direction dial's vector when
"All axes" is selected. Supports the direction dial.

### Condition
Covers all four DirectInput "condition" effects — **spring**, **damper**,
**inertia**, and **friction** — pick which with the node's **Condition kind**
setting; they all share the same inputs:

- **PositiveCoefficient** / **NegativeCoefficient** — `[-1, 1]`, strength on
  either side of center.
- **Offset** — `[-1, 1]`, shifts where "center" is (this is what drives a trim
  effect).
- **DeadBand** — `[0, 1]`, a neutral zone around center with no force.
- **Saturation** — `[0, 1]`, the maximum force the effect can reach (defaults
  to 1, full force).

Condition effects apply **per axis** rather than as a single directional
push, so there is **no direction dial** for this node — "All axes" simply
means "the same spring/damper/etc. on every axis," like a natural centering
feel.

### Periodic
An oscillating force — **Sine**, **Square**, **Triangle**, **Sawtooth Up**, or
**Sawtooth Down**, picked with the node's **Waveform** setting. Inputs:
**Magnitude** `[-1, 1]`, **Period** in seconds (must be greater than 0 — it
defaults to 0.1s / 10 Hz), **Phase** as a `[0, 1)` fraction of one cycle, and
**Offset** `[-1, 1]`. Supports the direction dial. Good for buffet, ground
rumble, or a stick shaker.

### Ramp Force
Linearly ramps from **StartMagnitude** to **EndMagnitude** (both `[-1, 1]`)
over **Duration** seconds (defaults to 1s) — the only hardware-output effect
that naturally ends on its own rather than playing until stopped. Useful for
a one-shot event like a touchdown thump. Supports the direction dial.

## Gain, and why a raw axis into Magnitude feels like "full force instantly"

Every hardware-output node's numeric inputs are converted to the device's
native force units by scaling `[-1, 1]` to the device's full range — so
wiring a joystick axis (already `[-1, 1]`) straight into **Magnitude** spans
the effect's *entire* force range over that axis's *entire* travel, and
anything beyond ±1 just saturates at maximum. If an effect feels like it
snaps straight to full force, check what's feeding it — a Range Map node is
usually what you want between a raw input and a force input, to compress it
into a gentler slice of the range. **Gain** (per node, 0–10000) scales the
whole effect down independently of this.

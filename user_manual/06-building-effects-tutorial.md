# 6. Building effects — a tutorial

This walks through building the three [example profiles](../examples/README.md)
from an empty canvas, step by step. If you'd rather just use them, load the
files directly — this is for understanding *how* they're built so you can
design your own.

Open the Editor on a new, empty profile to start (Start Menu → icsmoi Editor,
or Edit on an empty Runtime slot).

## Tutorial 1 — Airspeed buffet

A Constant Force effect that grows with indicated airspeed.

1. From the sidebar, add a **SimConnect Input** node. Set its variable to
   **AIRSPEED INDICATED**.
2. Add a **Range Map** node. Wire the SimConnect node's **Value** output into
   Range Map's **Value** input.
3. On the Range Map node, set **InMin** = `40`, **InMax** = `200` (its
   unconnected fields, in knots), and **OutMin** = `0`, **OutMax** = `0.15`
   (start gentle — see [8. Safety](08-safety.md)). Leave **Clamp** on.
4. Add a **Constant Force** node. Wire Range Map's **Result** into its
   **Magnitude** input.
5. On the Constant Force node's settings block, pick your device and an axis
   (or "All axes" plus a direction).
6. Save, load the profile into a Runtime slot, connect to the sim, and switch
   **Effects** on. The push should grow as your indicated airspeed climbs
   from 40 to 200 knots and hold flat above that.

## Tutorial 2 — Centering spring with airspeed-based stiffness

A self-centering spring that firms up as you speed up.

1. Add a **SimConnect Input** node set to **AIRSPEED INDICATED**.
2. Add a **Curve** node. Wire the SimConnect output into its **Value** input.
3. Shape the curve (see [4. The Editor](04-the-editor.md#the-curve-node)):
   double-click to add points roughly at `(0, 0.05)`, `(60, 0.15)`,
   `(120, 0.4)`, `(200, 0.6)` — soft near stall speed, firmer at cruise.
4. Add a **Condition** node, set its **Condition kind** to **Spring**. Wire
   the Curve's **Result** into *both* **PositiveCoefficient** and
   **NegativeCoefficient** (one output can feed more than one input).
5. Pick a device on the Condition node's settings (Condition effects apply
   per-axis on the whole device — see
   [5. Node reference](05-node-reference.md#condition)).
6. Save, load, connect to the sim, switch Effects on, and feel the spring
   firm up as you accelerate.

## Tutorial 3 — Press-to-step trim

Two buttons nudge a spring's center point by a fixed step each press — no sim
connection needed.

1. Add a **Joystick Input** node, pick your device. Its first row defaults to
   an unassigned binding — click it and press your "trim up" button; use
   **+ Add input** for a second row and assign your "trim down" button.
2. Add two **Edge Detector** nodes. Wire the trim-up joystick pin into the
   first one's **Input**, and trim-down into the second's **Input**.
3. Add a **Math Compute** node with expression `([A]-[B])*0.02`. Wire the
   first Edge Detector's **Rising** output into **A**, the second's
   **Rising** into **B**. (`0.02` is the step size per press — bigger moves
   trim faster.)
4. Add an **Integrator** node, turn on **AddPerTick**. Wire the Math node's
   **Result** into its **Input**. Leave **Min**/**Max** at their −1/1
   defaults.
5. Add a **Condition** node (**Spring**). Give it a modest baseline feel by
   setting **PositiveCoefficient** and **NegativeCoefficient** to `0.3`
   (their unconnected constant fields — no need to wire anything into them).
   Wire the Integrator's **Result** into **Offset**.
6. Pick a device on the Condition node, save, load into a slot, switch
   Effects on, and press your two buttons — the spring's center should step
   with each press.

From here, try combining ideas from all three: a Curve-shaped stiffness *and*
a button-trimmed offset on the same Condition node, a Periodic node layered
in for buffet, or an Edge Detector driving a Ramp Force for a one-shot thump.

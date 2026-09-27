# Example profiles

Three ready-made `.icsmoi.json` profiles you can load straight into a Runtime
slot, or open in the Editor to see how they're wired. See
[`user_manual/06-building-effects-tutorial.md`](../user_manual/06-building-effects-tutorial.md)
for a step-by-step walkthrough of building each of these from scratch.

They're also installed alongside icsmoi (in an `Examples` folder next to the
app) if you installed via the MSI, so you don't need this repository to try
them.

## 01-airspeed-buffet.icsmoi.json

A Constant Force effect whose magnitude ramps up with indicated airspeed —
a rough, gentle stand-in for airframe buffet at speed. Needs: MSFS running and
connected (**Connect to Sim** in the Editor, or just running via the Runtime),
and a force-feedback device assigned to the Constant Force node's device/axis
fields.

## 02-centering-spring.icsmoi.json

A centering spring (DirectInput Condition effect) whose stiffness is driven by
a Curve node reading airspeed — soft on the ground, firming up as you
accelerate. Needs the same setup as above, plus the Condition node's device
assigned (Condition effects always target a specific device's axes — see
[`user_manual/05-node-reference.md`](../user_manual/05-node-reference.md)).

## 03-button-trim.icsmoi.json

Press-to-step trim: two joystick buttons nudge a running offset up/down
(via an Integrator), which feeds a centering spring's `Offset` — each press
moves the trim by a small fixed step, exactly like a real trim hat. Needs a
DirectInput controller with at least two buttons; after loading, click the
Joystick Input node's two input rows in the Editor to assign your own trim-up
and trim-down buttons (the profile ships assuming Button 1 and Button 2, which
may not match your controller). No sim connection required — it works from
the joystick alone.

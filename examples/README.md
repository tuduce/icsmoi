# Example profiles

Five ready-made `.icsmoi.json` profiles you can load straight into a Runtime
slot, or open in the Editor to see how they're wired. The first three have a
step-by-step build-from-scratch walkthrough in
[`user_manual/06-building-effects-tutorial.md`](../user_manual/06-building-effects-tutorial.md);
`04` and `05` are larger, real-world profiles meant to be explored in the
Editor directly rather than rebuilt by hand.

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

## 04-fixed-landing-gear.icsmoi.json

A full-feel profile for a fixed-gear aircraft (40 nodes): runway surface feel
with separate rumble/hum patterns for asphalt, concrete (including a periodic
expansion-joint jolt), and grass — selected by `SURFACE TYPE` and gated by
`SIM ON GROUND`/ground speed — plus stall buffet, roll/pitch centering
springs whose stiffness scales with airspeed relative to the yellow-line
speed, and joystick-button trim on two axes. Open it in the Editor to see the
full graph rather than following a tutorial. Needs: MSFS connected, your own
device/axis picked on every hardware-output node, and your own controller's
buttons re-captured on the Joystick Input node — it was built against a
specific setup, so these won't match yours automatically.

## 05-retractable-landing-gear.icsmoi.json

The same full feel package as `04` (47 nodes), adapted for retractable gear:
adds a `GEAR HANDLE POSITION` SimConnect node feeding a little extra
Logic/Select wiring that gates some of the ground-feel effects by gear state.
Same setup requirements as `04` — re-assign every device/axis and joystick
button to your own hardware before use.

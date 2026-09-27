# 2. Quick Start

This walks through the fastest path to feeling icsmoi actually do something:
loading the bundled **Button Trim** example, which needs only a joystick — no
flight sim required.

> Before doing this with an unfamiliar profile on real hardware, skim
> [8. Safety](08-safety.md). This example uses a small, deliberately gentle
> spring, but it's a good habit to build from the start.

1. Plug in a DirectInput controller (a force-feedback stick is best, but any
   controller works for this example — the "spring" just won't push back
   without FFB hardware).
2. Launch **icsmoi** (Start Menu, or click the tray icon if it's already
   running). The status window opens.
3. Under **Slot 1**, click **Load profile…** and pick
   `Examples\03-button-trim.icsmoi.json` from icsmoi's install folder (or from
   this repo's [`examples/`](../examples/) folder if you built from source).
4. Click the pencil (**Edit**) icon on Slot 1. This opens the Editor with that
   profile loaded.
5. Find the **Joystick Input** node. It has two rows, defaulted to "Button 1"
   and "Button 2" — click the first row's field, then press the button on
   your own controller you want to use for trim up (you have ~15 seconds;
   click the field again to cancel). Repeat for the second row with your
   trim-down button.
6. Save the profile (toolbar **Save** button). The running Runtime picks up
   the change immediately — no restart needed.
7. Back in the status window, find the **Effects** toggle and switch it on.
   This is the one moment icsmoi actually touches real hardware — see
   [3. The Runtime app](03-the-runtime-app.md) for what this toggle does.
8. Press your assigned buttons. You should feel the centering spring's
   trim point step with each press.
9. When you're done, switch **Effects** back off (or unplug the controller) —
   this immediately stops every effect.

From here:
- [3. The Runtime app](03-the-runtime-app.md) explains the rest of the status
  window (slots, aircraft association, hot reload).
- [4. The Editor](04-the-editor.md) explains building/editing profiles.
- [6. Building effects — a tutorial](06-building-effects-tutorial.md) walks
  through building this and the other example profiles from an empty canvas.

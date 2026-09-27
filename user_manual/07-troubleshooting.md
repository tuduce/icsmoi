# 7. Troubleshooting

## The sim connection dot stays grey or amber

- Amber means icsmoi is trying to connect but hasn't succeeded yet — it
  retries automatically every few seconds, so this is normal for the first
  moment after MSFS finishes loading into a flight.
- Make sure MSFS is fully loaded into a flight (not just the main menu).
- If it never turns green, restart MSFS and/or icsmoi.

## My controller doesn't show up in the device list

- Make sure it's plugged in and shows up in Windows' own "Set up USB game
  controllers" panel first — if Windows doesn't see it, icsmoi won't either.
- icsmoi re-scans the device list periodically; try reopening the Editor or
  the device dropdown if you plugged it in after opening icsmoi.
- If a device disconnects mid-session, icsmoi retries reconnecting to it
  automatically every couple of seconds.

## I switched Effects on and don't feel anything

Check, in this order:

1. **Is a device actually assigned?** Every hardware-output node needs a
   device (and usually an axis) picked in its settings block.
2. **Is the value reaching it too small?** Weak magnitudes may not be
   perceptible on your particular hardware — try temporarily raising the
   node's **Gain**, or the constant/curve values feeding it, to confirm the
   wiring works at all, then dial it back down.
3. **Is a raw axis or button wired straight into a hardware-output input?**
   This is usually *too much* rather than too little — see
   [5. Node reference](05-node-reference.md#gain-and-why-a-raw-axis-into-magnitude-feels-like-full-force-instantly).
   If it instead feels like it snaps straight to full force, that's the more
   likely cause.
4. **Check the status window's message line** — a failed effect (wrong
   device, driver issue) shows an error there instead of doing nothing
   silently.

## An effect feels "stuck" or doesn't update

Very rapid changes (many times per second) to the same effect are naturally
throttled a little to keep the connection to your hardware stable — a
fast-changing value is still fully expressed, just sampled rather than
updated on every single tick. If a value seems to have frozen entirely rather
than merely lagging, toggle **Effects** off and back on.

## A slot shows a load error

The profile file has a problem (often: it references a node type from a much
older or newer icsmoi version). The **previous, working profile keeps running
in that slot** — nothing gets torn down just because a reload failed. Open the
file's Editor (if it opens at all) to find and fix the issue, or load a known
-good file instead.

## Reporting a bug

Please include:

- Your icsmoi version (Releases page filename, or check the status
  window/Editor toolbar).
- Whether MSFS 2020 or 2024, and your controller model.
- The relevant lines from `%LOCALAPPDATA%\icsmoi\runtime-errors.log`.
- The `.icsmoi.json` profile file, if the issue is profile-specific.

# 1. Installation

## Requirements

- **Windows 10 or 11, 64-bit.**
- **A DirectInput game controller.** A force-feedback (FFB) device if you want
  to feel real effects; any DirectInput controller (even without FFB) works
  fine if you only want to read its axes/buttons into a profile.
- **Microsoft Flight Simulator 2020 or 2024** — only needed if your profile
  reads live simulator data (airspeed, aircraft attitude, etc.). Profiles that
  only use joystick input don't need the sim running at all.

icsmoi is self-contained: it does **not** require you to separately install
the .NET runtime.

## Installing

1. Download the latest `icsmoi-<version>-win-x64.msi` from the project's
   [Releases](../../../releases) page.
2. Run it and follow the installer.
3. **Windows may show a "Windows protected your PC" SmartScreen warning.**
   This is expected — the installer isn't code-signed (that requires a paid
   certificate, which this hobby project doesn't currently have). Click
   **More info**, then **Run anyway**. This is the same warning any small
   independent developer's installer gets; it does not mean anything is wrong
   with the download, as long as you got it from the project's own GitHub
   Releases page.
4. The installer places icsmoi under `Program Files\icsmoi\`, adds a Start
   Menu shortcut, and installs the [example profiles](../examples/README.md)
   into an `Examples` subfolder next to the app.

## First launch

Start **icsmoi** from the Start Menu. It's a background app: it puts an icon
in the system tray and opens a small status window (see
[3. The Runtime app](03-the-runtime-app.md)). Closing the status window
doesn't quit icsmoi — click the tray icon to bring it back, or exit from the
tray icon's right-click menu.

## Where icsmoi keeps its data

- Settings, profile-slot assignments, and aircraft associations:
  `%LOCALAPPDATA%\icsmoi\runtime-settings.json`
- Error log (useful for troubleshooting, or to attach to a bug report):
  `%LOCALAPPDATA%\icsmoi\runtime-errors.log`
- Your own saved profiles live wherever you chose to save them — icsmoi
  doesn't force a fixed folder.

## Uninstalling

Use **Settings → Apps → Installed apps** (or the classic Control Panel →
Programs and Features) and uninstall **icsmoi** like any other Windows app.
This removes the installed program files but leaves your own saved profiles
and the `%LOCALAPPDATA%\icsmoi\` settings folder untouched.

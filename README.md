<img src="Assets/icon-512.png" alt="SISR watcher icon" width="96" align="right">

# SISR watcher

A small Windows tray app that starts and stops [SISR](#about-sisr) automatically when your games run.

**Download:** get the latest version from the [Releases page](https://github.com/GreyGhost03/SISRWatcher/releases/latest).

## Why

SISR watcher is built to keep couch gaming on a PC smooth when you play Xbox PC games with a Steam Controller.

Xbox PC (Game Pass / Microsoft Store) games don't support the Steam Controller directly. SISR lets the
Steam Controller act as an Xbox controller, but running it by hand means getting up for the keyboard and mouse,
and while SISR is running it can also get in the way of navigating the Steam Big Picture interface. That makes
the trip from Big Picture into a game, and back out again, a hassle.

SISR watcher handles all of that in the background. It launches SISR when one of your chosen games starts and
closes it when the game exits, so you never start SISR manually, and Big Picture works normally the whole time
you're picking a game or browsing. The result is a controller-only experience: pick the game in Big Picture, play,
quit, and you're back in Big Picture.

## Features

- Lives in the system tray; open the window when you need it
- Add games from a list of running programs, by browsing or dropping an `.exe`/shortcut, by scanning your `XboxGames` folders, or by typing an exe name
- Pause individual games without removing them
- Adjustable **SISR start delay** (in seconds)
- Activity log
- Optional start with Windows
- Only closes a SISR it started itself; a SISR you opened manually is left alone

## About SISR

[SISR](https://github.com/Alia5/SISR) is a separate project and is **not included** with this app. Download and set it up from its own project
page, then point SISR watcher at `SISR.exe`.

## Requirements

- Windows 10 or 11 (64-bit)
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) or newer to run
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or newer to build from source
- [SISR](https://github.com/Alia5/SISR)

## Build from source

```
winget install Microsoft.DotNet.SDK.8
build.bat
```

This creates `dist\SISRWatcher.exe`. You can also build manually:

```
dotnet publish SISRWatcher.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o dist
```

## Use

1. Run `SISRWatcher.exe`. On first run it looks for `SISR.exe` near its own folder. If it can't find it,
   open **Settings** and click **Browse** (or **Auto-detect**).
2. Click **Add game** and choose a method:
   - **Running now**: launch the game, then tick it in the list.
   - **Browse or drop exe**: drop a game `.exe` or shortcut into the box, or click it to browse.
   - **Scan Xbox games**: lists executables in `XboxGames` folders on your drives.
   - **Type a name**: type the exe name yourself (for example `MyGame.exe`). The game doesn't need to be installed yet.
3. Play. SISR starts after the SISR start delay and closes when the game exits.

Games are matched by process name (the exe name without `.exe`). New games are watched immediately; no restart needed.

### Tray

- Click the tray icon to open the window. The X button only hides the window.
- Right-click the icon for **Pause watching**, **Stop SISR now**, and **Quit**.
- Icon colors: gray = idle, amber = game detected, green = SISR running, red = problem.

## Where settings are stored

| File | Contents |
| --- | --- |
| `config.json` (next to `SISRWatcher.exe`) | Game list and SISR start delay. Plain text; safe to edit or back up. |
| `%APPDATA%\SISRWatcher\local.json` | Where `SISR.exe` is on this PC |
| Windows startup entry | "Start with Windows" (per PC) |

The app picks up edits to `config.json` made while it's running, so keeping the app folder in a synced
location (Nextcloud, OneDrive, Dropbox, etc.) works for sharing a game list between PCs. Each PC keeps its own SISR location.

## Project layout

| File | Purpose |
| --- | --- |
| `Program.cs` | Entry point; single-instance handling |
| `TrayApp.cs` | Tray icon, menu, window management |
| `Watcher.cs` | Detects games and starts/stops SISR |
| `Config.cs` | Reads and writes `config.json` and `local.json` |
| `Discovery.cs` | Running-programs list, Xbox games scan, SISR auto-detect |
| `MainForm.cs` | Main window |
| `AddGameForm.cs` | Add game dialog |
| `Startup.cs` | Start with Windows |
| `Assets/` | App icon (`SISRWatcher.ico` is used for the exe; `icon-512.png` is for the repo) |

The tray and window icons are drawn in code (`TrayApp.cs`), so the tray icon can change color with state.

## Limitations

- Checks for games every 2 seconds, not instantly
- Matching is by process name, so a game update that renames its exe needs the game to be added again
- "Scan Xbox games" can list extra executables for some games; check the details column
- Windows only

## License

[MIT](LICENSE)

# ETS2 Tachograph

A digital tachograph for **Euro Truck Simulator 2** that tracks your driving time against the EU driver-hours rules (Regulation (EC) 561/2006), warns you before you need to stop, and shows it all on a dark, DTCO 4.0-style instrument.

It runs as a companion app. It reads live data from the [TruckSim GPS Telemetry Server](https://github.com/TruckSim-GPS/trucksim-gps-server) (default `http://localhost:31377`), so that server must be running while you play.

> This is a game aid. It is not a certified tachograph and has no legal standing.

## Features

- **EU rules engine** driven by in-game time:
  - 4h30 continuous driving, then a 45 min break (or a 15 + 30 split)
  - 9h daily driving, extendable to 10h twice a week
  - 56h weekly and 90h fortnightly driving
  - 11h daily rest, reduced 9h rest (max three), and 3 + 9 split rest
  - 45h weekly rest, or 24h reduced with compensation owed
- **Automatic activity detection:** Driving, Work and Rest from speed, engine and parking brake. Ferry and train crossings count as Rest (or Availability, your choice).
- **Spoken and visual warnings** at 30, 15, 5 and 0 minutes, with an optional chime.
- **Session persistence:** your totals carry over between play sessions.
- **28-day history view** with a 24-hour activity chart, daily and weekly totals, infringements, and CSV export.
- **Global hotkeys** for everything (status, today's summary, history, previous/next day, voice on/off, show/hide, silence), all reassignable. They work while the game has focus.
- **Tuning options:** traffic-light hold time, voice volume and speed, chime on/off.

## Install

Download `Ets2Tachograph_Setup_x.y.z.exe` from the [Releases](https://github.com/johnevansau320/ETS2Tachograph/releases) page and run it. It installs for the current user and needs no admin rights or .NET install.

The installer is unsigned, so Windows SmartScreen or your antivirus may warn about it. You can check the SHA-256 shown in the release notes against the file you downloaded.

Settings and your driving record are stored in `%AppData%\Ets2Tachograph` and are kept when you uninstall.

### Default hotkeys

| Action | Hotkey |
|---|---|
| Speak full status | Ctrl+Alt+T |
| Speak today's summary | Ctrl+Alt+S |
| Show / hide history | Ctrl+Alt+H |
| History: previous / next day | Ctrl+Alt+PageUp / PageDown |
| Spoken warnings on / off | Ctrl+Alt+M |
| Show / hide instrument | Ctrl+Alt+W |
| Silence voice and dismiss banner | Ctrl+Alt+D |

Right-click the window for History, Hotkeys, Settings, export and update check. The instrument and history windows only appear over the game in windowed or borderless mode.

## Build from source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) on Windows.

```bash
dotnet test tests/Tachograph.Tests
dotnet run --project src/Tachograph.App
```

| Project | What it is |
|---|---|
| `src/Tachograph.Core` | Rules engine, telemetry parsing, history, export (no UI) |
| `src/Tachograph.App` | WPF instrument, history window, settings, hotkeys |
| `src/Tachograph.Cli` | Console runner for quick checks |
| `tests/Tachograph.Tests` | xUnit tests for the engine and helpers |
| `installer/` | Inno Setup script |

To build the installer, publish the app, then compile the script:

```bash
dotnet publish src/Tachograph.App -c Release -r win-x64 --self-contained true -o publish
ISCC.exe /DSourceDir=publish /DOutDir=dist installer/Ets2Tachograph.iss
```

## Assumptions

- ETS2 game time 0 is Monday 00:00, which sets the weekly boundaries.
- A stop with the engine running counts as Driving for a short hold (default 10 game minutes), then Work.
- Engine off with the parking brake on counts as Rest immediately.

## Licence

[MIT](LICENSE)

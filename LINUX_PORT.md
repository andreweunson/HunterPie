# HunterPie on Linux

HunterPie reads the memory of a Windows game, so on Linux it runs natively and attaches to the game running
under Wine/Proton through `/proc/<pid>/mem`. `HunterPie.Linux` is a headless (console) host: it has no overlay yet,
but it runs the same Core + Integrations code as the Windows client and prints player, quest and monster events.

## Build and run

```bash
git submodule update --init                        # monster/quest names come from the Localization submodule
dotnet build HunterPie.Linux.slnf -c Release      # the WPF projects cannot build on Linux, so use the filter
cd HunterPie.Linux/bin/Release/net10.0
./HunterPie.Linux --doctor                         # checks the machine and looks for a running game
./HunterPie.Linux                                  # attach and print events, Ctrl+C to quit
```

Options: `--verbose` (debug logs), `--run-for <seconds>`, `--help`.

### Required once: allow reading the game's memory
Linux blocks reading other processes by default (Yama `ptrace_scope=1`). Either
`sudo sysctl kernel.yama.ptrace_scope=0` (until reboot) or `sudo setcap cap_sys_ptrace=eip ./HunterPie.Linux`.
`--doctor` tells you which applies.

### Recommended: `xdotool`
Used for the window title and focus (works on X11 and XWayland, which Proton uses). Monster Hunter: World needs it
because the game build number is only in the window title. Rise and Wilds attach without it.

## Overlay (Avalonia)
```bash
dotnet build HunterPie.Linux.slnf -c Release
cd HunterPie.Linux.Overlay/bin/Release/net10.0
./HunterPie.Linux.Overlay --demo --edit     # no game needed: fake data, drag widgets into place
./HunterPie.Linux.Overlay --toggle-edit     # toggle edit mode from another terminal (same as kill -USR1 <pid>) (saves ~/.config/HunterPie/overlay.json)
./HunterPie.Linux.Overlay                   # attach to the game and show monster health + quest timer
```
It is a transparent, always-on-top window covering one monitor (`--screen <n>`). Outside edit mode its input region is
empty (XShape), so mouse and keyboard go to the game. In edit mode drag a widget to move it and scroll over it to resize. In edit mode an on-screen panel has "Save & exit edit mode" and "Quit overlay" buttons; Esc leaves edit mode and Ctrl+Q quits.
Bind `kill -USR1 $(pidof HunterPie.Linux.Overlay)` to a key in your desktop environment to get a "toggle layout" hotkey.

Requirements: a compositing window manager (otherwise the transparent window is drawn black) and the game in
**borderless windowed** mode; overlays cannot draw over exclusive fullscreen. Works on X11 and XWayland. The first
build prints a blocked-connection warning for Avalonia build telemetry; set `AVALONIA_TELEMETRY_OPTOUT=1` to silence it.

Widgets today: monster health (name, bar, numbers, enrage badge; one card per monster) and quest timer.

## How it works
- `HunterPie.Platforms/Linux/` implements the platform interfaces: process discovery (`/proc/*/cmdline` + the PE image
  in `/proc/<pid>/maps`, which also gives the `BASE` address), memory (`/proc/<pid>/mem`), game version (PE
  `VS_VERSION_INFO` parsed from the `.exe`), settings/credentials under `~/.config/HunterPie/`.
- `HunterPie.Linux/` is the host: it mirrors the WPF client's startup (mappers, DI modules, config) without a
  `Dispatcher`, and shares `ScanService` and `ScopedLocalizationRepository` as source from the WPF project, so the Windows build is untouched.
- Config (`config.json`) and the address maps live next to the executable, like on Windows.

## Status
| Piece | State |
|---|---|
| Core, DI, Integrations | Portable, unchanged except `ProcessCandidate` / `IsAdmin` |
| Linux platform layer | Done |
| Headless host | Done; attach, scan loop, shutdown and `--doctor` exercised against a fake game process |
| Real-game test | Wilds 1.42.0.2 on Proton: attach, login, stage, quest, monster HP and enrage verified. World and Rise not yet tried |
| Overlay (`HunterPie.Linux.Overlay`) | Monster health, quest timer and damage meter (per-player damage, share of team damage, DPS counted from when each member was first seen). Monster/quest widgets confirmed over the real game on KDE; the damage meter (with weapon icons, stats and DPS graph) is verified on a virtual X server and against a fake party only |
| Remaining WPF widgets (player vitals, abnormalities, damage meter, parts, ailments, settings UI) | Not ported: `HunterPie.UI` is 424 C# / 116 XAML files |
| Native module (DLL injection), Discord RPC, updater | Windows only for now |

## Known limits of the headless host
- Only `en-us.xml` is loaded, whatever language is set in `config.json`. Without the Localization submodule, names show as ids.
- Wilds/World/Rise game-specific context initializers (native module, integrity patchers) are Windows-only and not run.
- `config.json` is written next to the binary (same as Windows); move it to XDG when the install path matters.

## Next
Port more widgets onto the overlay (they only need `IContext`: player health/stamina, abnormalities, damage meter, monster
parts and ailments), then a settings UI. Click-through is solved with XShape; true global hotkeys are not (the SIGUSR1
toggle is the stand-in). On native Wayland a layer-shell surface and the GlobalShortcuts portal would replace both.

### Overlay stacking on Wayland

Avalonia has no native Wayland backend, so the overlay is an XWayland window and `Topmost` only asks the compositor for
"keep above". Compositors may still put a focused fullscreen game above it. `--override-redirect` is an experiment:
the overlay becomes an unmanaged X11 window, which compositors normally stack above everything. It cannot take
keyboard focus (Esc / Ctrl+Q do nothing, the on-screen buttons and `--toggle-edit` still work). Whether it helps
depends on your compositor and on whether the game itself runs under XWayland. Not verified against a real
compositor, only on a bare X server.

### Damage meter

Rows are sorted by damage, with the colour bar showing each member's share of the team total; support hunters are
italic and grey. Each row has the hunter's weapon icon (the vector art from `HunterPie.UI/Resources/Icons.xaml`,
copied into `Assets/WeaponIcons.axaml`), and, when the host has read it, a stats line with attack, elemental attack
and affinity.

Under the rows is a DPS graph with one line per member (yours is thicker). It plots the same DPS number the rows
show, sampled once per second of quest time. The history has a fixed size: when it fills up every second sample is
dropped and the sampling interval doubles, so a whole quest always fits and drawing it never gets more expensive.

In layout edit mode two checkboxes turn the graph and the stats line on or off; the choice is saved in
`overlay.json` next to the widget positions.

The final numbers stay on screen after a quest ends and clear when you leave the hunting zone or a new quest
starts. The meter is polled four times a second from the party data the host already scans, so it adds no memory
reads. Players already in the party when the overlay first looks count from the start of the quest, so attaching
mid-quest gives each of them a DPS averaged over the whole quest.

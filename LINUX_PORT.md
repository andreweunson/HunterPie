# HunterPie on Linux

HunterPie reads the memory of a Windows game, so on Linux it runs natively and attaches to the game running
under Wine/Proton through `/proc/<pid>/mem`. `HunterPie.Linux` is a headless (console) host: it has no overlay yet,
but it runs the same Core + Integrations code as the Windows client and prints player, quest and monster events.

## Build and run

```bash
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
| Real-game test | **Not done yet**: needs a machine with the game. Run `--doctor` first and report what it prints |
| Overlay UI (WPF -> Avalonia) | Not started: `HunterPie.UI` + `HunterPie` are 540 C# / 161 XAML files |
| Native module (DLL injection), Discord RPC, updater | Windows only for now |

## Known limits of the headless host
- No localization submodule needed: missing language files fall back to keys.
- Wilds/World/Rise game-specific context initializers (native module, integrity patchers) are Windows-only and not run.
- `config.json` is written next to the binary (same as Windows); move it to XDG when the install path matters.

## Next: UI
Port `HunterPie.UI` to Avalonia on top of this host. The hard parts are the click-through transparent overlay window
and global hotkeys (X11 input shape / XGrabKey, or layer-shell and the GlobalShortcuts portal on Wayland).

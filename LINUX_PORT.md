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
Starting it opens the **HunterPie settings window** (the app's main window): game status, "Edit widget layout" and
"Reset positions", the monitor the overlay covers, "Keep above fullscreen games" (override-redirect, see below), and
for each widget (quest timer, monsters, damage meter) a visibility checkbox, size and opacity sliders and its options. Closing that window quits HunterPie and the overlay. Option changes apply to the overlay
immediately and are saved to `overlay.json`.

The overlay itself is a transparent, always-on-top window covering one monitor (`--screen <n>`). Outside edit mode its input region is
empty (XShape), so mouse and keyboard go to the game. In edit mode drag a widget to move it and scroll over it to resize. In edit mode an on-screen panel has "Save & exit edit mode" and "Quit HunterPie" buttons; Esc leaves edit mode and Ctrl+Q quits.
Bind `kill -USR1 $(pidof HunterPie.Linux.Overlay)` to a key in your desktop environment to get a "toggle layout" hotkey.

Requirements: a compositing window manager (otherwise the transparent window is drawn black) and the game in
**borderless windowed** mode; overlays cannot draw over exclusive fullscreen. Works on X11 and XWayland. The first
build prints a blocked-connection warning for Avalonia build telemetry; set `AVALONIA_TELEMETRY_OPTOUT=1` to silence it.

Widgets today: monster health (name, bar, numbers, enrage badge, stamina, weaknesses, ailments; one card per monster), quest timer and damage meter.

## Logs

Both the headless host and the overlay write log files (turn off with `--no-logs`) to `$HUNTERPIE_LOG_DIR`, else
`$XDG_STATE_HOME/HunterPie/logs`, else `~/.local/state/HunterPie/logs`. The settings window has an "Open log folder" button.

- `session-<date>-<time>.log`: everything HunterPie logs, **debug lines included** (the console only shows them with
  `--verbose`), plus the game events below, plus each hunt's summary. One per run.
- `hunt-<date>-<time>-<quest>.log`: one per hunt, opened when the quest starts and closed with a summary when it ends
  (or when the game closes, marked "Interrupted"). It starts with what happened since you entered the hunting zone,
  because the fight usually begins before the quest clock does.

Events are written as `[time][EVENT][+quest clock] text`: stage changes, monsters spawning, losing health (whole-percent
steps), becoming enraged or exhausted, ailments being inflicted, the party changing, quest start and end, fainting. The
summary has the result and duration, each monster that took damage (outcome, enrages, exhaustions, ailment counts, with
the others listed as not engaged) and the party's damage, share and DPS (the same "damage before the quest clock does
not count" rule as the overlay). The flinch gauge is not logged: the game's counter for it misses most flinches.

```
===== Hunt summary =====
Quest:     (unnamed) (id 392, Hunt)
Result:    Success
Duration:  04:49  (13:37:20 to 13:42:10)
Monsters
  Chatacabra (id 19): slain (0 / 10,080 HP when last seen)
      enraged 3x for 01:12 in total, exhausted 1x for 00:40 in total
      ailments: Poison x2
  Not engaged: Tempered Balahara x3
Party damage
  1. Rock (you) [HuntingHorn]  9,100 damage  91%  31.9 DPS
```

Only the newest 20 session logs and 100 hunt logs are kept; nothing else in the folder is touched. A log directory that
cannot be written turns file logging off with one warning instead of affecting the game reading.

HunterPie's own log level setting drops messages before any writer sees them, so the host keeps it at Debug and filters
the console itself; the level in `config.json` is no longer what decides what the console shows.

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
| Overlay (`HunterPie.Linux.Overlay`) | Monster health, quest timer and damage meter (per-player damage, share of team damage, DPS counted from when each member was first seen). Monster (health, capture marker, stamina, weaknesses, portrait) and quest widgets: the first version was confirmed over the real game on KDE, the additions are verified on a virtual X server and against fake monsters; the damage meter (with weapon icons, stats and DPS graph) is verified on a virtual X server and against a fake party only |
| Log files (session and per-hunt, with summary) | Verified against a fake game and on the host with no game; not yet seen on a real hunt |
| Remaining WPF widgets (player vitals, abnormalities, parts, crown and monster type) | Not ported: `HunterPie.UI` is 424 C# / 116 XAML files |
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
The settings window's "Keep above fullscreen games" checkbox is the same as `--override-redirect` (the flag turns it on and
it is remembered); it applies and reverts live.


Avalonia has no native Wayland backend, so the overlay is an XWayland window and `Topmost` only asks the compositor for
"keep above". Compositors may still put a focused fullscreen game above it. `--override-redirect` is an experiment:
the overlay becomes an unmanaged X11 window, which compositors normally stack above everything. It cannot take
keyboard focus (Esc / Ctrl+Q do nothing, the on-screen buttons and `--toggle-edit` still work). Whether it helps
depends on your compositor and on whether the game itself runs under XWayland. Not verified against a real
compositor, only on a bare X server.

### Monster widget

Monster cards sit side by side. Each shows the monster's portrait on a tilted square whose outline marks tempered
(purple), arch-tempered (orange) and frenzied (pink) variants, the health bar with a marker where the monster can be
captured (dim above the threshold, bright with a CAPTURE badge once it is at or below it), a stamina bar with its
numbers and the elements it is weak to (the vector art from `HunterPie.UI/Resources/Abnormalities.xaml`, copied into
`Assets/ElementIcons.axaml`). Stamina and weaknesses only appear when the game reports them.

The Monsters section of the settings window has Icons, Stamina and Weaknesses checkboxes; the choice is saved in
`overlay.json`.

**Portraits are downloaded.** HunterPie's monster icons are not in the source tree: like the Windows client, the
overlay fetches `https://cdn.hunterpie.com/Assets/Monsters/Icons/<Game>_<id>.png` the first time a monster appears
and keeps it in `$XDG_CACHE_HOME/HunterPie/monster-icons` (default `~/.cache/HunterPie/monster-icons`). A file is only
accepted if it is a PNG under 512 KB that decodes; a missing portrait just leaves the empty tilted square. Turning the
Icons checkbox off stops the downloads. `HUNTERPIE_ICON_CDN` points the overlay at a different server (a mirror, or a
local one for testing).

Ailments: under the health bar each monster shows a ring per ailment, as in the Windows client. The outer arc (in the
ailment's colour) is the time left while the effect is active, with the countdown below it; the inner blue arc is the
build-up towards triggering it, with a percentage; the number in the middle is how often it has been inflicted. Enrage
is the first ring. A ring appears when its values start changing and hides 15 s after the last change. Entries the game
lists without a name are skipped. The game's ailment table has two "Exhaust" entries, but id 14 is the only one monsters report and on a real hunt it behaves
like a flinch gauge (it fills with hits and resets with a larger maximum when the monster flinches), so it is labelled
"Flinch". The real exhaust is the monster's stamina: when the bar runs out the game takes a moment, then resets it to the
length of the exhaustion and counts it down. The stamina bar turns teal and reads "EXHAUSTED 38s" for that. The Ailments checkbox in the settings window turns the row off. Names come from the
language file (`Languages/en-us.xml`), colours match the Windows client.

### Damage meter

Rows are sorted by damage, with the colour bar showing each member's share of the team total; support hunters are
italic and grey. Each row has the hunter's weapon icon (the vector art from `HunterPie.UI/Resources/Icons.xaml`,
copied into `Assets/WeaponIcons.axaml`), and, when the host has read it, a stats line with attack, elemental attack
and affinity.

Under the rows is a DPS graph with one line per member (yours is thicker). It plots the same DPS number the rows
show, sampled once per second of quest time. The history has a fixed size: when it fills up every second sample is
dropped and the sampling interval doubles, so a whole quest always fits and drawing it never gets more expensive.

The Damage meter section of the settings window turns the graph and the stats line on or off; the choice is saved in
`overlay.json` next to the widget positions.

The final numbers stay on screen after a quest ends and clear when you leave the hunting zone or a new quest
starts. The meter is polled four times a second from the party data the host already scans, so it adds no memory
reads. Players already in the party when the overlay first looks count from the start of the quest, so attaching
mid-quest gives each of them a DPS averaged over the whole quest. The quest clock can start after the fight did (the
monster is tagged first), so damage a hunter already has when first seen in the first 15 s, or when joining later, is
shown in the damage column but left out of DPS; otherwise the first seconds would show an absurd peak.

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

Options: `--verbose` (debug logs), `--run-for <seconds>`, `--find-map` (see below), `--help`.

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

### Hiding ailments

The monster card draws a ring for every ailment the game reports, and some are noise: the game has a flayer ailment per
hunter in the party. Settings > Monsters > Hidden ailments lists what is left out, and `overlay.json` holds the same
list under `Monsters.HiddenAilments`:

```json
"HiddenAilments": ["AILMENT_FLAYER_*", "Convert Element"]
```

Each entry is matched, ignoring case, against an ailment's id (`AILMENT_FLAYER_PL1`) and its display name
(`Flayer PL1`); `*` matches any run of characters and `?` one. The default is `AILMENT_FLAYER_*`. A file without the key
gets the default, an empty list `[]` hides nothing. Changes apply to the cards at once. Ids the game reports show up in
the suggestions of the settings box. The hunt logs are not filtered: they record every ailment.

## Logs

Both the headless host and the overlay write log files (turn off with `--no-logs`) to `$HUNTERPIE_LOG_DIR`, else
`$XDG_STATE_HOME/HunterPie/logs`, else `~/.local/state/HunterPie/logs`. The settings window has an "Open log folder" button.

- `session-<date>-<time>.log`: everything HunterPie logs, **debug lines included** (the console only shows them with
  `--verbose`), plus the game events below, plus each hunt's summary. One per run.
- `hunt-<date>-<time>-<quest>.log`: one per hunt, opened when the quest starts and closed with a summary when it ends
  (or when the game closes, marked "Interrupted"). It starts with what happened since you entered the hunting zone,
  because the fight usually begins before the quest clock does.

Events are written as `[time][EVENT][+quest clock] text`: stage changes, monsters spawning, losing health (whole-percent
steps), becoming enraged or exhausted, ailments being inflicted, the party changing, quest start and end, and carts used (the game keeps one cart counter for the whole party, so this is the party total, not your own faints). The
summary has the result and duration, each monster that took damage (outcome, enrages, exhaustions, ailment counts, with
the others listed as not engaged) and the party's damage, share and DPS (the same "damage before the quest clock does
not count" rule as the overlay). The flinch gauge is not logged: the game's counter for it misses most flinches.

The summary ends with each timed skill's uptime: the share of the quest clock it was active, how long that is in total
and how many times it activated. Only skills the game reports as timed buffs appear (Burst, Latent Power, Offensive
Guard and the like); passive skills have nothing to measure. Time is the quest clock, so pauses and loading do not
count against a skill. Skills that were already running when the quest clock started count from the start, and if
HunterPie attached to a quest that was already under way the percentages are of the time since it attached, with a
line saying so.

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

Skill uptime (over 04:49 of quest time)
  Burst          62.3%  (03:00, 4 activations)
  Latent Power   41.2%  (01:59, 2 activations)
```

Only the newest 20 session logs and 100 hunt logs are kept; nothing else in the folder is touched. A log directory that
cannot be written turns file logging off with one warning instead of affecting the game reading.

HunterPie's own log level setting drops messages before any writer sees them, so the host keeps it at Debug and filters
the console itself; the level in `config.json` is no longer what decides what the console shows.

## When the game updates and `--doctor` says the map is missing

The game only attaches when `Address/MonsterHunterWilds.<version>.map` exists for its exact file version. Between two
small patches (the last digit of the version) the map's 14 `Address` lines have always moved by one shared,
page-aligned amount, while its `Offset` lines stayed the same. `--find-map` measures that amount:

```bash
./HunterPie.Linux --find-map     # game running, character loaded into the world (not the title screen)
```

It starts from the newest older map, tries every shift of 0x1000 within +-8 MB and keeps the one where the manager
globals (`QuestManager`, `PlayerManager` and so on) hold readable pointers and the offset chains hanging off them lead
to readable memory. The map is written to the `Address` folder next to the executable only when one shift is clearly
ahead of all others, and never over an existing file. Otherwise it says why and writes nothing, which is what happens
when a patch moved the globals by different amounts (the x.y.0.0 releases did). Those need a map made from the
executable itself.

`Encryption::Round` and `Encryption::Key` are shifted with the rest but cannot be verified that way. Wilds hides its
floats (monster health, attack, affinity) behind AES, and a wrong key shows up as nonsense health such as `0/0 HP` or
`2.4e37`. `--find-crypto` checks them, again with the game running and the character loaded:

```bash
./HunterPie.Linux --find-crypto
```

It decrypts the player's affinity and attack, and the health of any monsters in the area, with the key and round in the
map. If the numbers are believable (affinity a short decimal, monster maximum health a whole number) it says so and the
problem is elsewhere. If not, it scans +-16 MB around the old position for the 16 byte round value followed by the key,
first at page-aligned positions and then at every 16 bytes, and rewrites the two lines of the map when one pair clearly
stands out. Only 4 of the 16 decrypted bytes matter, so a lone player's values are a weak test; with a monster in the
area it is decisive.

After the map works, copy the file to `HunterPie/Address/` so it survives a rebuild.

## Finding skills HunterPie does not track

Wilds skills come from a table of pointers (the map's `Player::Abnormalities::Skills`). Each tracked skill is one slot:
`Offset` in `Game/Wilds/Data/AbnormalityData.xml` is the slot's position in hex, and the struct the slot points at has the
timer (a float) at +0x10 and the maximum at +0x14. Skills such as Maximum Might, Agitator and Peak Performance are not in
that file. `--dump-skills` and `--diff-skills` help find out whether they are in the table, and where. Both only read.

```bash
./HunterPie.Linux --dump-skills off      # in a hunting area, skill NOT active
./HunterPie.Linux --dump-skills on       # same place, skill active
./HunterPie.Linux --dump-skills off2     # skill off again (optional, hides values that change by themselves)
./HunterPie.Linux --diff-skills skills-off-*.txt skills-on-*.txt skills-off2-*.txt
```

A dump is a text file in the log folder (`skills-<label>-<date>-<time>.txt`) and the command also prints the slots with a
running timer, with the name of the tracked skill each one is, which doubles as a check that the table was found. The
diff lists the 4-byte words that changed in each slot, first for slots that are not in the XML (where a new skill would
show up) and then for ones that are. A skill with a timer shows a float going from 0 to a positive value at +0x10; a skill
that only switches on and off shows a small int flipping. Confirm with a second situation that one slot always follows
the skill, then add an `Abnormality` line to the XML (`Id="Skills.<game skill id>"`, `Name`, `Offset="<slot>"`, and
`DependsOn`/`WithValue` for a flag instead of a timer). If nothing new changes, the skill is not in this table.

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

### Quitting

*Quit HunterPie* (or closing the settings window) stops the game session on a background thread, waits up to 3 s for
it, and ends the process; a watchdog exits after 6 s whatever is still running, and `Main` calls `Environment.Exit`
so a thread left behind by the game host cannot keep the process alive. The session log gets `[App] Quit requested`,
`[App] Game session stopped` (or `did not stop in time` / `Shutdown took too long`), which says where a slow quit
was held up.

### Damage meter

Rows are sorted by damage, with the colour bar showing each member's share of the team total; support hunters are
italic and grey. Each row has the hunter's weapon icon (the vector art from `HunterPie.UI/Resources/Icons.xaml`,
copied into `Assets/WeaponIcons.axaml`), and, when the host has read it, a stats line with attack, elemental attack
and affinity.

Under the rows is a graph with one line per member (yours is thicker), sampled once per second of quest time. What it
plots is set under Graph in the Damage meter section of the settings window, the same three choices as the Windows
client's damage plot strategy: *Damage per second* (the number the rows show, damage divided by the time since the
hunter joined), *Moving average damage per second* (damage dealt in the last 5 to 60 seconds, 10 by default, divided
by that time; before that much time has passed it equals the plain DPS) and *Total damage*. The three histories are
recorded side by side, so switching shows what was already recorded; changing the moving average window starts that
line afresh. Saved as `GraphMode` and `GraphWindowSeconds` in `overlay.json`.

*DPS from* sets where the seconds in DPS are counted from, for the rows and the graph alike: *Joining the quest* (the
default: from when the hunter was first seen, which is the quest start for those there from the beginning), *Start of
the quest*, or *First hit* (so the walk to the monster does not count; a hunter who has not hit anything yet shows 0).
Damage dealt before the quest clock started is left out in all three. With the moving average, a window longer than the
time since the start of the count simply uses that time. In the hunt log's summary the DPS is still the per-hunter
since-joining figure.

With the moving average there is also *Discard old points*: the graph then shows only the last 10 to 120 seconds
(60 by default) at one point per second, sliding as the quest goes on, instead of the whole quest. The recent seconds
are recorded all the time, so switching it on shows a full graph at once. Changing the DPS strategy or the window
restarts the moving average line (and the DPS line for the strategy). Saved as `DpsStrategy`, `DiscardOldPoints` and
`GraphVisibleSeconds`. The history has a fixed size: when it fills up every second sample is
dropped and the sampling interval doubles, so a whole quest always fits and drawing it never gets more expensive.

The Damage meter section of the settings window turns the graph and the stats line on or off; the choice is saved in
`overlay.json` next to the widget positions.

A hunter who leaves and comes back (the game swaps support hunters in and out whenever the party size changes, and
real players can reconnect) is one row, and one entry in the hunt log, not two. The game starts a returning hunter's
damage from zero again, so the meter adds up the damage of every stay; a counter that carried on is not added twice.
Their DPS counts from the first time they joined. The hunt log says `Party: Kai returned`. Hunters are recognised by
name, so two different hunters with the same name in one quest would be merged.

The final numbers stay on screen after a quest ends and clear when you leave the hunting zone or a new quest
starts. The meter is polled four times a second from the party data the host already scans, so it adds no memory
reads. Players already in the party when the overlay first looks count from the start of the quest, so attaching
mid-quest gives each of them a DPS averaged over the whole quest. The quest clock can start after the fight did (the
monster is tagged first), so damage a hunter already has when first seen in the first 15 s, or when joining later, is
shown in the damage column but left out of DPS; otherwise the first seconds would show an absurd peak.

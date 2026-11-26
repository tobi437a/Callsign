# Callsign

A police career mod for **GTA V Story Mode**. Start as a rookie on probation, work your shifts,
and earn your way up the ranks, unlocking new gear, vehicles, districts and call types as you go.

Calls are not fixed scripts. Each incident is assembled from parts (where it happens, what's
going on, who the suspect really is, what goes wrong halfway through), so the same call can
play out very differently twice. Other developers can add their own callouts and content
through a plugin API.

> **Single player only.** Never launch GTA Online with this mod (or any ScriptHookV mod)
> installed. Remove the files listed under [Uninstalling](#uninstalling) first.

> **Early development.** Right now the mod has the on/off duty system, generated callouts
> (on foot and in vehicles), traffic stops, stopping and questioning suspects, arrests and the
> plugin API. The career is being built. See [Roadmap](#roadmap).

---

## For players

### Requirements
- GTA V (PC) in **Story Mode**
- [Script Hook V](http://www.dev-c.com/gtav/scripthookv/) matching your game version
- [ScriptHookVDotNet](https://github.com/scripthookvdotnet/scripthookvdotnet) **v3** (nightly builds recommended)
- .NET Framework 4.8 (already on Windows 10/11)

### Installation
1. Install Script Hook V and ScriptHookVDotNet following their instructions.
2. Copy `Callsign.dll` and `Callsign.API.dll` into `<GTA V folder>\scripts\`.
3. Start the game in Story Mode. You'll see *"Callsign loaded"* once a save has loaded.

The mod creates a `<GTA V folder>\Callsign\` folder on first start, with a `Plugins` and a
`Data` folder inside, and a log file.

### Controls

| Key | Action |
|---|---|
| `F7` | Go on / off duty. While on duty you can't get a wanted level, the game's own police are switched off, and story phone calls, texts and random events wait until you go off duty. |
| `Y` / `N` | Accept / decline the call dispatch is offering. |
| Hold `Shift` | Pull over the vehicle in front of you (on duty, emergency lights on). |
| Hold `E` | Stop the suspect (on foot, close to them). |
| `E` | Open / close the interaction menu (standing next to a stopped suspect). |
| `Up` / `Down`, `Enter`, `Backspace` | Move through the menu, choose, close. |
| `End` | Call off the current callout or traffic stop. |
| `F9` | Ask dispatch for a call right now. |
| `F6` | Settings: switch single callouts off (saved in `Callsign\settings.xml`), repair your vehicle. |

### How to play
While on duty, dispatch offers a call every minute or two: accept it and follow the yellow route.
Suspects may cooperate, run or fight. Pull vehicles over with your emergency lights. Stop people
with `E` and work out what they're hiding through the menu (ID, questions, records check, tests,
pat-down), then arrest them or let them go. Arrested suspects ride in your back seat to the station.
Between calls, turn on your lights behind any car and hold `Shift` to pull it over: run the plate,
check the driver, then cite, warn or arrest. Most drivers stop; ones with something to hide may not.
All built-in calls are listed in [docs/CALLOUTS.md](docs/CALLOUTS.md).

### Uninstalling
Delete `Callsign.dll` and `Callsign.API.dll` from `scripts\`, and the `Callsign\` folder.

### Installing plugins
Put the plugin's `.dll` (and any files it came with) in `<GTA V folder>\Callsign\Plugins\`.
Only install plugins you trust: a plugin is code that runs inside your game.

### Something's wrong?
Check `<GTA V folder>\Callsign\Callsign.log`. A plugin that crashes is switched off
automatically and named in the log. If the mod doesn't load at all, check
`ScriptHookVDotNet.log` in the GTA V folder. When reporting a bug, attach both logs.

---

## For plugin developers

Plugins are .NET Framework 4.8 (x64) class libraries that reference `Callsign.API.dll`, subclass
`Callsign.API.Plugin`, and go in `<GTA V folder>\Callsign\Plugins\`. They can follow duty and
calls, and add their own callout content (situations, locations, suspects, outcomes) that mixes
with the built-in calls. Built-in content uses the same API.

- **[Plugin guide](docs/PLUGINS.md)**: setup, lifecycle, rules, the full API and how to add callouts.
- **[`samples/ExamplePlugin`](samples/ExamplePlugin)**: a complete project to start from.

---

## Roadmap
- [x] On/off duty
- [x] Plugin API foundation
- [x] Callouts assembled from parts (plugins can add parts)
- [ ] Mid-call twists
- [x] Arrests: cuffing, transport, booking
- [x] Stopping and questioning suspects; vehicle calls and pursuits
- [x] Traffic stops
- [ ] Pedestrian stops
- [ ] Pacing director: calm after chaos, no repeats back-to-back
- [ ] Pick a mode on every start: **Career** or **Free patrol** (everything unlocked)
- [ ] Career: rookie in Los Santos with a field training officer → Officer → Senior Officer → Sergeant → specialisations
- [ ] Backup and support units, pursuit tactics, investigations

## Building from source
Requires the .NET SDK 10 or newer (Windows). From the repository root:

```powershell
.\build.ps1                               # build everything + run unit tests
.\deploy.ps1 -GamePath "D:\Games\GTA V"   # build, then install into a game folder
```
`deploy.ps1` also accepts `-WithExamplePlugin` to install the sample plugin.

## License
Callsign is released under the [MIT License](LICENSE).

`lib/ScriptHookVDotNet3.dll` is included only as a compile reference. It is part of
[ScriptHookVDotNet](https://github.com/scripthookvdotnet/scripthookvdotnet) by crosire and
contributors, under the zlib license ([lib/LICENSE-ScriptHookVDotNet.txt](lib/LICENSE-ScriptHookVDotNet.txt)).

Callsign is a fan-made mod, not affiliated with or endorsed by Rockstar Games or Take-Two Interactive.

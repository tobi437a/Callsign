# Callsign

A police career mod for **GTA V Story Mode**. Start as a rookie on probation, work your shifts,
and earn your way up the ranks, unlocking new gear, vehicles, districts and call types as you go.

Calls are not fixed scripts. Each incident is assembled from parts (where it happens, what's
going on, who the suspect really is, what goes wrong halfway through), so the same call can
play out very differently twice. Other developers can add their own callouts and content
through a plugin API.

> **Single player only.** Never launch GTA Online with this mod (or any ScriptHookV mod)
> installed. Remove the files listed under [Uninstalling](#uninstalling) first.

> **Early development.** Right now the mod has the on/off duty system, generated callouts and
> the plugin API. Arrests, traffic stops and the career are being built. See [Roadmap](#roadmap).

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
| `E` | Arrest the suspect (on foot, standing next to them). |
| `H` | Let the suspect go with a warning. |
| `End` | Call off the current callout. |
| `F9` | Ask dispatch for a call right now. |

While on duty, dispatch offers a call every minute or two. Accept it, follow the yellow route,
and find the suspect: they might cooperate, run, or fight. Runners give up when you catch them on
foot; fighters give up once stunned or hurt. Arrested suspects follow you in cuffs: walk them to
your car and get in, they take a back seat. Then drive them to the police station marked on the map
to book them.

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

Plugins can react to what happens in the mod and, as the API grows, add their own callouts,
locations, suspect behaviours and more. Built-in content uses the same API, so plugins are not
second-class citizens.

### Setup
Create a .NET Framework 4.8 class library (x64) and reference `Callsign.API.dll` and
`ScriptHookVDotNet3.dll`. Neither should be copied into your output, because the game already has them:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <PlatformTarget>x64</PlatformTarget>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="Callsign.API">
      <HintPath>path\to\Callsign.API.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="ScriptHookVDotNet3">
      <HintPath>path\to\ScriptHookVDotNet3.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
</Project>
```

### A minimal plugin

```csharp
using Callsign.API;
using Callsign.API.Events;

public sealed class MyPlugin : Plugin
{
    ICallsignApi _api;

    public override string Name => "My Plugin";
    public override string Author => "You";

    public override void OnLoad(ICallsignApi api)
    {
        _api = api;
        api.Duty.DutyChanged += OnDutyChanged;
        api.Log.Info("Loaded!");
    }

    void OnDutyChanged(object sender, DutyChangedEventArgs e)
    {
        GTA.UI.Notification.PostTicker(e.IsOnDuty ? "Stay safe out there." : "Shift over.", false, false);
    }

    public override void OnUnload()
    {
        // Delete anything you spawned. Event subscriptions are removed for you.
    }
}
```

Build it and drop the DLL into `<GTA V folder>\Callsign\Plugins\`. A complete project is in
[`samples/ExamplePlugin`](samples/ExamplePlugin).

### Lifecycle
- `OnLoad(api)`: once when the mod starts, and again after every script reload. Keep `api`:
  it is your plugin's own handle to the mod.
- `OnTick()`: every frame, after the mod's own features have run.
- `OnUnload()`: when scripts reload or the game exits. Clean up what you spawned.

All callbacks run on the game's script thread.

### Rules
- **Don't subclass `GTA.Script`** in a plugin, and don't call `Script.Wait`. The mod runs your
  plugin itself; blocking would freeze the whole mod. Use `OnTick` and keep state between frames.
- **Exceptions switch your plugin off.** If a callback or event handler throws, the plugin is
  disabled for the session and the error is written to `Callsign.log` with your plugin's name.
  The mod and other plugins keep running.
- **Log through `api.Log`.** Messages go to `Callsign.log`, prefixed with your plugin's name.

### What the API offers today

| Member | Description |
|---|---|
| `ICallsignApi.ApiVersion` | Version of the API the running mod implements. |
| `ICallsignApi.Duty.IsOnDuty` | Whether the player is on duty. |
| `ICallsignApi.Duty.SetOnDuty(bool)` | Put the player on or off duty (same as pressing the key). |
| `ICallsignApi.Duty.DutyChanged` | Raised after the player goes on or off duty. |
| `ICallsignApi.Log` | `Info` / `Warn` / `Error` into `Callsign.log`. |
| `ICallsignApi.Callouts.AddSituation / AddLocation / AddSuspect / AddOutcome` | Add callout parts (since 0.2). |
| `ICallsignApi.Callouts.Current` | The call currently offered or running, or null. |
| `ICallsignApi.Callouts.CalloutOffered / CalloutAccepted / CalloutEnded` | Follow calls as they happen. |

Coming as the features are built: fully custom callouts, more part kinds (twists), suspect
behaviours, interaction-menu options, more events (pursuits, arrests, shifts, promotions), and
services for spawning, backup and the career.

### Adding callout content
Every call is drawn as **situation × location × suspect × outcome** from all registered parts,
built-in and from every plugin. Situations pick the other parts by tag, so what you add mixes
with everything else:

```csharp
api.Callouts.AddSituation(new CalloutSituation(
    "myplugin-noise-complaint", "Noise complaint",
    "Neighbours report a {suspect} playing loud music near {location}.",
    locationTags: new[] { "public" }, outcomeTags: new[] { "calm", "violent" }));

api.Callouts.AddLocation(new CalloutLocation(
    "myplugin-pillbox", "Pillbox Hill Medical Center", new Vector3(298f, -584f, 43f),
    new[] { "public", "city" }));
```

- Ids must be unique across all plugins: start them with your plugin's name.
- Built-in tags: locations `store`, `public`, `street`, `city`, `county`; suspects `rowdy`,
  `armed`; outcomes `calm`, `runs`, `violent`.
- Your parts are removed when your plugin unloads.

### Compatibility
`Callsign.API` follows semantic versioning. Within a major version, updates only **add**
things: a plugin built against 1.2 keeps working on 1.5. Anything due for removal is marked
`[Obsolete]` first and only removed in the next major version. Check `api.ApiVersion` if you
rely on something newer than your minimum.

### Fast iteration
You don't need to restart the game. Overwrite your DLL in `Callsign\Plugins\` while the game
is running and press **Insert** (ScriptHookVDotNet's reload key) to load the new version.
Plugin DLLs are never locked by the game.

---

## Roadmap
- [x] On/off duty
- [x] Plugin API foundation
- [x] Callouts assembled from parts (plugins can add parts)
- [ ] Mid-call twists
- [ ] Arrests: cuffing, transport, booking
- [ ] Traffic and pedestrian stops
- [ ] Pacing director: calm after chaos, no repeats back-to-back
- [ ] Career: rookie with a field training officer → Officer → Senior Officer → Sergeant → specialisations
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

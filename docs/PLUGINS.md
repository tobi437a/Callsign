# Callsign plugin guide

Everything you need to write a plugin for [Callsign](../README.md). Plugins can react to what
happens in the mod and, as the API grows, add their own callouts, locations, suspect behaviours
and more. Built-in content uses the same API, so plugins are not
second-class citizens.

## Setup
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

## A minimal plugin

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
[`samples/ExamplePlugin`](../samples/ExamplePlugin).

## Lifecycle
- `OnLoad(api)`: once when the mod starts, and again after every script reload. Keep `api`:
  it is your plugin's own handle to the mod.
- `OnTick()`: every frame, after the mod's own features have run.
- `OnUnload()`: when scripts reload or the game exits. Clean up what you spawned.

All callbacks run on the game's script thread.

## Rules
- **Don't subclass `GTA.Script`** in a plugin, and don't call `Script.Wait`. The mod runs your
  plugin itself; blocking would freeze the whole mod. Use `OnTick` and keep state between frames.
- **Exceptions switch your plugin off.** If a callback or event handler throws, the plugin is
  disabled for the session and the error is written to `Callsign.log` with your plugin's name.
  The mod and other plugins keep running.
- **Log through `api.Log`.** Messages go to `Callsign.log`, prefixed with your plugin's name.

## What the API offers today

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

## Adding callout content
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
- See [CALLOUTS.md](CALLOUTS.md) for the built-in calls your parts mix with.
- Built-in tags: locations `store`, `public`, `street`, `city`, `county`; suspects `rowdy`,
  `armed`, `robber`, and in vehicles `thief`, `dui`, `gang`; outcomes `calm`, `runs`, `violent`.
- Vehicle calls (since 0.4): set `InVehicle = true` on the situation and give suspects
  `Vehicles = new[] { "sultan" }`. A vehicle situation only uses suspects with vehicles, the others
  only suspects on foot.
- Several suspects (since 0.4): set `MinSuspects` / `MaxSuspects` (up to 4) on the situation. Each
  suspect rolls their own outcome. `{count}` in the dispatch text becomes the number, as a word.
- Your parts are removed when your plugin unloads.

## Compatibility
`Callsign.API` follows semantic versioning. Within a major version, updates only **add**
things: a plugin built against 1.2 keeps working on 1.5. Anything due for removal is marked
`[Obsolete]` first and only removed in the next major version. Check `api.ApiVersion` if you
rely on something newer than your minimum.

## Fast iteration
You don't need to restart the game. Overwrite your DLL in `Callsign\Plugins\` while the game
is running and press **Insert** (ScriptHookVDotNet's reload key) to load the new version.
Plugin DLLs are never locked by the game.

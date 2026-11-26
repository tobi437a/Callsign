// =====================================================================
//  CallsignScript  -  GTA V Story Mode police mod (SHVDN 3)
//  SINGLE PLAYER ONLY. Do not load this and go into GTA Online.
//
//  Composition root: the ONLY Script subclass in the mod. It builds the
//  services and modules, wires them, and forwards Tick/KeyDown/Aborted.
//  Feature logic does not belong here (see CLAUDE.md, Architecture).
// =====================================================================
using System;
using System.Collections.Generic;
using GTA;
using Callsign.Core.Arrest;
using Callsign.Core.Callouts;
using Callsign.Core.Duty;
using Callsign.Core.Entities;
using Callsign.Core.Events;
using Callsign.Core.Interaction;
using Callsign.Core.Suspects;
using Callsign.Core.Ports;
using Callsign.Dev;
using Callsign.Engine;
using Callsign.Modules;
using Callsign.Plugins;

namespace Callsign
{
    public sealed class CallsignScript : Script
    {
        readonly FileLog _log;
        readonly KeyInput _input = new KeyInput();
        readonly List<IModule> _modules = new List<IModule>();
        readonly EntityScopes _scopes;

        public CallsignScript()
        {
            ModPaths paths = ModPaths.Detect();
            _log = new FileLog(paths.LogFile);

            // --- services ---
            var bus = new EventBus(ex => _log.Error("Event handler threw", ex));
            var duty = new DutyService(bus);
            _scopes = new EntityScopes(_log);
            var random = new SeededRandom();

            var parts = new CalloutParts();
            int partCount = CalloutContentLoader.LoadDirectory(System.IO.Path.Combine(paths.Data, "Callouts"), parts, _log);
            _log.Info("Loaded " + partCount + " callout part(s)");
            List<PoliceStation> stations = StationLoader.LoadDirectory(System.IO.Path.Combine(paths.Data, "Arrest"), _log);
            _log.Info("Loaded " + stations.Count + " police station(s)");
            var suspectContent = new SuspectContent();
            SuspectContentLoader.LoadDirectory(System.IO.Path.Combine(paths.Data, "Suspects"), suspectContent, _log);
            foreach (string missing in suspectContent.Missing())
                _log.Warn("Suspect content has no " + missing + " (check Data\\Suspects)");

            var world = new GameWorld();
            var blips = new GameBlips();
            var notifier = new GameNotifier();
            var clock = new GameClock();
            var custody = new CustodyServices(world, blips, notifier, clock, stations, new CustodySettings());
            var interaction = new InteractionServices(world, notifier, new GameMenuView(), clock, random,
                new Dialogue(suspectContent, random), new InteractionSettings());
            var sceneServices = new CalloutSceneServices(_scopes, new GamePedFactory(), new GameVehicleFactory(), blips,
                world, notifier, clock, random, _log, new CalloutSettings(), new SuspectProfileGenerator(suspectContent, random),
                interaction, custody);
            var callouts = new CalloutService(new IncidentGenerator(parts, random), duty, bus, clock, random, sceneServices);

            // --- modules, in tick order (dev bridge first so its keys count this tick; plugins last) ---
            if (System.IO.Directory.Exists(paths.Dev))
                _modules.Add(new DevBridgeModule(paths.Dev, duty, callouts, _scopes, _input, _log));
            _modules.Add(new DutyModule(duty, bus, _input));
            _modules.Add(new CalloutsModule(callouts, _input));
            _modules.Add(new PluginsModule(paths.Plugins, duty, parts, callouts, bus, _log));

            foreach (IModule m in _modules)
                Run(m, "Start", m.Start);

            Tick += OnTick;
            KeyDown += (s, e) => _input.Press(e.KeyCode);
            Aborted += OnAborted;

            GTA.UI.Screen.ShowSubtitle("Callsign loaded - press F7 to go on duty", 4000);
        }

        void OnTick(object sender, EventArgs e)
        {
            foreach (IModule m in _modules)
                Run(m, "Tick", m.Tick);
            _input.EndFrame();
        }

        void OnAborted(object sender, EventArgs e)
        {
            for (int i = _modules.Count - 1; i >= 0; i--)
                Run(_modules[i], "Stop", _modules[i].Stop);
            // Safety net: whatever a module left open is deleted now, so a reload leaves no ghost peds.
            int leaked = _scopes.CloseAll(ReleaseMode.Delete);
            if (leaked > 0) _log.Warn(leaked + " entity scope(s) were still open at stop; deleted them");
            _log.Info("Stopped");
        }

        void Run(IModule module, string phase, Action action)
        {
            try { action(); }
            catch (Exception ex) { _log.Error("Module " + module.Name + " failed in " + phase, ex); }
        }
    }
}

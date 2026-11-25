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
using Callsign.Core.Duty;
using Callsign.Core.Entities;
using Callsign.Core.Events;
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

            // --- modules, in tick order (plugins last) ---
            _modules.Add(new DutyModule(duty, bus, _input));
            _modules.Add(new PluginsModule(paths.Plugins, duty, bus, _log));

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

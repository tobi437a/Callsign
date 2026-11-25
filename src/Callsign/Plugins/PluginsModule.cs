using System;
using System.Collections.Generic;
using Callsign.API;
using Callsign.Core.Duty;
using Callsign.Core.Events;
using Callsign.Core.Ports;
using Callsign.Modules;

namespace Callsign.Plugins
{
    /// <summary>
    /// Hosts third-party plugins: loads them, gives each its own <see cref="PluginApi"/>, ticks
    /// them, and isolates failures (a plugin that throws is unloaded and logged).
    /// Registered last, so plugins see the frame after the mod's own modules ran.
    /// </summary>
    internal sealed class PluginsModule : IModule
    {
        readonly string _pluginDirectory;
        readonly DutyService _duty;
        readonly IEventBus _bus;
        readonly ILog _log;
        readonly List<LoadedPlugin> _loaded = new List<LoadedPlugin>();
        PluginLoader? _loader;

        public PluginsModule(string pluginDirectory, DutyService duty, IEventBus bus, ILog log)
        {
            _pluginDirectory = pluginDirectory;
            _duty = duty;
            _bus = bus;
            _log = log;
        }

        public string Name => "Plugins";

        public void Start()
        {
            _loader = new PluginLoader(_pluginDirectory, _log);
            foreach (Plugin plugin in _loader.LoadAll())
            {
                var loaded = new LoadedPlugin(plugin, new PluginApi(SafeName(plugin), _duty, _bus, _log));
                if (Guard(loaded, "OnLoad", () => plugin.OnLoad(loaded.Api)))
                {
                    _loaded.Add(loaded);
                    _log.Info("Loaded " + SafeName(plugin) + " " + plugin.Version + " by " + plugin.Author);
                }
            }
            _log.Info(_loaded.Count + " plugin(s) running, API " + PluginApi.CurrentApiVersion);
        }

        public void Tick()
        {
            for (int i = _loaded.Count - 1; i >= 0; i--)
            {
                var p = _loaded[i];
                if (!Guard(p, "OnTick", p.Plugin.OnTick)) _loaded.RemoveAt(i);
            }
        }

        public void Stop()
        {
            foreach (var p in _loaded) Guard(p, "OnUnload", p.Plugin.OnUnload);
            _loaded.Clear();
            _loader?.Dispose();
        }

        /// <summary>Runs a plugin callback; on exception logs it, detaches the plugin's API
        /// and returns false so the caller drops the plugin.</summary>
        bool Guard(LoadedPlugin p, string callback, Action action)
        {
            try { action(); return true; }
            catch (Exception ex)
            {
                _log.Error("Plugin " + SafeName(p.Plugin) + " threw in " + callback + " and was disabled", ex);
                p.Api.Dispose();
                if (callback != "OnUnload") GTA.UI.Notification.PostTicker("~r~Callsign: plugin " + SafeName(p.Plugin) + " crashed~s~ (see Callsign.log)", false, false);
                return false;
            }
        }

        static string SafeName(Plugin plugin)
        {
            try { return plugin.Name; } catch { return plugin.GetType().Name; }
        }

        sealed class LoadedPlugin
        {
            public LoadedPlugin(Plugin plugin, PluginApi api) { Plugin = plugin; Api = api; }
            public Plugin Plugin { get; }
            public PluginApi Api { get; }
        }
    }
}

using System;

namespace Callsign.API
{
    /// <summary>
    /// Base class for every Callsign plugin. Put a public, non-abstract subclass with a
    /// parameterless constructor in a DLL under <c>&lt;GTA V&gt;\Callsign\Plugins\</c>.
    /// </summary>
    /// <remarks>
    /// Do NOT subclass <c>GTA.Script</c> in a plugin: Callsign ticks plugins itself, on the
    /// game's script thread, after its own modules. All callbacks run on that thread.
    /// An exception thrown from a callback disables the plugin (and is logged); it never
    /// takes the mod down.
    /// </remarks>
    public abstract class Plugin
    {
        /// <summary>Display name, used in logs and notifications.</summary>
        public abstract string Name { get; }

        /// <summary>Author shown in the log.</summary>
        public virtual string Author { get { return "Unknown"; } }

        /// <summary>Plugin version. Defaults to the plugin assembly's version.</summary>
        public virtual Version Version { get { return GetType().Assembly.GetName().Version; } }

        /// <summary>Called once after Callsign starts (also after every script reload).
        /// Keep <paramref name="api"/>: it is this plugin's handle to the mod.</summary>
        public abstract void OnLoad(ICallsignApi api);

        /// <summary>Called every game frame while the plugin is loaded.</summary>
        public virtual void OnTick() { }

        /// <summary>Called when Callsign stops (script reload / game exit). Clean up spawned
        /// entities here. Event subscriptions made through the API are removed automatically.</summary>
        public virtual void OnUnload() { }
    }
}

using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace Callsign.Core.Settings
{
    /// <summary>
    /// The player's choices for the mod, saved to <c>Callsign\settings.xml</c>. One section per
    /// feature; a new setting is a new property here (with a default) plus an item on a settings page.
    /// Elements missing from an older file keep their defaults, unknown ones are ignored.
    /// </summary>
    [XmlRoot("CallsignSettings")]
    public sealed class ModSettings
    {
        public CalloutPreferences Callouts { get; set; } = new CalloutPreferences();
    }

    /// <summary>Which callouts dispatch may offer.</summary>
    public sealed class CalloutPreferences
    {
        /// <summary>Situation ids the player switched off. Ids, not titles, so a call keeps its setting if it is renamed.</summary>
        [XmlArray("Disabled"), XmlArrayItem("Situation")]
        public List<string> Disabled { get; set; } = new List<string>();

        public bool IsEnabled(string situationId) =>
            !Disabled.Exists(id => string.Equals(id, situationId, StringComparison.OrdinalIgnoreCase));

        public void SetEnabled(string situationId, bool enabled)
        {
            Disabled.RemoveAll(id => string.Equals(id, situationId, StringComparison.OrdinalIgnoreCase));
            if (!enabled) Disabled.Add(situationId);
        }
    }

    /// <summary>Published after a setting changed (and was saved).</summary>
    public sealed record SettingsChanged(ModSettings Settings);
}

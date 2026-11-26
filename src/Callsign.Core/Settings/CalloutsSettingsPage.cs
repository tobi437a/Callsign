using System;
using System.Collections.Generic;
using System.Linq;
using Callsign.Core.Callouts;
using Callsign.Core.Ui;

namespace Callsign.Core.Settings
{
    /// <summary>Switches single callouts on or off: one item per registered situation, built-in and plugin ones alike.</summary>
    public sealed class CalloutsSettingsPage : ISettingsPage
    {
        /// <summary>"Switch all on" while any call is off, else "Switch all off".</summary>
        public const string AllId = "all";
        const string SituationPrefix = "situation:";

        readonly CalloutParts _parts;
        readonly SettingsService _settings;

        public CalloutsSettingsPage(CalloutParts parts, SettingsService settings)
        {
            _parts = parts;
            _settings = settings;
        }

        public string Title => "Callouts";

        /// <summary>The menu item id for a situation.</summary>
        public static string ItemId(string situationId) => SituationPrefix + situationId;

        public IEnumerable<MenuItem> Items()
        {
            CalloutPreferences prefs = _settings.Current.Callouts;
            foreach (SituationPart s in _parts.Situations.All.OrderBy(s => s.Title, StringComparer.OrdinalIgnoreCase))
                yield return new MenuItem(ItemId(s.Id), s.Title, Value: prefs.IsEnabled(s.Id) ? "~g~On" : "~r~Off");
            yield return new MenuItem(AllId, AnyOff(prefs) ? "Switch all on" : "Switch all off");
        }

        bool AnyOff(CalloutPreferences prefs) => _parts.Situations.All.Any(s => !prefs.IsEnabled(s.Id));

        public void Choose(string id)
        {
            if (id == AllId)
            {
                bool turnOn = AnyOff(_settings.Current.Callouts);
                _settings.Change(s =>
                {
                    foreach (SituationPart situation in _parts.Situations.All) s.Callouts.SetEnabled(situation.Id, turnOn);
                });
            }
            else if (id.StartsWith(SituationPrefix, StringComparison.Ordinal))
            {
                string situation = id.Substring(SituationPrefix.Length);
                _settings.Change(s => s.Callouts.SetEnabled(situation, !s.Callouts.IsEnabled(situation)));
            }
        }
    }
}

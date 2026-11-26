using System;
using Callsign.Core.Events;
using Callsign.Core.Ports;

namespace Callsign.Core.Settings
{
    /// <summary>
    /// Owns the player's settings: features read <see cref="Current"/>, changes go through
    /// <see cref="Change"/>, which saves them and publishes <see cref="SettingsChanged"/>.
    /// </summary>
    public sealed class SettingsService
    {
        readonly ISettingsStore _store;
        readonly IEventBus _bus;

        public SettingsService(ISettingsStore store, IEventBus bus)
        {
            _store = store;
            _bus = bus;
            Current = store.Load();
        }

        public ModSettings Current { get; }

        public void Change(Action<ModSettings> change)
        {
            change(Current);
            _store.Save(Current);
            _bus.Publish(new SettingsChanged(Current));
        }
    }
}

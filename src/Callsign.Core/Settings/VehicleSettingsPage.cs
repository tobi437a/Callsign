using System.Collections.Generic;
using Callsign.Core.Ports;
using Callsign.Core.Ui;

namespace Callsign.Core.Settings
{
    /// <summary>Things to do with the player's vehicle.</summary>
    public sealed class VehicleSettingsPage : ISettingsPage
    {
        public const string RepairId = "repair";

        readonly IPlayerVehicle _vehicle;
        readonly INotifier _notifier;

        public VehicleSettingsPage(IPlayerVehicle vehicle, INotifier notifier)
        {
            _vehicle = vehicle;
            _notifier = notifier;
        }

        public string Title => "Vehicle";

        public IEnumerable<MenuItem> Items()
        {
            bool available = _vehicle.Available;
            yield return new MenuItem(RepairId, "Repair vehicle", Enabled: available, Value: available ? null : "~c~no vehicle nearby");
        }

        public void Choose(string id)
        {
            if (id == RepairId && _vehicle.Repair()) _notifier.Notify("Vehicle ~g~repaired~s~.");
        }
    }
}

using Callsign.API;
using Callsign.API.Events;

namespace ExamplePlugin
{
    /// <summary>Minimal plugin: greets the player when they go on or off duty.</summary>
    public sealed class ExamplePlugin : Plugin
    {
        ICallsignApi? _api;

        public override string Name => "Example Plugin";
        public override string Author => "Callsign";

        public override void OnLoad(ICallsignApi api)
        {
            _api = api;
            api.Duty.DutyChanged += OnDutyChanged;
            api.Log.Info("Hello from the example plugin (API " + api.ApiVersion + ")");
        }

        void OnDutyChanged(object? sender, DutyChangedEventArgs e)
        {
            GTA.UI.Notification.PostTicker(
                e.IsOnDuty ? "~g~[Example plugin]~s~ Stay safe out there." : "~g~[Example plugin]~s~ Shift over.",
                false, false);
        }

        public override void OnUnload()
        {
            _api?.Log.Info("Goodbye");
        }
    }
}

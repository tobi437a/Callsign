using Callsign.API;
using Callsign.API.Callouts;
using Callsign.API.Events;
using GTA.Math;

namespace ExamplePlugin
{
    /// <summary>
    /// Minimal plugin: greets the player on duty changes, adds a callout situation and a location,
    /// and logs how calls end.
    /// </summary>
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

            // A new situation. It reuses the built-in "public" locations, all suspects and the
            // calm/violent outcomes, so it plays anywhere right away.
            api.Callouts.AddSituation(new CalloutSituation(
                "example-noise-complaint", "Noise complaint",
                "Neighbours report a {suspect} playing loud music near {location}.",
                locationTags: new[] { "public" },
                outcomeTags: new[] { "calm", "violent" }));

            // A new location. Built-in situations that want "public" places can use it too.
            api.Callouts.AddLocation(new CalloutLocation(
                "example-pillbox-hill", "Pillbox Hill Medical Center",
                new Vector3(298f, -584f, 43f), new[] { "public", "city" }));

            api.Callouts.CalloutEnded += OnCalloutEnded;
        }

        void OnDutyChanged(object? sender, DutyChangedEventArgs e)
        {
            GTA.UI.Notification.PostTicker(
                e.IsOnDuty ? "~g~[Example plugin]~s~ Stay safe out there." : "~g~[Example plugin]~s~ Shift over.",
                false, false);
        }

        void OnCalloutEnded(object? sender, CalloutEndedEventArgs e)
        {
            _api?.Log.Info("Call '" + e.Callout.Title + "' at " + e.Callout.LocationName + " ended: " + e.Reason);
        }

        public override void OnUnload()
        {
            // Parts and event subscriptions made through the API are removed automatically.
            _api?.Log.Info("Goodbye");
        }
    }
}

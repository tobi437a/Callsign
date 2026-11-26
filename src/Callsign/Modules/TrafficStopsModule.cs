using Callsign.Core.Callouts;
using Callsign.Core.TrafficStops;
using Callsign.Core.Ui;
using Callsign.Engine;
using Keys = System.Windows.Forms.Keys;

namespace Callsign.Modules
{
    /// <summary>Keys for traffic stops; the logic lives in <see cref="TrafficStopService"/>.</summary>
    internal sealed class TrafficStopsModule : IModule
    {
        const Keys PullOverKey = Keys.ShiftKey;
        const int PullOverHoldMs = 500;
        const Keys InteractKey = Keys.E;
        const int StopHoldMs = 500;
        const Keys CancelKey = Keys.End;

        readonly TrafficStopService _stops;
        readonly KeyInput _input;

        public TrafficStopsModule(TrafficStopService stops, KeyInput input)
        {
            _stops = stops;
            _input = input;
        }

        public string Name => "TrafficStops";

        public void Start() { }

        public void Tick()
        {
            if (_input.WasPressed(CancelKey)) _stops.Cancel();
            var menu = new MenuInput(_input.WasPressed(Keys.Up), _input.WasPressed(Keys.Down),
                _input.WasPressed(Keys.Enter), _input.WasPressed(Keys.Back));
            var scene = new CalloutInput(_input.WasHeld(InteractKey, StopHoldMs), _input.WasPressed(InteractKey), menu);
            _stops.Tick(new TrafficStopInput(_input.WasHeld(PullOverKey, PullOverHoldMs), scene));
        }

        public void Stop() => _stops.Stop();
    }
}

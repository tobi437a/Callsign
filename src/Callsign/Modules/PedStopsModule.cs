using Callsign.Core.Callouts;
using Callsign.Core.PedStops;
using Callsign.Core.Ui;
using Callsign.Engine;
using Keys = System.Windows.Forms.Keys;

namespace Callsign.Modules
{
    /// <summary>Keys for pedestrian stops; the logic lives in <see cref="PedStopService"/>.</summary>
    internal sealed class PedStopsModule : IModule
    {
        const Keys InteractKey = Keys.E;
        const int StopHoldMs = 500;
        const Keys CancelKey = Keys.End;

        readonly PedStopService _stops;
        readonly KeyInput _input;

        public PedStopsModule(PedStopService stops, KeyInput input)
        {
            _stops = stops;
            _input = input;
        }

        public string Name => "PedStops";

        public void Start() { }

        public void Tick()
        {
            if (_input.WasPressed(CancelKey)) _stops.Cancel();
            var menu = new MenuInput(_input.WasPressed(Keys.Up), _input.WasPressed(Keys.Down),
                _input.WasPressed(Keys.Enter), _input.WasPressed(Keys.Back));
            _stops.Tick(new CalloutInput(_input.WasHeld(InteractKey, StopHoldMs), _input.WasPressed(InteractKey), menu));
        }

        public void Stop() => _stops.Stop();
    }
}

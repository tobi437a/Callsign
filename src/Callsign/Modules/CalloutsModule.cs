using Callsign.Core.Callouts;
using Callsign.Engine;
using Keys = System.Windows.Forms.Keys;

namespace Callsign.Modules
{
    /// <summary>Keys for callouts; the callout logic itself lives in <see cref="CalloutService"/>.</summary>
    internal sealed class CalloutsModule : IModule
    {
        const Keys AcceptKey = Keys.Y;
        const Keys DeclineKey = Keys.N;
        const Keys InteractKey = Keys.E;
        const Keys ReleaseKey = Keys.H;
        const Keys CancelKey = Keys.End;
        const Keys RequestKey = Keys.F9;

        readonly CalloutService _callouts;
        readonly KeyInput _input;

        public CalloutsModule(CalloutService callouts, KeyInput input)
        {
            _callouts = callouts;
            _input = input;
        }

        public string Name => "Callouts";

        public void Start() { }

        public void Tick()
        {
            if (_input.WasPressed(AcceptKey)) _callouts.Accept();
            if (_input.WasPressed(DeclineKey)) _callouts.Decline();
            if (_input.WasPressed(CancelKey)) _callouts.Cancel();
            if (_input.WasPressed(RequestKey)) _callouts.RequestNow();

            _callouts.Tick(new CalloutInput(_input.WasPressed(InteractKey), _input.WasPressed(ReleaseKey)));

            if (_callouts.Current == CalloutService.Phase.Offered)
                GTA.UI.Screen.ShowHelpTextThisFrame("~y~Y~s~ accept call    ~y~N~s~ decline");
        }

        public void Stop() => _callouts.Stop();
    }
}

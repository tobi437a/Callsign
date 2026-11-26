using GTA;

namespace Callsign.Engine
{
    internal sealed class GameControls : Callsign.Core.Ports.IGameControls
    {
        // Context = E on foot: hail a taxi, shop/door prompts, ...
        public void BlockInteractKey() => Game.DisableControlThisFrame(Control.Context);
    }
}

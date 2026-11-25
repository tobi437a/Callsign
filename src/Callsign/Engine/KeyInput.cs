using System.Collections.Generic;
using System.Windows.Forms;

namespace Callsign.Engine
{
    /// <summary>Keys pressed since the last tick. The bootstrap feeds it; modules poll it.</summary>
    internal sealed class KeyInput
    {
        readonly HashSet<Keys> _pressed = new HashSet<Keys>();

        public void Press(Keys key) => _pressed.Add(key);

        public bool WasPressed(Keys key) => _pressed.Contains(key);

        /// <summary>Called by the bootstrap after all modules have ticked.</summary>
        public void EndFrame() => _pressed.Clear();
    }
}

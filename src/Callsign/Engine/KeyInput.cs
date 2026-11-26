using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace Callsign.Engine
{
    /// <summary>
    /// Keys pressed since the last tick, and keys being held. The bootstrap feeds it; modules poll it.
    /// A press counts once per physical press (Windows' key repeat while held is ignored).
    /// </summary>
    internal sealed class KeyInput
    {
        readonly HashSet<Keys> _pressed = new HashSet<Keys>();
        readonly Dictionary<Keys, int> _downSince = new Dictionary<Keys, int>();
        readonly HashSet<Keys> _holdReported = new HashSet<Keys>();
        readonly Dictionary<Keys, int> _injectedUntil = new Dictionary<Keys, int>();

        public void Press(Keys key)
        {
            if (_downSince.ContainsKey(key)) return;   // key repeat
            _downSince[key] = Environment.TickCount;
            _pressed.Add(key);
        }

        /// <summary>A synthetic press (dev tools): pressed now and held for <paramref name="holdMs"/>.</summary>
        public void Inject(Keys key, int holdMs)
        {
            _downSince.Remove(key);
            _holdReported.Remove(key);
            Press(key);
            _injectedUntil[key] = Environment.TickCount + holdMs;
        }

        public bool WasPressed(Keys key) => _pressed.Contains(key);

        /// <summary>True on the one tick where <paramref name="key"/> has been held for <paramref name="ms"/>.</summary>
        public bool WasHeld(Keys key, int ms)
        {
            if (!_downSince.TryGetValue(key, out int since) || _holdReported.Contains(key)) return false;
            if (Environment.TickCount - since < ms) return false;
            _holdReported.Add(key);
            return true;
        }

        /// <summary>Called by the bootstrap after all modules have ticked.</summary>
        public void EndFrame()
        {
            _pressed.Clear();
            // Polling instead of KeyUp events: a key released while the game was unfocused still gets let go.
            var released = new List<Keys>();
            foreach (Keys key in _downSince.Keys)
            {
                if (_injectedUntil.TryGetValue(key, out int until))
                {
                    if (Environment.TickCount - until < 0) continue;
                    _injectedUntil.Remove(key);
                }
                if (!GTA.Game.IsKeyPressed(key)) released.Add(key);
            }
            foreach (Keys key in released)
            {
                _downSince.Remove(key);
                _holdReported.Remove(key);
            }
        }
    }
}

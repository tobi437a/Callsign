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
        /// <summary>Keys whose hold has fired, and the frame it fired in (every module asking that frame sees it).</summary>
        readonly Dictionary<Keys, int> _holdReported = new Dictionary<Keys, int>();
        int _frame;
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

        /// <summary>
        /// Takes <paramref name="key"/> away from the modules after this one, for this press: it no
        /// longer counts as pressed this tick or as held until it is let go (an open menu owns its keys).
        /// </summary>
        public void Consume(Keys key)
        {
            _pressed.Remove(key);
            if (_downSince.ContainsKey(key)) _holdReported[key] = -1;   // a frame that never comes: WasHeld stays false
        }

        /// <summary>
        /// True on the one tick where <paramref name="key"/> has been held for <paramref name="ms"/>
        /// (for every caller that tick, so modules don't steal it from each other).
        /// </summary>
        public bool WasHeld(Keys key, int ms)
        {
            if (_holdReported.TryGetValue(key, out int frame)) return frame == _frame;
            if (!_downSince.TryGetValue(key, out int since) || Environment.TickCount - since < ms) return false;
            _holdReported[key] = _frame;
            return true;
        }

        /// <summary>Called by the bootstrap after all modules have ticked.</summary>
        public void EndFrame()
        {
            _pressed.Clear();
            _frame++;
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

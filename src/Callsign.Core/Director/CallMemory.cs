using System;
using System.Collections.Generic;

namespace Callsign.Core.Director
{
    /// <summary>
    /// Anti-repeat memory for one kind of part: the last few ids used, most recent first. A remembered
    /// id gets a factor below 1, lowest when it was the very last one, recovering as newer ones push it out.
    /// </summary>
    public sealed class CallMemory
    {
        readonly List<string> _recent = new List<string>();
        readonly int _capacity;
        readonly double _strength;

        /// <param name="capacity">How many recent ids are remembered.</param>
        /// <param name="strength">0..1: how hard the last one is pushed down (0.8 = its weight × 0.2).</param>
        public CallMemory(int capacity, double strength)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            _capacity = capacity;
            _strength = Math.Max(0, Math.Min(0.95, strength));
        }

        /// <summary>Most recent first.</summary>
        public IReadOnlyList<string> Recent => _recent;

        public void Remember(string id)
        {
            _recent.Insert(0, id);
            if (_recent.Count > _capacity) _recent.RemoveAt(_recent.Count - 1);
        }

        /// <summary>1 for ids not in memory; each time it's remembered pushes it further down.</summary>
        public double Factor(string id)
        {
            double factor = 1;
            for (int i = 0; i < _recent.Count; i++)
                if (string.Equals(_recent[i], id, StringComparison.OrdinalIgnoreCase))
                    factor *= 1 - _strength * (_capacity - i) / _capacity;
            return factor;
        }

        public void Clear() => _recent.Clear();
    }
}

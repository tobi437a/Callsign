using System;
using System.Collections.Generic;
using Callsign.Core.Ports;

namespace Callsign.Core.Callouts
{
    /// <summary>Weighted random choice.</summary>
    public static class Weighted
    {
        /// <summary>
        /// Picks one item with probability proportional to its weight. Items with weight &lt;= 0 are
        /// never picked. Returns false when nothing can be picked.
        /// </summary>
        public static bool TryPick<T>(IReadOnlyList<T> items, Func<T, double> weight, IRandom random, out T picked)
        {
            double total = 0;
            foreach (T item in items) total += Math.Max(0, weight(item));

            picked = default!;
            if (total <= 0) return false;

            double roll = random.NextDouble() * total;
            T? last = default;
            foreach (T item in items)
            {
                double w = Math.Max(0, weight(item));
                if (w <= 0) continue;
                last = item;
                if (roll < w) { picked = item; return true; }
                roll -= w;
            }
            picked = last!;   // floating-point leftovers land on the last eligible item
            return true;
        }
    }
}

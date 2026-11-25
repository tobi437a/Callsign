using System;

namespace Callsign.Core.Ports
{
    /// <summary><see cref="IRandom"/> on <see cref="System.Random"/>. Used in game and in tests (with a seed).</summary>
    public sealed class SeededRandom : IRandom
    {
        readonly Random _random;

        public SeededRandom() { _random = new Random(); }

        public SeededRandom(int seed) { _random = new Random(seed); }

        public int Next(int maxExclusive) => _random.Next(maxExclusive);

        public int Next(int minInclusive, int maxExclusive) => _random.Next(minInclusive, maxExclusive);

        public double NextDouble() => _random.NextDouble();
    }
}

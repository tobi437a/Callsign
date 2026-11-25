namespace Callsign.Core.Ports
{
    /// <summary>
    /// All randomness goes through this, so tests can use a fixed seed and get the same rolls.
    /// </summary>
    public interface IRandom
    {
        /// <summary>A value in [0, maxExclusive). <paramref name="maxExclusive"/> must be &gt; 0.</summary>
        int Next(int maxExclusive);

        /// <summary>A value in [minInclusive, maxExclusive).</summary>
        int Next(int minInclusive, int maxExclusive);

        /// <summary>A value in [0, 1).</summary>
        double NextDouble();
    }
}
